using System.IO;
using System.IO.Pipes;
using System.Windows;
using Microsoft.Win32;
using Nexonic.Core;

namespace Nexonic.App;

public partial class App : Application
{
    const string PipeName = "nexonic-download-manager";
    Mutex? single;
    MainWindow? window;
    TrayIcon? tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Errors that stop the app are written to a crash report on this computer (Help → Crash Reports).
        AppDomain.CurrentDomain.UnhandledException += (_, x) =>
        {
            if (x.ExceptionObject is Exception ex) CrashReports.Write(ex, "app", HelpWindows.Version);
        };
        DispatcherUnhandledException += (_, x) => CrashReports.Write(x.Exception, "window", HelpWindows.Version);
        TaskScheduler.UnobservedTaskException += (_, x) => { CrashReports.Write(x.Exception, "background task", HelpWindows.Version); x.SetObserved(); };
        // A .torrent file or magnet link the app was opened with goes to the running copy (or this one) to show.
        var torrents = TorrentInbox.Drop(e.Args);
        single = new Mutex(true, "Nexonic.DownloadManager.Single", out var first);
        if (!first)
        {
            // Another copy runs: wake it (it adds the new downloads and shows its window) and quit. Never open a second window.
            // One byte tells the running copy why it was woken: 'i' = new downloads from the browser, 's' = the user opened the app.
            var why = e.Args.Contains("--inbox") && torrents == 0 ? (byte)'i' : (byte)'s';
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try { using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly); c.Connect(1500); c.WriteByte(why); c.Flush(); break; }
                catch { Thread.Sleep(200); }
            }
            Shutdown();
            return;
        }
        Directory.CreateDirectory(AppPaths.Support);
        RegisterBrowserHost();
        var manager = new DownloadManager();
        L.Use(manager.Settings.Language);
        AppTheme.HookWindows();
        AppTheme.Apply(manager.Settings.Theme, manager.Settings.ColorSchemeJson, manager.Settings.FontFamily);
        Translate.Install();
        window = new MainWindow(manager);
        // Started by the browser helper: stay in the tray unless the user wants the window for browser downloads.
        if (!e.Args.Contains("--inbox") || manager.Settings.BringToFrontOnBrowserDownload) window.Show();
        manager.DownloadDialogRequested += job => DownloadWindows.ShowInfo(manager, job);
        manager.TorrentRequested += source => { ShowWindow(); Dialogs.Torrent(window!, manager, source, DownloadQueue.MainId); };
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(manager.CollectTorrents));
        manager.ProgressRequested += row => DownloadWindows.ShowProgress(manager, row);
        manager.ShowRequested += ShowWindow;
        tray = new TrayIcon("Nexonic Download Manager");
        tray.DoubleClick += ShowWindow;
        var menu = tray.Menu.Items;
        menu.Add(TrayIcon.Item(L.T("Show Downloads"), ShowWindow));
        menu.Add(TrayIcon.Item(L.T("Add URL…"), () => { ShowWindow(); window!.ShowAdd(Dialogs.ClipboardLink()); }));
        menu.Add(TrayIcon.Item(L.T("Stop All"), manager.PauseAll));
        menu.Add(TrayIcon.Item(L.T("Resume All"), manager.ResumeAll));
        menu.Add(SpeedLimitMenu(manager));
        menu.Add(TrayIcon.Item(L.T("Start All Queues"), () => { foreach (var q in manager.Queues.Where(q => !q.IsMain).ToList()) manager.StartQueue(q.Id); }));
        menu.Add(TrayIcon.Item(L.T("Stop All Queues"), () => { foreach (var q in manager.Queues.Where(q => !q.IsMain).ToList()) manager.StopQueue(q.Id); }));
        menu.Add(TrayIcon.Item(L.T("Open Downloads Folder"), () =>
        {
            var dir = manager.Settings.Folder;
            if (System.IO.Directory.Exists(dir)) System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }));
        var watchClip = new System.Windows.Controls.MenuItem { Header = L.T("Watch Copied Links"), IsCheckable = true, IsChecked = manager.Settings.MonitorClipboard };
        watchClip.Click += (_, _) => { manager.Settings.MonitorClipboard = watchClip.IsChecked; manager.ApplySettings(manager.Settings); };
        tray.Menu.Opened += (_, _) => watchClip.IsChecked = manager.Settings.MonitorClipboard;
        menu.Add(watchClip);
        menu.Add(new System.Windows.Controls.Separator());
        menu.Add(TrayIcon.Item(L.T("Quit"), () => Quit(manager)));
        manager.PowerActionPrompt += (q, act, exec) => Dispatcher.Invoke(() => Dialogs.PowerCountdown(window!, q, act, exec));
        manager.Notify += (title, body) =>
        {
            if (manager.Settings.Notifications) tray.Balloon(title, body);
        };
        manager.Finished += r =>
        {
            if (manager.Settings.Notifications)
                tray.Balloon("Download finished", r.FileName + (r.ChecksumResult is { } c ? " – " + c : ""));
            if (manager.Settings.ShowCompleteDialog) Dialogs.Complete(manager, r);
            foreach (var archive in LinkGrabber.ToUnpack(r, manager.Rows.Select(x => x.Record), manager.Settings.ExtractZips)) Unzip(archive, text =>
            {
                if (manager.Settings.Notifications) tray.Balloon("Unzipped", text);
            });
        };
        manager.ClipboardLink += link => window.ShowAdd(link);
        manager.QuitRequested += () => Quit(manager);
        manager.ApplyAutomation(f => Dispatcher.InvokeAsync(f).Task);
        manager.ApplyRemote();
        ApplyScale(manager.Settings.InterfaceScale);
        Demo.Start(manager, window);
        Updater.Start(manager);
        _ = ListenAsync(manager);
    }

    static double scale = 1;
    static bool scaleHooked;

    /// <summary>Options → General → Interface size: every window's content is drawn larger or smaller (75 % to 200 %).
    /// Open windows change at once; small windows that size themselves get wider with their content.</summary>
    public static void ApplyScale(int percent)
    {
        scale = Math.Clamp(percent, 75, 200) / 100.0;
        if (!scaleHooked)
        {
            scaleHooked = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            {
                if (sender is not Window w) return;
                Scale(w);
                if (scale != 1 && w.SizeToContent != SizeToContent.Manual && !double.IsNaN(w.Width)) w.Width *= scale;
            }));
        }
        foreach (Window w in Current.Windows) Scale(w);
    }

    static void Scale(Window w)
    {
        if (w.Content is FrameworkElement content)
            content.LayoutTransform = scale == 1 ? System.Windows.Media.Transform.Identity : new System.Windows.Media.ScaleTransform(scale, scale);
    }

    /// <summary>Unzip when finished (Downloads menu): unpacks in the background, then reports on the UI thread.</summary>
    void Unzip(string path, Action<string> done)
    {
        var name = Path.GetFileName(path);
        Task.Run(() =>
        {
            string text;
            try { text = $"{name} → {Path.GetFileName(Archives.Extract(path))}"; }
            catch (Exception e) { text = $"{name}: {e.Message}"; }
            Dispatcher.BeginInvoke(() => done(text));
        });
    }

    /// <summary>Tray quick setting: the speed limit for all downloads, from off to 10 MB/s.</summary>
    static System.Windows.Controls.MenuItem SpeedLimitMenu(DownloadManager manager)
    {
        var item = new System.Windows.Controls.MenuItem { Header = L.T("Speed Limit") };
        foreach (var kb in SpeedLimits.Presets)
        {
            var choice = new System.Windows.Controls.MenuItem { Header = kb <= 0 ? L.T("No Limit") : SpeedLimits.Title(kb), IsCheckable = true };
            choice.Click += (_, _) => { var s = manager.Settings; s.SpeedLimitKBps = kb; manager.ApplySettings(s); };
            item.Items.Add(choice);
        }
        item.SubmenuOpened += (_, _) =>
        {
            var current = manager.Settings.SpeedLimitKBps;
            for (var i = 0; i < SpeedLimits.Presets.Length; i++)
                ((System.Windows.Controls.MenuItem)item.Items[i]).IsChecked = SpeedLimits.Presets[i] == current;
        };
        return item;
    }

    void Quit(DownloadManager manager)
    {
        manager.Save();
        if (window != null) WindowPlace.Save(window);
        tray?.Dispose();
        Shutdown();
    }

    void ShowWindow()
    {
        window!.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;   // bring the existing window to the front, then let it behave normally again
        window.Topmost = false;
        window.Focus();
    }

    async Task ListenAsync(DownloadManager manager)
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync();
                var why = server.ReadByte();
                Dispatcher.Invoke(() =>
                {
                    manager.CollectInbox();
                    if (why != 'i') ShowWindow();
                });
            }
            catch { await Task.Delay(500); }
        }
    }

    /// <summary>Lets the browser extension find the helper (per user, no admin rights needed).</summary>
    static void RegisterBrowserHost()
    {
        var host = Path.Combine(AppContext.BaseDirectory, "nexonic-host.exe");
        if (!File.Exists(host)) return;
        var manifest = Path.Combine(AppPaths.Support, NativeMessaging.HostName + ".json");
        File.WriteAllText(manifest, NativeMessaging.Manifest(host));
        foreach (var browser in new[] { @"Google\Chrome", @"Microsoft\Edge", @"BraveSoftware\Brave-Browser", @"Chromium", @"Vivaldi" })
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\{browser}\NativeMessagingHosts\{NativeMessaging.HostName}");
            key.SetValue("", manifest);
        }
        var firefox = Path.Combine(AppPaths.Support, NativeMessaging.HostName + ".firefox.json");
        File.WriteAllText(firefox, NativeMessaging.FirefoxManifest(host));
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Mozilla\NativeMessagingHosts\{NativeMessaging.HostName}"))
            key.SetValue("", firefox);
    }
}

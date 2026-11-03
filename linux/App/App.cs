using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>Starts the manager, the main window, the tray icon and the helpers, like the Windows app's App.</summary>
public sealed class App : Application
{
    /// <summary>Set by Program before the app starts.</summary>
    public static SingleInstance? Single { get; set; }
    public static bool StartedForInbox { get; set; }
    public static App? Instance => Current as App;

    DownloadManager? manager;
    TrayIcon? tray;
    IClassicDesktopStyleApplicationLifetime? desktop;
    public MainWindow? MainWindow { get; private set; }

    public override void Initialize()
    {
        Name = "Nexonic Download Manager";
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Prefs.Load().Variant;
    }

    public static void ApplyTheme(Prefs prefs)
    {
        if (Current != null) Current.RequestedThemeVariant = prefs.Variant;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
        {
            desktop = d;
            d.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            d.Exit += (_, _) =>
            {
                manager?.Save();
                Single?.Dispose();
            };
            // The keyring may ask to be unlocked: ask before the lists are read, without blocking the app.
            Task.Run(() => Secrets.Protector is KeyringProtector k && k.Available)
                .ContinueWith(t => Dispatcher.UIThread.Post(() => Start(t.IsCompletedSuccessfully && t.Result)));
        }
        base.OnFrameworkInitializationCompleted();
    }

    void Start(bool keyring)
    {
        Directory.CreateDirectory(AppPaths.Support);
        Task.Run(() => { try { new Integration().Install(); } catch { } });
        var m = manager = new DownloadManager(a => Dispatcher.UIThread.Post(a));
        var window = MainWindow = new MainWindow(m);
        if (!keyring) window.ShowKeyringWarning();
        // Started by the browser helper: stay in the tray unless the user wants the window for browser downloads.
        if (!StartedForInbox || m.Settings.BringToFrontOnBrowserDownload) window.Show();
        m.DownloadDialogRequested += job => DownloadWindows.ShowInfo(m, job);
        m.ProgressRequested += row => DownloadWindows.ShowProgress(m, row);
        m.ShowRequested += ShowWindow;
        m.Notify += (title, body) => { if (m.Settings.Notifications) LinuxSystem.Notify(title, body, IconPath); };
        m.Finished += r =>
        {
            if (m.Settings.Notifications)
                LinuxSystem.Notify("Download finished", r.FileName + (r.ChecksumResult is { } c ? " – " + c : ""), IconPath);
            if (m.Settings.ShowCompleteDialog) Dialogs.Complete(m, r);
        };
        m.ClipboardLink += link => _ = window.ShowAdd(link);
        m.QuitRequested += Quit;
        // The inbox was read when the manager started; links handed over before that open now.
        m.CollectInbox();
        CreateTray(m);
        m.ApplyAutomation(f => Dispatcher.UIThread.InvokeAsync(f).GetTask());
        Single?.Listen(why => Dispatcher.UIThread.Post(() =>
        {
            m.CollectInbox();
            if (why != 'i') ShowWindow();
        }));

        var ticks = 0;
        var clipboardBusy = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += async (_, _) =>
        {
            m.Tick();
            if (++ticks % 2 != 0 || clipboardBusy || !m.Settings.MonitorClipboard) return;
            clipboardBusy = true;
            try { m.OfferClipboard(window.Clipboard is { } c ? await c.GetTextAsync() : null); }
            catch { }
            finally { clipboardBusy = false; }
        };
        timer.Start();
        if (Environment.GetEnvironmentVariable("NDM_SHOW") is { Length: > 0 } show) { Demo.Show(show, window, m); return; }
        Updater.Start(m);
    }

    /// <summary>The icon file for notifications (installed with the app, or the copy in the AppImage).</summary>
    static string? IconPath
    {
        get
        {
            var local = Path.Combine(AppContext.BaseDirectory, "nexonic-download-manager.png");
            return File.Exists(local) ? local : "nexonic-download-manager";
        }
    }

    void CreateTray(DownloadManager m)
    {
        try
        {
            using var s = typeof(App).Assembly.GetManifestResourceStream("tray.png")!;
            var menu = new NativeMenu();
            void Item(string header, Action click)
            {
                var item = new NativeMenuItem { Header = header };
                item.Click += (_, _) => click();
                menu.Items.Add(item);
            }
            Item("Show Downloads", ShowWindow);
            Item("Add URL…", async () => { if (MainWindow is { } w) await w.ShowAdd(await Ui.ClipboardLink(w)); });
            Item("Stop All", m.PauseAll);
            Item("Resume All", m.ResumeAll);
            var limits = new NativeMenu();
            var limitItems = new List<(NativeMenuItem Item, int KB)>();
            foreach (var kb in SpeedLimits.Presets)
            {
                var choice = new NativeMenuItem { Header = SpeedLimits.Title(kb), ToggleType = NativeMenuItemToggleType.Radio, IsChecked = m.Settings.SpeedLimitKBps == kb };
                var value = kb;
                choice.Click += (_, _) =>
                {
                    var s = m.Settings;
                    s.SpeedLimitKBps = value;
                    m.ApplySettings(s);
                    foreach (var (it, k) in limitItems) it.IsChecked = k == value;
                };
                limitItems.Add((choice, kb));
                limits.Items.Add(choice);
            }
            menu.Items.Add(new NativeMenuItem { Header = "Speed Limit", Menu = limits });
            menu.Items.Add(new NativeMenuItemSeparator());
            Item("Quit", Quit);
            tray = new TrayIcon { Icon = new WindowIcon(s), ToolTipText = "Nexonic Download Manager", Menu = menu, IsVisible = true };
            tray.Clicked += (_, _) => ShowWindow();
            TrayIcon.SetIcons(this, new TrayIcons { tray });
        }
        catch { /* no tray on this desktop: the app menu entry brings the window back */ }
    }

    public void ShowWindow()
    {
        if (MainWindow is not { } w) return;
        w.Bring();
        w.Topmost = true;   // bring the existing window to the front, then let it behave normally again
        w.Topmost = false;
    }

    public void Quit()
    {
        manager?.Save();
        MainWindow?.SavePlace();
        if (MainWindow != null) MainWindow.Quitting = true;
        if (tray != null) tray.IsVisible = false;
        desktop?.Shutdown();
    }

    /// <summary>Quits and starts the given program (the updated AppImage) once this copy has closed.</summary>
    public void Restart(string program)
    {
        try
        {
            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            foreach (var a in new[] { "-c", "sleep 2; exec \"$0\" </dev/null >/dev/null 2>&1", program }) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        catch { }
        Quit();
    }
}

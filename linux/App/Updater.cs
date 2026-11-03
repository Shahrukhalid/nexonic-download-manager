using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>
/// Tells the user when a new version is published. The AppImage updates itself: the new file is downloaded next to it, checked
/// against the checksum GitHub publishes for it, and only then put in place. Packages (.deb, .rpm) open the download page.
/// </summary>
public static class Updater
{
    static DownloadManager? manager;
    static DispatcherTimer? daily;
    static bool busy;
    static bool installing;

    /// <summary>The AppImage file name of each release, by processor.</summary>
    public static string AppImageAsset => RuntimeInformation.OSArchitecture == Architecture.Arm64
        ? "NexonicDownloadManager-Linux-aarch64.AppImage" : "NexonicDownloadManager-Linux-x86_64.AppImage";

    /// <summary>Checks a few seconds after the app opens, then once a day while it keeps running.</summary>
    public static void Start(DownloadManager m)
    {
        manager = m;
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        first.Tick += (_, _) => { first.Stop(); _ = Check(false); };
        first.Start();
        daily = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        daily.Tick += (_, _) => _ = Check(false);
        daily.Start();
    }

    static HttpClient Client()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("NexonicDownloadManager/" + HelpWindows.Version);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static async Task Check(bool manual)
    {
        if (installing || manager is null || busy || (!manual && !manager.Settings.CheckForUpdates)) return;
        busy = true;
        Updates.Release? release = null;
        try
        {
            using var c = Client();
            c.Timeout = TimeSpan.FromSeconds(20);
            // GitHub's API first; when it refuses or fails, the copy of the same details attached to the release.
            foreach (var url in new[] { Updates.LatestUrl, Updates.MirrorUrl })
            {
                try { release = Updates.Parse(await c.GetStringAsync(url)); } catch { }
                if (release != null) break;
            }
        }
        catch { }
        finally { busy = false; }

        var current = HelpWindows.Version;
        var owner = App.Instance?.MainWindow;
        if (release is null)
        {
            if (manual) await Ui.Message(owner, "Couldn't check for updates", "GitHub didn't answer. Check your internet connection and try again later, or download the newest version from the website.");
            return;
        }
        if (!Updates.IsNewer(release.Version, current))
        {
            if (manual) await Ui.Message(owner, "You're up to date", $"Nexonic Download Manager {current} is the newest version.");
            return;
        }
        if (!manual && manager.Settings.SkippedVersion == release.Version) return;

        var choice = await Ask(release, current);
        if (installing) return;
        switch (choice)
        {
            case "update": await Install(release); break;
            case "skip":
                manager.Settings.SkippedVersion = release.Version;
                manager.ApplySettings(manager.Settings);
                break;
        }
    }

    static async Task<string> Ask(Updates.Release r, string current)
    {
        var result = "later";
        var w = new Window
        {
            Title = "Update available", Width = 520, SizeToContent = SizeToContent.Height, CanResize = false, Icon = Ui.WindowIcon,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true,
        };
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(Ui.Title("A new version of Nexonic Download Manager is available", 15));
        root.Children.Add(new TextBlock { Text = $"Version {r.Version} is ready to install. You have {current}.", Margin = new Thickness(0, 6, 0, 10) });
        root.Children.Add(new TextBlock { Text = "What's new:", FontWeight = FontWeight.SemiBold });
        var notes = Updates.Notes(r.Notes, current);
        root.Children.Add(new TextBox
        {
            Text = notes.Length > 0 ? notes : "Improvements and fixes.", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 200,
            Margin = new Thickness(0, 4, 0, 0),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        void Add(string text, string value, bool isDefault = false, bool isCancel = false) =>
            buttons.Children.Add(Ui.Button(text, () => { result = value; w.Close(); }, isDefault, isCancel));
        Add("Skip This Version", "skip");
        Add("Remind Me Later", "later", isCancel: true);
        Add(new Integration().Kind == InstallKind.AppImage ? "Update Now" : "Download", "update", isDefault: true);
        root.Children.Add(buttons);
        w.Content = root;
        await Ui.ShowModal(w, null);
        return result;
    }

    /// <summary>Replaces the AppImage after checking it; anything else opens the release page.</summary>
    static async Task Install(Updates.Release r)
    {
        var integration = new Integration();
        var appImage = integration.AppImagePath;
        var asset = AppImageAsset;
        if (integration.Kind != InstallKind.AppImage || appImage == null || !r.Assets.TryGetValue(asset, out var url)
            || !Updates.TrustedAsset(url) || Updates.Digest(r, asset) is not { } digest || !CanWrite(Path.GetDirectoryName(appImage)!))
        {
            LinuxSystem.Open(r.Page);
            return;
        }
        installing = true;
        var progress = new Window
        {
            Title = "Updating Nexonic Download Manager", Width = 380, SizeToContent = SizeToContent.Height, CanResize = false, Icon = Ui.WindowIcon,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = $"Downloading version {r.Version}…" },
                    new ProgressBar { IsIndeterminate = true, Height = 8, Margin = new Thickness(0, 12, 0, 0) },
                },
            },
        };
        progress.Show();
        // Next to the AppImage (same file system), hidden, so the switch is a single rename.
        var temp = Path.Combine(Path.GetDirectoryName(appImage)!, $".{Path.GetFileName(appImage)}.update-{Environment.ProcessId}");
        try
        {
            using (var c = Client())
            await using (var src = await c.GetStreamAsync(url))
            await using (var dst = File.Create(temp))
                await src.CopyToAsync(dst);
            progress.Close();
            if (!await Task.Run(() => Updates.Verify(temp, digest)))
            {
                try { File.Delete(temp); } catch { }
                installing = false;
                await Ui.Message(null, "Update", "The downloaded update did not match the published file, so it was not installed. Download it from the release page instead.");
                LinuxSystem.Open(r.Page);
                return;
            }
            File.SetUnixFileMode(temp, File.GetUnixFileMode(appImage) | UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, appImage, true);   // this copy keeps running from the old file until it closes
        }
        catch (Exception e)
        {
            progress.Close();
            try { File.Delete(temp); } catch { }
            installing = false;
            await Ui.Message(null, "Update", "The update could not be downloaded: " + e.Message + "\nTry again later, or download it from the release page.");
            return;
        }
        installing = false;
        if (await Ui.Confirm(null, "Update installed", $"Version {r.Version} is installed. Restart the app now to use it? Downloads continue where they stopped.", "Restart Now", "Later"))
            App.Instance?.Restart(appImage);
    }

    static bool CanWrite(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".nexonic-write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}

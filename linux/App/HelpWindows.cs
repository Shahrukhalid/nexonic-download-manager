using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>Help and About windows, with the same topics and links as the Windows and Mac apps.</summary>
public static class HelpWindows
{
    public const string Author = "Shahrukh Khalid";
    public const string Website = "https://www.shahrukhalid.com";
    public const string GitHub = "https://github.com/Shahrukhalid";
    public const string Project = "https://github.com/Shahrukhalid/nexonic-download-manager";
    public const string OnlineHelp = Project + "/blob/main/docs/HELP.md";
    public const string Releases = Project + "/releases/latest";
    public const string ReportProblem = Project + "/issues/new";
    public const string License = Project + "/blob/main/LICENSE";
    public const string Terms = Project + "/blob/main/TERMS.md";

    public static string Version
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version;
            return v is null ? "dev" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    static readonly (string Title, (string Heading, string Text)[] Sections)[] Topics =
    {
        ("Getting Started", new[]
        {
            ("Add a download", "Click Add URL (Ctrl+N), paste one or more links and press Start Download. Links you copy are offered automatically when the Add window opens."),
            ("Pause and resume", "Select a download and press Stop or Resume. Downloads continue where they stopped, even after you close the app or restart the computer."),
            ("Find your files", "Finished files are saved in the folder chosen in the Add window (your Downloads folder by default). Double-click a finished download to open it, or right-click and choose Show in Folder."),
        }),
        ("Browser Extension", new[]
        {
            ("Chrome, Chromium, Edge, Brave, Vivaldi and Opera", "Open the app once (it sets up the browser helper for every browser it finds). In Options, Browsers & Command Line, choose Open the Extension Folder. Open the browser's extensions page, turn on Developer mode, click Load unpacked and choose the Extension folder."),
            ("Firefox", "Open Firefox Add-on.xpi from the same folder with Firefox. For a quick test, open about:debugging and choose Load Temporary Add-on."),
            ("Flatpak and Snap browsers", "Browsers installed as Flatpak or Snap run in a sandbox; the helper is set up for Firefox and Chromium there too, but some versions only allow it after you grant the browser access to other programs."),
            ("Download and progress windows", "When you click a download link in the browser, the download window shows the file name, size, category and folder: choose Start Download, Download Later or Cancel. Each download you start gets its own progress window with the speed, time left, a speed graph and every connection. Turn either window off in Options, General."),
            ("What it does", "Downloads from the browser are handed to the app automatically. Right-click a link and choose Download with Nexonic, or use the video button that appears on pages with video."),
        }),
        ("Saving Videos", new[]
        {
            ("From a website", "Click the video button the extension shows on the page, or paste the page link into Add URL. Videos are saved in the best available quality as .mp4."),
            ("Quality", "The download window and the Download this video button list the qualities the video is offered in. Pick one, or Sound only to save just the audio."),
            ("Streamed video", "Streams (.m3u8) are found by the extension, downloaded in parallel and joined into one .mp4. Copy-protected services are not supported."),
        }),
        ("Queues and Scheduler", new[]
        {
            ("Queues", "Put downloads in a queue to run them one after another or a few at a time. Right-click a download and choose Move to Queue."),
            ("Scheduler", "Start a queue at a set time, stop it at another, and optionally close the app, sleep or shut down the computer when it is done."),
        }),
        ("Batch and Site Grabber", new[]
        {
            ("Batch download", "Download a numbered series such as file01.jpg to file50.jpg by writing the link once with a pattern, for example file[01-50].jpg."),
            ("Site Grabber", "Enter a page address to list every file linked from it, filter by type and download the ones you check."),
        }),
        ("Speed and Options", new[]
        {
            ("Faster downloads", "Each file is split into parts that download at the same time. Change the number of connections in Options (up to 32)."),
            ("Speed limit", "Set a limit for all downloads in Options, or for one download in its Properties."),
            ("Proxy and sign-in", "Set a proxy in Options, Connection. For password-protected downloads, add the user name and password in the Add window or under Properties."),
        }),
        ("Command Line", new[]
        {
            ("Use ndm", "Open a new terminal and type ndm help. Example: ndm https://example.com/file.zip --queue Night"),
        }),
        ("Troubleshooting", new[]
        {
            ("A link has expired", "Right-click the download, choose Refresh Link and paste a fresh link. The download continues without starting over."),
            ("The AppImage doesn't start", "Make it executable (right-click, Properties, Allow executing as program, or chmod +x in a terminal). If it still does not start, install the fuse3 package, or start it with --appimage-extract-and-run."),
            ("Passwords and cookies are not saved", "They are encrypted with a key kept in your desktop's keyring. Install and unlock a keyring (GNOME Keyring or KWallet, and the libsecret tools), then restart the app."),
            ("No tray icon", "On GNOME, the tray needs the AppIndicator extension (included in Ubuntu). Without it, open the app again from the menu to bring the window back."),
            ("The browser doesn't hand over downloads", "Open the app once so it can register itself with your browsers, then restart the browser."),
            ("A video site stopped working", "The video engine updates itself every day. Try again later, and if it still fails, report the problem with the page link."),
            ("Updating the app", "When a new version is released, the app tells you when it opens, with what's new. Choose Update Now to install it, Remind Me Later, or Skip This Version. The AppImage replaces itself after checking the download; packages (.deb and .rpm) open the download page, or update with your package manager. Turn the check off in Options."),
        }),
        ("Keyboard Shortcuts", new[]
        {
            ("Downloads", "Ctrl+N  Add URL\nCtrl+F  Search downloads\nCtrl+A  Select all downloads in the list\nDelete  Remove the selected downloads\nF1  Help"),
        }),
    };

    static Window? help;

    public static void ShowHelp(Window owner)
    {
        if (help is { } open) { open.Activate(); return; }
        var list = new ListBox { Width = 220, Background = Ui.Shade, Padding = new Thickness(4, 8) };
        list.ItemsSource = Topics.Select(t => t.Title).ToList();
        var detail = new StackPanel { Margin = new Thickness(28, 20), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };

        void Show(int index)
        {
            detail.Children.Clear();
            var topic = Topics[index];
            detail.Children.Add(new TextBlock { Text = topic.Title, FontSize = 24, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            foreach (var (heading, text) in topic.Sections)
            {
                detail.Children.Add(new TextBlock { Text = heading, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 3) });
                detail.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
            }
            detail.Children.Add(new Separator { Margin = new Thickness(0, 16, 0, 8) });
            detail.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 20,
                Children = { Ui.Link("Online Help", OnlineHelp), Ui.Link("Report a Problem", ReportProblem), Ui.LinkButton("Check for Updates", () => _ = Updater.Check(true)) },
            });
        }

        list.SelectionChanged += (_, _) => { if (list.SelectedIndex >= 0) Show(list.SelectedIndex); };
        var root = new DockPanel();
        DockPanel.SetDock(list, Dock.Left);
        root.Children.Add(list);
        root.Children.Add(new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var w = new Window
        {
            Title = "Nexonic Download Manager Help", Width = 880, Height = 580, MinWidth = 640, MinHeight = 400, Icon = Ui.WindowIcon,
            WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, Content = root,
        };
        list.SelectedIndex = 0;
        help = w;
        w.Closed += (_, _) => help = null;
        if (owner.IsVisible) w.Show(owner); else w.Show();
    }

    public static async Task ShowAbout(Window owner)
    {
        var p = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        p.Children.Add(new Image { Source = Ui.AppIcon, Width = 80, Height = 80, Margin = new Thickness(0, 0, 0, 10) });
        p.Children.Add(new TextBlock { Text = "Nexonic Download Manager", FontSize = 20, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        p.Children.Add(new TextBlock { Text = $"Version {Version}", Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
        p.Children.Add(new TextBlock { Text = "Free, open-source download manager for Linux, Windows, Mac and Android.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0),
            Children = { new TextBlock { Text = "Made by ", VerticalAlignment = VerticalAlignment.Center }, Ui.Link(Author, Website) },
        });
        p.Children.Add(new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                Spaced(Ui.Link("Website", Website)), Spaced(Ui.Link("GitHub", GitHub)), Spaced(Ui.Link("Source Code", Project)), Spaced(Ui.Link("Report a Problem", ReportProblem)),
            },
        });
        p.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 10) });
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8,
            Children = { new TextBlock { Text = $"© 2026 {Author}.", FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center }, Ui.Link("MIT License", License), Ui.Link("Terms of Use", Terms) },
        });
        var w = new Window
        {
            Title = "About Nexonic Download Manager", Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, Icon = Ui.WindowIcon,
            WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(Ui.Button("Check for Updates", () => _ = Updater.Check(true)));
        buttons.Children.Add(Ui.Button("Close", w.Close, isDefault: true, isCancel: true));
        p.Children.Add(buttons);
        w.Content = p;
        await Ui.ShowModal(w, owner);
    }

    static Control Spaced(Control c)
    {
        c.Margin = new Thickness(8, 0);
        return c;
    }
}

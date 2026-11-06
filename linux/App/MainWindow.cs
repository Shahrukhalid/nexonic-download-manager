using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>The main window: toolbar, the lists on the left, the downloads table and the details of the selected download.</summary>
public sealed class MainWindow : Window
{
    readonly DownloadManager manager;
    readonly ListBox sidebar = new() { Width = 220, Background = Ui.Shade, Padding = new Thickness(0, 6) };
    readonly ListBox list = new() { SelectionMode = SelectionMode.Multiple, Background = Brushes.Transparent };
    readonly ObservableCollection<Row> view = new();
    readonly TextBox search = new() { Watermark = "Search downloads", Width = 230, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock statusLeft = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock statusRight = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock keyringWarning = new() { FontSize = 12, Foreground = Ui.Waiting, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), IsVisible = false };
    readonly StackPanel empty = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 420, IsVisible = false };
    readonly TextBlock emptyTitle = Ui.Title("No downloads here yet");
    readonly Border details = new() { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 10), IsVisible = false };
    readonly TextBlock detailName = new() { FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly SelectableTextBlock detailUrl = new() { FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly SegmentBar bar = new() { Height = 14, Margin = new Thickness(0, 6) };
    readonly TextBlock detailInfo = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock detailError = new() { FontSize = 12, Foreground = Ui.Bad, TextWrapping = TextWrapping.Wrap };
    readonly MenuItem moveMenu = new() { Header = "Move to Queue" };
    string filter = "all";
    string sortKey = "added";
    bool sortDescending = true;
    bool rebuilding;
    int ticks;

    /// <summary>Set by the app when it really quits; until then closing the window keeps downloads running in the tray.</summary>
    public bool Quitting { get; set; }

    public MainWindow(DownloadManager manager)
    {
        this.manager = manager;
        Title = "Nexonic Download Manager";
        Icon = Ui.WindowIcon;
        Width = 1180;
        Height = 700;
        MinWidth = 760;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RestorePlace();

        list.ItemsSource = view;
        list.ItemTemplate = RowTemplate;
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(10, 5)), new Setter(ListBoxItem.MinHeightProperty, 34.0) },
        });
        list.SelectionChanged += (_, _) => { ShowDetails(); UpdateButtons(); };
        list.DoubleTapped += (_, _) => OnDoubleClick();
        list.ContextMenu = BuildContextMenu();
        sidebar.SelectionChanged += (_, _) => OnFilter();
        search.TextChanged += (_, _) => { Refilter(); UpdateStatus(); };

        Content = BuildLayout();
        BuildSidebar();
        // Rebuild after the change has finished: rebuilding the list inside the change notification crashed the app
        // when a download was added while another one was listed (found by linux/packaging/smoke.py).
        manager.Queues.CollectionChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(BuildSidebar);
        manager.Rows.CollectionChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Refilter());

        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = new Cmd(() => _ = OnAdd()) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = new Cmd(() => search.Focus()) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.A, KeyModifiers.Control), Command = new Cmd(SelectAllUnlessTyping) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Delete), Command = new Cmd(() => { if (!Typing) _ = ConfirmRemove(Selected, toTrash: false); }) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F1), Command = new Cmd(() => HelpWindows.ShowHelp(this)) });
        void Shortcut(Key key, KeyModifiers mods, Action run) =>
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(key, mods), Command = new Cmd(() => { if (!Typing) run(); }) });
        Shortcut(Key.R, KeyModifiers.Control, ResumeSelected);
        Shortcut(Key.OemPeriod, KeyModifiers.Control, StopSelected);
        Shortcut(Key.R, KeyModifiers.Control | KeyModifiers.Shift, manager.ResumeAll);
        Shortcut(Key.P, KeyModifiers.Control | KeyModifiers.Shift, manager.PauseAll);
        Shortcut(Key.O, KeyModifiers.Control, OnDoubleClick);
        Shortcut(Key.T, KeyModifiers.Control | KeyModifiers.Shift, OpenTorrents);
        Shortcut(Key.B, KeyModifiers.Control | KeyModifiers.Shift, OpenBatch);
        Shortcut(Key.G, KeyModifiers.Control | KeyModifiers.Shift, OpenGrabber);
        Shortcut(Key.Q, KeyModifiers.Control, manager.RequestQuit);
        Shortcut(Key.L, KeyModifiers.Control | KeyModifiers.Shift, () => _ = OpenPlaylist(""));
        Shortcut(Key.L, KeyModifiers.Control, OpenLibrary);
        VideoSubtitles.Enabled = manager.Settings.VideoSubtitles;
        UpdateButtons();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        AddHandler(DragDrop.DropEvent, OnDrop);

        Closing += (_, e) =>
        {
            if (Quitting) return;
            e.Cancel = true;   // closing the window keeps the downloads running
            manager.Save();
            SavePlace();
            Hide();
        };

        Refilter();
        UpdateStatus();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => { ShowDetails(); UpdateStatus(); };
        timer.Start();
    }

    /// <summary>Opens where it was last time, when that place is still on a screen.</summary>
    void RestorePlace()
    {
        var p = Prefs.Load();
        if (p.WindowWidth is double w && p.WindowHeight is double h && w >= MinWidth && h >= MinHeight)
        {
            Width = w;
            Height = h;
        }
        if (p.WindowX is int x && p.WindowY is int y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
            Opened += (_, _) => { if (Screens.ScreenFromPoint(new PixelPoint(x + 40, y + 20)) == null) Position = new PixelPoint(80, 80); };
        }
        if (p.WindowMaximized) WindowState = WindowState.Maximized;
    }

    public void SavePlace()
    {
        try
        {
            var p = Prefs.Load();
            p.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                p.WindowX = Position.X;
                p.WindowY = Position.Y;
                p.WindowWidth = Bounds.Width;
                p.WindowHeight = Bounds.Height;
            }
            p.Save();
        }
        catch { }
    }

    bool Typing => FocusManager?.GetFocusedElement() is TextBox;

    void SelectAllUnlessTyping()
    {
        if (Typing) { if (FocusManager?.GetFocusedElement() is TextBox t) t.SelectAll(); return; }
        list.SelectAll();
    }

    /// <summary>Shows that secrets cannot be saved because no keyring answered (never silently).</summary>
    public void ShowKeyringWarning()
    {
        keyringWarning.Text = "⚠ No keyring: passwords and cookies are not saved";
        ToolTip.SetTip(keyringWarning,
            "No Secret Service keyring (GNOME Keyring, KWallet…) answered, or it is locked. Passwords, proxy passwords and browser " +
            "cookies work until the app closes but are never written to disk unencrypted. Install or unlock a keyring " +
            "(for example gnome-keyring and libsecret-tools) and restart the app.");
        keyringWarning.IsVisible = true;
    }

    // ---- layout ----

    readonly WrapPanel toolbar = new();
    Button? resumeButton, stopButton, deleteButton;
    readonly MenuItem showToolbarItem = new() { Header = "Show _Toolbar Buttons", ToggleType = MenuItemToggleType.CheckBox };
    readonly MenuItem showNamesItem = new() { Header = "Show Button _Names", ToggleType = MenuItemToggleType.CheckBox };
    readonly MenuItem unzipItem = new() { Header = "_Unzip Finished .zip Files", ToggleType = MenuItemToggleType.CheckBox };
    readonly MenuItem subtitlesItem = new() { Header = "Save Su_btitles With Videos", ToggleType = MenuItemToggleType.CheckBox };

    void OpenLibrary() => Dialogs.MediaLibraryWindow(this, manager);

    /// <summary>Playlists and channels: pick their videos first, then they open in the Add window.</summary>
    async Task OpenPlaylist(string link)
    {
        var queue = CurrentQueue;
        Bring();
        await Dialogs.Playlist(this, manager, link, links => ShowAdd(links, queue));
    }

    void OpenTorrents() => _ = Dialogs.TorrentManager(this, manager, CurrentQueue);
    void OpenGrabber() => _ = Dialogs.Grabber(this, manager, CurrentQueue);
    void OpenBatch() => _ = Dialogs.Batch(this, manager, CurrentQueue);
    void ResumeSelected() { foreach (var r in Selected) manager.Resume(r); }
    void StopSelected() { foreach (var r in Selected) manager.Pause(r); }
    void StartQueue() { manager.StartQueue(CurrentQueue); BuildSidebar(); }
    void StopQueue() { manager.StopQueue(CurrentQueue); BuildSidebar(); }

    /// <summary>Menus like IDM's: Tasks, Downloads, View and Help. Each item does the same as its toolbar button.</summary>
    Menu BuildMenu()
    {
        MenuItem Item(string header, Action run, string? keys = null)
        {
            var item = new MenuItem { Header = header };
            if (keys != null) item.InputGesture = KeyGesture.Parse(keys);
            item.Click += (_, _) => run();
            return item;
        }
        MenuItem Group(string header, params Control[] items)
        {
            var g = new MenuItem { Header = header };
            foreach (var i in items) g.Items.Add(i);
            return g;
        }
        Separator Line() => new();
        showToolbarItem.Click += (_, _) => { manager.Settings.ShowToolbar = !manager.Settings.ShowToolbar; manager.ApplySettings(manager.Settings); ApplyToolbar(); };
        showNamesItem.Click += (_, _) => { manager.Settings.ToolbarText = !manager.Settings.ToolbarText; manager.ApplySettings(manager.Settings); ApplyToolbar(); };
        ToolTip.SetTip(unzipItem, "Unpack each finished .zip download into a folder next to it");
        unzipItem.Click += (_, _) => { manager.Settings.ExtractZips = !manager.Settings.ExtractZips; manager.ApplySettings(manager.Settings); ApplyToolbar(); };
        ToolTip.SetTip(subtitlesItem, "Videos keep subtitles in your language and English when the site has them");
        subtitlesItem.Click += (_, _) =>
        {
            manager.Settings.VideoSubtitles = VideoSubtitles.Enabled = !manager.Settings.VideoSubtitles;
            manager.ApplySettings(manager.Settings);
            ApplyToolbar();
        };
        var menu = new Menu();
        menu.Items.Add(Group("_Tasks",
            Item("_Add URL…", () => _ = OnAdd(), "Ctrl+N"),
            Item("Add _Torrent…", OpenTorrents, "Ctrl+Shift+T"),
            Item("Add _Batch Download…", OpenBatch, "Ctrl+Shift+B"),
            Item("Site _Grabber…", OpenGrabber, "Ctrl+Shift+G"),
            Item("Add _Playlist or Channel…", () => _ = OpenPlaylist(""), "Ctrl+Shift+L"),
            Line(),
            Item("_Options…", () => _ = Dialogs.Options(this, manager)),
            Line(),
            Item("_Quit", manager.RequestQuit, "Ctrl+Q")));
        menu.Items.Add(Group("_Downloads",
            Item("_Resume", ResumeSelected, "Ctrl+R"),
            Item("_Stop", StopSelected, "Ctrl+OemPeriod"),
            Line(),
            Item("Resume _All", manager.ResumeAll, "Ctrl+Shift+R"),
            Item("Stop A_ll", manager.PauseAll, "Ctrl+Shift+P"),
            Line(),
            Item("_Open", OnDoubleClick, "Ctrl+O"),
            Line(),
            Item("_Delete…", () => _ = ConfirmRemove(Selected, toTrash: false)),
            Item("Delete _Completed…", () => _ = OnClear()),
            Line(),
            unzipItem,
            subtitlesItem,
            Item("_Media Library…", OpenLibrary, "Ctrl+L"),
            Line(),
            Item("S_cheduler…", () => _ = OnScheduler()),
            Item("Start _Queue", StartQueue),
            Item("Stop Q_ueue", StopQueue)));
        menu.Items.Add(Group("_View", showToolbarItem, showNamesItem));
        menu.Items.Add(Group("_Help",
            Item("_Help", () => HelpWindows.ShowHelp(this), "F1"),
            Item("_About Nexonic Download Manager", () => _ = HelpWindows.ShowAbout(this))));
        return menu;
    }

    /// <summary>View menu: show or hide the toolbar buttons and their names (saved with the settings).</summary>
    void ApplyToolbar()
    {
        var showToolbar = manager.Settings.ShowToolbar;
        var showNames = manager.Settings.ToolbarText;
        toolbar.IsVisible = showToolbar;
        showToolbarItem.IsChecked = showToolbar;
        showNamesItem.IsChecked = showNames;
        showNamesItem.IsEnabled = showToolbar;
        unzipItem.IsChecked = manager.Settings.ExtractZips;
        subtitlesItem.IsChecked = manager.Settings.VideoSubtitles;
        foreach (var b in toolbar.Children.OfType<Button>())
            if (b.Content is StackPanel p && p.Children.Count > 1) p.Children[1].IsVisible = showNames;
    }

    /// <summary>Resume, Stop and Delete work on the selection, so they are greyed out without one (as in IDM).</summary>
    void UpdateButtons()
    {
        var any = list.SelectedItems?.Count > 0;
        foreach (var b in new[] { resumeButton, stopButton, deleteButton }) if (b != null) b.IsEnabled = any;
    }

    Control BuildLayout()
    {
        var root = new DockPanel();

        toolbar.Margin = new Thickness(8, 6, 8, 4);
        toolbar.VerticalAlignment = VerticalAlignment.Center;
        Button Tool(string icon, string color, string caption, string tip, Action click)
        {
            var glyph = Ui.Icon(icon, 22);
            glyph.Foreground = Ui.Brush(color);
            var b = new Button
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 6), MinWidth = 64,
                Content = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new Border { Child = glyph, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = caption, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center },
                    },
                },
            };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            toolbar.Children.Add(b);
            return b;
        }
        void Gap() => toolbar.Children.Add(new Border { Width = 1, Height = 36, Background = Ui.Line, Margin = new Thickness(6, 0) });
        Tool(Ui.Icons.Add, "#2DA44E", "Add URL", "Add one or more links (Ctrl+N)", () => _ = OnAdd());
        Tool(Ui.Icons.Download, "#6E40C9", "Torrents", "Torrent Manager: add a .torrent file, torrent link or magnet (Ctrl+Shift+T)", OpenTorrents);
        Gap();
        resumeButton = Tool(Ui.Icons.Play, "#1F6FEB", "Resume", "Resume the selected downloads (Ctrl+R)", ResumeSelected);
        stopButton = Tool(Ui.Icons.Pause, "#D97706", "Stop", "Stop the selected downloads (Ctrl+.)", StopSelected);
        Tool(Ui.Icons.StopAll, "#CF222E", "Stop All", "Stop every download (Ctrl+Shift+P)", manager.PauseAll);
        Gap();
        Tool(Ui.Icons.SelectAll, "#0E9F9F", "Select All", "Select every download in this list (click again to deselect)", ToggleSelectAll);
        deleteButton = Tool(Ui.Icons.Delete, "#CF222E", "Delete", "Remove the selected downloads from the list", () => _ = ConfirmRemove(Selected, toTrash: false));
        Tool(Ui.Icons.Clear, "#2DA44E", "Delete Completed", "Remove finished downloads from the list", () => _ = OnClear());
        Gap();
        Tool(Ui.Icons.Clock, "#8250DF", "Scheduler", "Queues and timetables", () => _ = OnScheduler());
        Tool(Ui.Icons.StartQueue, "#10B981", "Start Queue", "Start the selected queue", StartQueue);
        Tool(Ui.Icons.StopQueue, "#DB2777", "Stop Queue", "Stop the selected queue", StopQueue);
        Gap();
        Tool(Ui.Icons.Globe, "#1F6FEB", "Grabber", "Download files from a website section (Ctrl+Shift+G)", OpenGrabber);
        Tool(Ui.Icons.Batch, "#9A6700", "Batch", "Download a numbered series of files (Ctrl+Shift+B)", OpenBatch);
        Tool(Ui.Icons.Options, "#6E7781", "Options", "Options", () => _ = Dialogs.Options(this, manager));

        var top = new DockPanel { LastChildFill = true };
        var searchBox = new DockPanel { Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var searchIcon = Ui.Icon(Ui.Icons.Search, 16);
        searchIcon.Margin = new Thickness(0, 0, 6, 0);
        searchIcon.Opacity = 0.6;
        DockPanel.SetDock(searchIcon, Dock.Left);
        searchBox.Children.Add(searchIcon);
        searchBox.Children.Add(search);
        ToolTip.SetTip(search, "Search by file name or link (Ctrl+F)");
        DockPanel.SetDock(searchBox, Dock.Right);
        top.Children.Add(searchBox);
        top.Children.Add(toolbar);
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);
        var topBorder = new Border { Child = top, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(topBorder, Dock.Top);
        root.Children.Add(topBorder);
        ApplyToolbar();

        var status = new DockPanel { Margin = new Thickness(12, 5) };
        DockPanel.SetDock(statusRight, Dock.Right);
        DockPanel.SetDock(keyringWarning, Dock.Right);
        status.Children.Add(statusRight);
        status.Children.Add(keyringWarning);
        status.Children.Add(statusLeft);
        var statusBorder = new Border { Child = status, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(statusBorder, Dock.Bottom);
        root.Children.Add(statusBorder);

        DockPanel.SetDock(sidebar, Dock.Left);
        root.Children.Add(sidebar);

        var center = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var header = HeaderRow();
        center.Children.Add(header);
        Grid.SetRow(list, 1);
        center.Children.Add(list);

        empty.Children.Add(new Border { Child = Ui.Icon(Ui.Icons.Download, 56), HorizontalAlignment = HorizontalAlignment.Center });
        emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        emptyTitle.Margin = new Thickness(0, 12, 0, 6);
        empty.Children.Add(emptyTitle);
        var hint = Ui.Muted("Click Add URL, paste a link, drop one on this window, or download from your browser with the extension.");
        hint.TextAlignment = TextAlignment.Center;
        empty.Children.Add(hint);
        var addButton = Ui.Button("Add URL", () => _ = OnAdd(), isDefault: false);
        addButton.Classes.Add("accent");
        addButton.HorizontalAlignment = HorizontalAlignment.Center;
        addButton.Margin = new Thickness(0, 14, 0, 0);
        empty.Children.Add(addButton);
        Grid.SetRow(empty, 1);
        center.Children.Add(empty);

        details.Child = new StackPanel { Children = { detailName, detailUrl, bar, detailInfo, detailError } };
        Grid.SetRow(details, 2);
        center.Children.Add(details);
        root.Children.Add(center);
        return root;
    }

    // ---- table ----

    const string Columns = "*,90,120,96,84,140,110,150";

    static readonly (string Title, string Key)[] Headers =
    {
        ("File Name", "name"), ("Size", "size"), ("Progress", "progress"), ("Speed", "speed"), ("Time Left", "left"),
        ("Status", "status"), ("Queue", "queue"), ("Added", "added"),
    };

    Control HeaderRow()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Margin = new Thickness(10, 0) };
        for (var i = 0; i < Headers.Length; i++)
        {
            var (title, key) = Headers[i];
            var b = new Button
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock { Text = title + (sortKey == key ? (sortDescending ? "  ▾" : "  ▴") : ""), FontSize = 12, FontWeight = FontWeight.SemiBold, Opacity = 0.8 },
            };
            ToolTip.SetTip(b, "Sort by " + title.ToLowerInvariant());
            b.Click += (_, _) =>
            {
                if (sortKey == key) sortDescending = !sortDescending;
                else { sortKey = key; sortDescending = key is "added" or "size" or "progress" or "speed"; }
                foreach (var (child, idx) in g.Children.OfType<Button>().Select((c, n) => (c, n)))
                    if (child.Content is TextBlock tb) tb.Text = Headers[idx].Title + (sortKey == Headers[idx].Key ? (sortDescending ? "  ▾" : "  ▴") : "");
                Refilter(force: true);
            };
            Grid.SetColumn(b, i);
            g.Children.Add(b);
        }
        return new Border { Child = g, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    /// <summary>One row of the table; it updates itself when the download changes.</summary>
    internal static readonly FuncDataTemplate<Row> RowTemplate = new((row, _) => RowView(row), supportsRecycling: false);

    static Control? RowView(Row? row)
    {
        // Avalonia builds the template with null when clearing a recycled item container.
        if (row is null) return null;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Background = Brushes.Transparent };
        var nameCell = new DockPanel { Margin = new Thickness(0, 0, 6, 0) };
        var badge = Ui.FileBadge(row.Name, 22);
        badge.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(badge, Dock.Left);
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        nameCell.Children.Add(badge);
        nameCell.Children.Add(name);
        TextBlock Cell(int column)
        {
            var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0), FontSize = 13 };
            Grid.SetColumn(t, column);
            g.Children.Add(t);
            return t;
        }
        g.Children.Add(nameCell);
        var size = Cell(1);
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, MinHeight = 8, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(progress, 2);
        g.Children.Add(progress);
        var speed = Cell(3);
        var left = Cell(4);
        var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var statusText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        statusPanel.Children.Add(dot);
        statusPanel.Children.Add(statusText);
        Grid.SetColumn(statusPanel, 5);
        g.Children.Add(statusPanel);
        var queue = Cell(6);
        var added = Cell(7);
        string lastName = "";
        void Update()
        {
            if (row.Name != lastName)
            {
                lastName = row.Name;
                name.Text = row.Name;
                ToolTip.SetTip(name, row.Record.Url);
                var b = Ui.FileBadge(row.Name, 22);
                b.Margin = badge.Margin;
                DockPanel.SetDock(b, Dock.Left);
                nameCell.Children[0] = b;
            }
            size.Text = row.Size;
            progress.Value = row.Percent;
            progress.Foreground = Ui.Brush(row.StatusColor);
            progress.IsIndeterminate = row.PreparingVideo;
            speed.Text = row.Speed;
            left.Text = row.TimeLeft;
            statusText.Text = row.Status;
            dot.Background = Ui.Brush(row.StatusColor);
            queue.Text = row.Queue;
            added.Text = row.Added;
        }
        Update();
        void Changed(object? s, System.ComponentModel.PropertyChangedEventArgs e) => Update();
        g.AttachedToVisualTree += (_, _) => { row.PropertyChanged -= Changed; row.PropertyChanged += Changed; Update(); };
        g.DetachedFromVisualTree += (_, _) => row.PropertyChanged -= Changed;
        return g;
    }

    // ---- sidebar ----

    public void BuildSidebar()
    {
        rebuilding = true;
        sidebar.Items.Clear();
        void Header(string text) => sidebar.Items.Add(new ListBoxItem
        {
            Content = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, Opacity = 0.6 },
            IsEnabled = false, Margin = new Thickness(4, 8, 0, 0), Padding = new Thickness(12, 4),
        });
        void Entry(string text, string key, int indent = 0)
        {
            var row = new DockPanel { LastChildFill = true };
            var count = new TextBlock { Text = CountFor(key), FontSize = 11, Opacity = 0.6, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            sidebar.Items.Add(new ListBoxItem { Content = row, Tag = key, Padding = new Thickness(16 + indent * 16, 5, 8, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        // Like IDM: each list opens into the file types it holds (sorted automatically by file type).
        static string Label(string c) => L.T(c == nameof(FileCategory.Other) ? "General" : c);
        void Types(string prefix) { foreach (var c in Enum.GetNames<FileCategory>()) Entry(Label(c), prefix + c, 1); }
        Header(L.T("Downloads"));
        Entry(L.T("All Downloads"), "all");
        Types("cat:");
        Entry(L.T("In Progress"), "active");
        Entry(L.T("Unfinished"), "unfinished");
        Types("ucat:");
        Entry(L.T("Finished"), "finished");
        Types("fcat:");
        Header(L.T("Queues"));
        foreach (var q in manager.Queues) Entry((q.Running ? "▶ " : "⏸ ") + (q.IsMain ? L.T(q.Name) : q.Name), "queue:" + q.Id);
        sidebar.SelectedItem = sidebar.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string?)i.Tag == filter)
                               ?? sidebar.Items.OfType<ListBoxItem>().First(i => (string?)i.Tag == "all");
        rebuilding = false;
        Refilter(force: true);
    }

    static bool Matches(string f, DownloadRecord r) => f switch
    {
        "active" => r.IsActive || r.Status == DownloadStatus.Queued,
        "unfinished" => r.Status != DownloadStatus.Finished,
        "finished" => r.Status == DownloadStatus.Finished,
        _ when f.StartsWith("cat:") => r.Category.ToString() == f[4..],
        _ when f.StartsWith("ucat:") => r.Status != DownloadStatus.Finished && r.Category.ToString() == f[5..],
        _ when f.StartsWith("fcat:") => r.Status == DownloadStatus.Finished && r.Category.ToString() == f[5..],
        _ when f.StartsWith("queue:") => r.Queue == f[6..],
        _ => true,
    };

    string CountFor(string key)
    {
        var n = manager.Rows.Count(r => Matches(key, r.Record));
        return n > 0 ? n.ToString() : "";
    }

    string CurrentQueue => filter.StartsWith("queue:") ? filter[6..] : DownloadQueue.MainId;

    void OnFilter()
    {
        if (rebuilding || sidebar.SelectedItem is not ListBoxItem { Tag: string key }) return;
        filter = key;
        Refilter(force: true);
    }

    /// <summary>Shows the downloads that match the list and the search, sorted; keeps the selection.</summary>
    void Refilter(bool force = false)
    {
        var q = (search.Text ?? "").Trim();
        IEnumerable<Row> rows = manager.Rows.Where(r => Matches(filter, r.Record) && (q.Length == 0
            || r.Record.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Record.Url.Contains(q, StringComparison.OrdinalIgnoreCase)));
        Func<Row, IComparable> by = sortKey switch
        {
            "name" => r => r.Name.ToLowerInvariant(),
            "size" => r => r.Record.Size ?? -1,
            "progress" => r => r.Percent,
            "speed" => r => r.SpeedBytes,
            "left" => r => r.TimeLeft,
            "status" => r => r.Status,
            "queue" => r => r.Queue,
            _ => r => r.Record.Added,
        };
        var sorted = (sortDescending ? rows.OrderByDescending(by) : rows.OrderBy(by)).ToList();
        if (!force && sorted.SequenceEqual(view)) return;
        var selected = Selected;
        view.Clear();
        foreach (var r in sorted) view.Add(r);
        foreach (var r in selected)
        {
            var i = view.IndexOf(r);
            if (i >= 0) list.Selection.Select(i);
        }
    }

    void UpdateStatus()
    {
        var active = manager.Rows.Count(r => r.Record.IsActive);
        var queued = manager.Rows.Count(r => r.Record.Status == DownloadStatus.Queued);
        var speed = manager.Rows.Where(r => r.Record.IsActive).Sum(r => r.SpeedBytes);
        var selected = Selected;
        statusLeft.Text = $"{active} downloading   ·   {queued} queued" + (speed > 0 ? $"   ·   {Row.Bytes((long)speed)}/s" : "")
            + (selected.Count > 1 ? $"   ·   {selected.Count} selected, {Row.Bytes(selected.Sum(r => r.Record.Size ?? 0))}" : "");
        statusRight.Text = (manager.Settings.SpeedLimitKBps > 0 ? $"Limit {manager.Settings.SpeedLimitKBps} KB/s   ·   " : "")
                           + $"{manager.Queues.Count(q => q.Running)} of {manager.Queues.Count} queues running";
        var isEmpty = view.Count == 0;
        empty.IsVisible = isEmpty;
        list.IsVisible = !isEmpty;
        emptyTitle.Text = (search.Text ?? "").Length > 0 ? $"Nothing matches “{search.Text}”" : "No downloads here yet";
        if (++ticks % 4 == 0)
        {
            foreach (var item in sidebar.Items.OfType<ListBoxItem>())
                if (item is { Tag: string key, Content: DockPanel dp } && dp.Children[0] is TextBlock c) c.Text = CountFor(key);
            Refilter();   // downloads that finished move to the right list
        }
    }

    // ---- selection and details ----

    List<Row> Selected => list.SelectedItems?.OfType<Row>().ToList() ?? new List<Row>();
    Row? One => list.SelectedItems is { Count: 1 } ? list.SelectedItem as Row : null;

    void ShowDetails()
    {
        if (One is not { } row) { details.IsVisible = false; return; }
        var r = row.Record;
        details.IsVisible = true;
        detailName.Text = r.FileName;
        detailUrl.Text = r.Url;
        bar.Update(r);
        var parts = new List<string> { $"{Row.Bytes(r.Received)} of {(r.Size is long s ? Row.Bytes(s) : "unknown size")}" };
        if (r.IsActive)
        {
            if (row.Speed.Length > 0) parts.Add(row.Speed);
            if (row.TimeLeft.Length > 0) parts.Add(row.TimeLeft + " left");
            if (r.SiteVideo == true) parts.Add("Video site · " + SiteVideo.ChoiceText(r.AudioOnly, r.MaxHeight));
            else if (r.IsStream == true) parts.Add($"Video pieces: {r.StreamDone ?? 0} of {r.StreamTotal ?? 0}");
            else lock (r.Segments) parts.Add($"Connections: {r.Segments.Count(x => !x.IsComplete)}");
        }
        parts.Add(r.AcceptsRanges || r.Status == DownloadStatus.Queued ? "Resume: yes" : "Resume: no");
        parts.Add("Queue: " + row.Queue);
        if (r.SpeedLimitKBps is int l) parts.Add($"Limit: {l} KB/s");
        if (r.ChecksumResult is { } c) parts.Add(c);
        detailInfo.Text = string.Join("   ·   ", parts);
        detailError.Text = r.Error ?? "";
        detailError.IsVisible = !string.IsNullOrEmpty(r.Error);
    }

    void OnDoubleClick()
    {
        if (One is not { } row) return;
        if (row.Record.Status == DownloadStatus.Finished && row.Record.SavedPath is { } p) LinuxSystem.Open(p);
        else DownloadWindows.ShowProgress(manager, row);
    }

    void ToggleSelectAll()
    {
        if (view.Count > 0 && list.SelectedItems?.Count == view.Count) list.Selection.Clear();
        else list.SelectAll();
        list.Focus();
    }

    // ---- actions ----

    async Task OnAdd() => await ShowAdd(await Ui.ClipboardLink(this), CurrentQueue);

    bool adding;

    /// <summary>The Add window (one at a time: the clipboard may offer another link meanwhile).</summary>
    public async Task ShowAdd(string links, string? queueId = null)
    {
        if (adding) return;
        adding = true;
        try
        {
            Bring();
            var one = links.Trim();
            if (!one.Contains('\n') && Playlists.IsPlaylist(one)) { adding = false; await OpenPlaylist(one); return; }
            await Dialogs.Add(this, manager, links, queueId ?? DownloadQueue.MainId);
        }
        finally { adding = false; }
    }

    public void Bring()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Asks before anything is removed. Files stay unless the user chose the trash.</summary>
    async Task ConfirmRemove(List<Row> rows, bool toTrash)
    {
        if (rows.Count == 0) return;
        var what = rows.Count == 1 ? "this download" : $"these {rows.Count} downloads";
        var text = toTrash
            ? $"Delete {what} and move the {(rows.Count == 1 ? "file" : "files")} to the trash?\n\nYou can still restore them from the trash."
            : $"Remove {what} from the list?\n\nThe downloaded files stay on your computer.";
        if (!await Ui.Confirm(this, "Delete", text, "Delete")) return;
        var failed = new List<string>();
        foreach (var r in rows)
        {
            if (toTrash && r.Record.Torrent?.Files.Count > 1) { failed.Add(r.Record.FileName + " (move torrent folders manually)"); continue; }
            if (toTrash && r.Record.SavedPath is { } p && File.Exists(p) && !LinuxSystem.Trash(p)) { failed.Add(r.Record.FileName); continue; }
            manager.Remove(r);
        }
        if (failed.Count > 0)
            await Ui.Message(this, "Not moved to the trash", "These files could not be moved to the trash, so they stay in the list:\n" + string.Join("\n", failed));
    }

    async Task OnClear()
    {
        var done = manager.Rows.Where(r => r.Record.Status == DownloadStatus.Finished).ToList();  // only the ones counted in the question
        var n = done.Count;
        if (n == 0) return;
        if (await Ui.Confirm(this, "Delete Completed", $"Remove {n} completed {(n == 1 ? "download" : "downloads")} from the list?\n\nThe downloaded files stay on your computer.", "Delete"))
            foreach (var r in done) manager.Remove(r);
    }

    async Task OnScheduler()
    {
        await Dialogs.Scheduler(this, manager, CurrentQueue);
        BuildSidebar();
    }

    ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action click)
        {
            var m = new MenuItem { Header = header };
            m.Click += (_, _) => click();
            menu.Items.Add(m);
            return m;
        }
        Item("Open", () => { if (One?.Record.SavedPath is { } p) LinuxSystem.Open(p); });
        Item("Show in Folder", () => { if (One?.Record.SavedPath is { } p) LinuxSystem.ShowInFolder(p); });
        Item("Show Progress", () => { if (One is { } r) DownloadWindows.ShowProgress(manager, r); });
        menu.Items.Add(new Separator());
        Item("Resume", () => { foreach (var r in Selected) manager.Resume(r); });
        Item("Stop", () => { foreach (var r in Selected) manager.Pause(r); });
        Item("Refresh Link…", () => { if (One is { } r) _ = Dialogs.RefreshLink(this, manager, r); });
        var torrentFiles = Item("Torrent Files…", () => { if (One is { } r && r.Record.Torrent != null) _ = Dialogs.TorrentFiles(this, manager, r); });
        Item("Properties…", () => { if (One is { } r) _ = Dialogs.Properties(this, manager, r); });
        Item("Copy Link", () => { if (One is { } r) _ = Clipboard?.SetTextAsync(r.Record.Url); });
        Item("Show QR Code…", () => { if (Selected.Count > 0) _ = Dialogs.ShowQr(this, Selected.Select(r => r.Record.Url).ToList()); });
        menu.Items.Add(moveMenu);
        menu.Items.Add(new Separator());
        Item("Delete…", () => _ = ConfirmRemove(Selected, toTrash: false));
        Item("Delete and Move File to Trash…", () => _ = ConfirmRemove(Selected, toTrash: true));
        menu.Opening += (_, _) =>
        {
            torrentFiles.IsEnabled = One?.Record.Torrent != null;
            moveMenu.Items.Clear();
            foreach (var q in manager.Queues)
            {
                var item = new MenuItem { Header = q.Name };
                var id = q.Id;
                item.Click += (_, _) =>
                {
                    foreach (var r in Selected)
                        manager.SetDetails(r, r.Record.Username ?? "", r.Record.Password ?? "", r.Record.Checksum ?? "", r.Record.SpeedLimitKBps ?? 0, id);
                };
                moveMenu.Items.Add(item);
            }
        };
        return menu;
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        var text = e.Data.GetText();
        if (string.IsNullOrWhiteSpace(text) && e.Data.GetFiles() is { } files)
            text = string.Join("\n", files.Select(f => f.TryGetLocalPathSafe()).Where(p => p != null).SelectMany(p => LaunchArgs.LinksIn(p!)));
        if (!string.IsNullOrWhiteSpace(text)) _ = ShowAdd(text, CurrentQueue);
    }

    sealed class Cmd(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => run();
    }
}

static class StorageItemExtensions
{
    public static string? TryGetLocalPathSafe(this Avalonia.Platform.Storage.IStorageItem item)
    {
        try { return Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(item); } catch { return null; }
    }
}

/// <summary>One bar for the whole file; each range shows how much of it has arrived.</summary>
public sealed class SegmentBar : Control
{
    List<(long Start, long Written, bool Done)> parts = new();
    long size;

    public void Update(DownloadRecord r)
    {
        lock (r.Segments) parts = r.Segments.Select(s => (s.Start, s.Written, s.IsComplete)).ToList();
        size = r.Size ?? 0;
        if (r.Status == DownloadStatus.Finished && parts.Count == 0 && size > 0) parts.Add((0, size, true));
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        dc.DrawRectangle(Ui.Shade, null, new Rect(0, 0, w, h), 3, 3);
        if (size <= 0) return;
        foreach (var (start, written, done) in parts)
        {
            var x = w * start / size;
            var width = Math.Max(0.5, w * written / size);
            dc.FillRectangle(done ? Ui.Good : Ui.Accent, new Rect(x, 0, width, h));
        }
    }
}

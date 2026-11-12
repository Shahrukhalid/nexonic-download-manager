using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Nexonic.Core;

namespace Nexonic.App;

public partial class MainWindow : Window
{
    readonly DownloadManager manager;
    string filter = "all";
    bool rebuilding;

    public MainWindow(DownloadManager manager)
    {
        InitializeComponent();
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/app.ico", UriKind.Absolute));
        WindowPlace.Restore(this);
        this.manager = manager;
        Grid.ItemsSource = manager.Rows;
        RestoreColumnLayout();
        Grid.ColumnReordered += (_, _) => SaveColumnLayout();
        BuildSidebar();
        manager.Queues.CollectionChanged += (_, _) => BuildSidebar();
        EnableDragToQueues();
        InputBindings.Add(new KeyBinding(new Cmd(() => OnAdd(this, new RoutedEventArgs())), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Cmd(() => OnRemove(this, new RoutedEventArgs())), Key.Delete, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new Cmd(() => Search.Focus()), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Cmd(() => { Search.Focus(); Search.SelectAll(); }), Key.F3, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new Cmd(() => HelpWindows.ShowHelp(this)), Key.F1, ModifierKeys.None));
        void Shortcut(Key key, ModifierKeys mods, RoutedEventHandler handler) =>
            InputBindings.Add(new KeyBinding(new Cmd(() => { if (Keyboard.FocusedElement is not TextBox) handler(this, new RoutedEventArgs()); }), key, mods));
        Shortcut(Key.R, ModifierKeys.Control, OnResume);
        Shortcut(Key.OemPeriod, ModifierKeys.Control, OnPause);
        Shortcut(Key.R, ModifierKeys.Control | ModifierKeys.Shift, OnResumeAll);
        Shortcut(Key.P, ModifierKeys.Control | ModifierKeys.Shift, OnPauseAll);
        Shortcut(Key.O, ModifierKeys.Control, OnOpen);
        Shortcut(Key.I, ModifierKeys.Control, OnProperties);
        Shortcut(Key.T, ModifierKeys.Control | ModifierKeys.Shift, OnTorrents);
        Shortcut(Key.B, ModifierKeys.Control | ModifierKeys.Shift, OnBatch);
        Shortcut(Key.G, ModifierKeys.Control | ModifierKeys.Shift, OnGrabber);
        Shortcut(Key.I, ModifierKeys.Control | ModifierKeys.Shift, OnGallery);
        Shortcut(Key.K, ModifierKeys.Control | ModifierKeys.Shift, OnLinkGrabber);
        // Screen readers announce each toolbar button by its name and tooltip, also when the names under the icons are hidden.
        foreach (var b in Toolbar.Children.OfType<Button>())
            if (b.Content is StackPanel p && p.Children.OfType<TextBlock>().LastOrDefault() is { } caption)
            {
                System.Windows.Automation.AutomationProperties.SetName(b, caption.Text);
                if (b.ToolTip is string tip) System.Windows.Automation.AutomationProperties.SetHelpText(b, tip);
            }
        Shortcut(Key.L, ModifierKeys.Control | ModifierKeys.Shift, OnPlaylist);
        Shortcut(Key.L, ModifierKeys.Control, OnLibrary);
        VideoSubtitles.Enabled = manager.Settings.VideoSubtitles;
        ApplyToolbar();
        BuildColumnsMenu();
        UpdateButtons();
        UpdateStatus();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        var ticks = 0;
        timer.Tick += (_, _) =>
        {
            ShowDetails(); UpdateStatus();
            ticks++;
            if (ticks % 10 == 1) manager.ApplyIdleLimit();                                   // every 5 seconds
            if (ticks % 120 == 1) _ = feeds.CheckAsync(manager.Settings.Proxy, AddFeedItem);  // feeds that are due, every minute
        };
        timer.Start();
    }

    /// <summary>The column order and widths as you left them (columns are named by their place in the original order).</summary>
    void RestoreColumnLayout()
    {
        var saved = ColumnLayout.Parse(manager.Settings.ColumnLayout);
        if (saved.Count != Grid.Columns.Count) return;  // a different set of columns: start from the default layout
        try
        {
            foreach (var (i, column) in Grid.Columns.Select((c, i) => (i, c)).OrderBy(x => saved.TryGetValue(x.i.ToString(), out var v) ? v.Index : x.i))
            {
                if (!saved.TryGetValue(i.ToString(), out var v)) continue;
                column.DisplayIndex = Math.Min(v.Index, Grid.Columns.Count - 1);
                if (!double.IsNaN(v.Width) && !column.Width.IsStar) column.Width = new DataGridLength(v.Width);
            }
        }
        catch (ArgumentOutOfRangeException) { }  // an inconsistent saved layout keeps the default
    }

    void SaveColumnLayout()
    {
        var layout = ColumnLayout.Format(Grid.Columns.Select((c, i) => (i.ToString(), c.DisplayIndex, c.ActualWidth)));
        if (layout == manager.Settings.ColumnLayout) return;
        manager.Settings.ColumnLayout = layout;
        manager.SaveSettings();
    }

    // Closing the window keeps downloads running in the tray.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        SaveColumnLayout();
        manager.Save();
        WindowPlace.Save(this);
        Hide();
    }

    // ---- sidebar ----

    public void BuildSidebar()
    {
        rebuilding = true;
        Sidebar.Items.Clear();
        void Header(string text) => Sidebar.Items.Add(new ListBoxItem
        {
            Content = text.ToUpperInvariant(), IsEnabled = false, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Gray, Margin = new Thickness(4, 8, 0, 2),
        });
        void Entry(string text, string key, int indent = 0)
        {
            var row = new DockPanel { LastChildFill = true, MinWidth = 150 };
            var count = new TextBlock { Text = CountFor(key), Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            Sidebar.Items.Add(new ListBoxItem { Content = row, Tag = key, Padding = new Thickness(14 + indent * 16, 5, 4, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch });
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
        if (FileCategories.Custom.Count > 0)
        {
            // The user's own categories, in their order (Options → Categories), with their icons.
            Header(L.T("Categories"));
            foreach (var c in FileCategories.Custom) Entry((c.Icon.Length > 0 ? c.Icon + " " : "") + c.Name, "ccat:" + c.Name);
        }
        Header(L.T("Queues"));
        foreach (var q in manager.Queues) Entry((q.Running ? "▶ " : "⏸ ") + (q.IsMain ? L.T(q.Name) : q.Name), "queue:" + q.Id);
        Sidebar.SelectedItem = Sidebar.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string?)i.Tag == filter)
                               ?? Sidebar.Items.OfType<ListBoxItem>().First(i => (string?)i.Tag == "all");
        rebuilding = false;
        ApplyFilter();
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
        _ when f.StartsWith("ccat:") => FileCategories.CustomFor(r.FileName, r.Url)?.Name == f[5..],
        _ => true,
    };

    string CountFor(string key)
    {
        var n = manager.Rows.Count(r => Matches(key, r.Record));
        return n > 0 ? n.ToString() : "";
    }

    void UpdateStatus()
    {
        var active = manager.Rows.Count(r => r.Record.IsActive);
        var queued = manager.Rows.Count(r => r.Record.Status == DownloadStatus.Queued);
        var speed = manager.Rows.Where(r => r.Record.IsActive).Sum(r => r.SpeedBytes);
        var selected = Selected;
        StatusLeft.Text = $"{active} downloading   ·   {queued} queued" + (speed > 0 ? $"   ·   {Units.Speed(speed)}" : "")
            + (selected.Count > 1 ? $"   ·   {selected.Count} selected, {Row.Bytes(selected.Sum(r => r.Record.Size ?? 0))}" : "");
        StatusRight.Text = (manager.Settings.SpeedLimitKBps > 0 ? $"Limit {manager.Settings.SpeedLimitKBps} KB/s   ·   " : "")
                           + $"{manager.Queues.Count(q => q.Running)} of {manager.Queues.Count} queues running";
        var view = CollectionViewSource.GetDefaultView(Grid.ItemsSource);
        var empty = view.IsEmpty;
        Empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Grid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.Text = Search.Text.Length > 0 ? $"Nothing matches “{Search.Text}”" : "No downloads here yet";
        if (++statusTicks % 4 == 0)
            foreach (var item in Sidebar.Items.OfType<ListBoxItem>())
                if (item is { Tag: string key, Content: DockPanel dp } && dp.Children[0] is TextBlock c) c.Text = CountFor(key);
    }

    int statusTicks;

    void OnSearch(object s, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        ApplyFilter();
        UpdateStatus();
    }

    string CurrentQueue => filter.StartsWith("queue:") ? filter[6..] : DownloadQueue.MainId;

    void OnFilter(object s, SelectionChangedEventArgs e)
    {
        if (rebuilding || Sidebar.SelectedItem is not ListBoxItem { Tag: string key }) return;
        filter = key;
        ApplyFilter();
    }

    void ApplyFilter()
    {
        var f = filter;
        var q = Search.Text.Trim();
        CollectionViewSource.GetDefaultView(Grid.ItemsSource).Filter = o =>
        {
            var r = ((Row)o).Record;
            return Matches(f, r) && (q.Length == 0 || r.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                     || r.Url.Contains(q, StringComparison.OrdinalIgnoreCase));
        };
    }

    // ---- selection and details ----

    List<Row> Selected => Grid.SelectedItems.OfType<Row>().ToList();
    Row? One => Grid.SelectedItems.Count == 1 ? Grid.SelectedItem as Row : null;

    void OnSelection(object s, SelectionChangedEventArgs e) { ShowDetails(); UpdateButtons(); }

    /// <summary>Resume, Stop and Delete work on the selection, so they are greyed out without one (as in IDM).</summary>
    void UpdateButtons()
    {
        var any = Grid.SelectedItems.Count > 0;
        ResumeButton.IsEnabled = StopButton.IsEnabled = DeleteButton.IsEnabled = any;
    }

    /// <summary>View menu: show or hide the toolbar buttons and their names (saved with the settings).</summary>
    void ApplyToolbar()
    {
        var showToolbar = manager.Settings.ShowToolbar;
        var showNames = manager.Settings.ToolbarText;
        Toolbar.Visibility = showToolbar ? Visibility.Visible : Visibility.Collapsed;
        ShowToolbarItem.IsChecked = showToolbar;
        ShowNamesItem.IsChecked = showNames;
        ShowNamesItem.IsEnabled = showToolbar;
        UnzipItem.IsChecked = manager.Settings.ExtractZips;
        SubtitlesItem.IsChecked = manager.Settings.VideoSubtitles;
        foreach (var b in Toolbar.Children.OfType<Button>())
            if (b.Content is StackPanel p && p.Children.Count > 1) p.Children[1].Visibility = showNames ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnToggleToolbar(object s, RoutedEventArgs e) { manager.Settings.ShowToolbar = ShowToolbarItem.IsChecked; manager.ApplySettings(manager.Settings); ApplyToolbar(); }
    void OnArchivePasswords(object s, RoutedEventArgs e) => Dialogs.ArchivePasswords(this, manager);
    void OnToggleUnzip(object s, RoutedEventArgs e) { manager.Settings.ExtractZips = UnzipItem.IsChecked; manager.ApplySettings(manager.Settings); }
    void OnToggleSubtitles(object s, RoutedEventArgs e)
    {
        manager.Settings.VideoSubtitles = VideoSubtitles.Enabled = SubtitlesItem.IsChecked;
        manager.ApplySettings(manager.Settings);
    }
    void OnPlaylist(object s, RoutedEventArgs e) => ShowPlaylist("");
    void OnLibrary(object s, RoutedEventArgs e) => Dialogs.MediaLibraryWindow(this, manager);

    /// <summary>Playlists and channels: pick their videos first, then they open in the Add window.</summary>
    void ShowPlaylist(string link)
    {
        var queue = CurrentQueue;
        Bring();
        Dialogs.ProfileBatchWindow(this, manager, link);
    }

    /// <summary>Drag downloads from the list onto a queue in the sidebar to move them there.</summary>
    void EnableDragToQueues()
    {
        const string format = "NexonicDownloads";
        Point? start = null;
        static T? Parent<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }
        Grid.PreviewMouseLeftButtonDown += (_, e) => start = Parent<DataGridRow>(e.OriginalSource as DependencyObject) != null ? e.GetPosition(null) : null;
        Grid.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || start is not { } p) return;
            var d = e.GetPosition(null) - p;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            var ids = Grid.SelectedItems.OfType<Row>().Select(r => r.Record.Id.ToString()).ToArray();
            if (ids.Length > 0) DragDrop.DoDragDrop(Grid, new DataObject(format, string.Join(",", ids)), DragDropEffects.Move);
        };
        string? QueueAt(DragEventArgs e) =>
            Parent<ListBoxItem>(e.OriginalSource as DependencyObject) is { Tag: string key } && key.StartsWith("queue:", StringComparison.Ordinal) ? key[6..] : null;
        Sidebar.AllowDrop = true;
        Sidebar.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(format) && QueueAt(e) != null ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        Sidebar.Drop += (_, e) =>
        {
            if (e.Data.GetData(format) is string list && QueueAt(e) is { } queue)
            {
                var ids = list.Split(',').ToHashSet();
                manager.MoveToQueue(manager.Rows.Where(r => ids.Contains(r.Record.Id.ToString())), queue);
                BuildSidebar();
            }
            e.Handled = true;
        };
    }

    /// <summary>View → Columns: turn the list's extra columns on or off (saved with the settings).</summary>
    void BuildColumnsMenu()
    {
        var columns = new Dictionary<string, DataGridColumn>
        {
            ["queue"] = QueueColumn, ["added"] = AddedColumn, ["finished"] = FinishedColumn, ["folder"] = FolderColumn, ["resume"] = ResumeColumn,
            ["peers"] = PeersColumn, ["ratio"] = RatioColumn, ["uploaded"] = UploadedColumn,
        };
        void Apply()
        {
            foreach (var (key, column) in columns)
                column.Visibility = manager.Settings.HiddenListColumns.Contains(key) ? Visibility.Collapsed : Visibility.Visible;
        }
        ColumnsItem.Items.Clear();
        foreach (var (key, title) in ListColumns.Optional)
        {
            var item = new MenuItem { Header = L.T(title), IsCheckable = true, IsChecked = !manager.Settings.HiddenListColumns.Contains(key) };
            item.Click += (_, _) =>
            {
                var hidden = manager.Settings.HiddenListColumns;
                if (item.IsChecked) hidden.Remove(key); else if (!hidden.Contains(key)) hidden.Add(key);
                manager.ApplySettings(manager.Settings);
                Apply();
            };
            ColumnsItem.Items.Add(item);
        }
        Apply();
    }

    void OnToggleNames(object s, RoutedEventArgs e) { manager.Settings.ToolbarText = ShowNamesItem.IsChecked; manager.ApplySettings(manager.Settings); ApplyToolbar(); }

    void ShowDetails()
    {
        if (One is not { } row) { Details.Visibility = Visibility.Collapsed; return; }
        var r = row.Record;
        Details.Visibility = Visibility.Visible;
        DetailName.Text = r.FileName;
        DetailUrl.Text = r.Url;
        Bar.Update(r);
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
        DetailInfo.Text = string.Join("   ·   ", parts);
        DetailError.Text = r.Error ?? "";
    }

    // ---- toolbar ----

    void OnTorrents(object s, RoutedEventArgs e) => Dialogs.TorrentManager(this, manager, CurrentQueue);

    void OnAdd(object s, RoutedEventArgs e) => ShowAdd(Dialogs.ClipboardLink(), CurrentQueue);

    bool adding;

    public void ShowAdd(string links, string? queueId = null)
    {
        if (adding) return; // one Add window at a time (the clipboard may offer another link meanwhile)
        adding = true;
        try
        {
            Bring();
            if (links.Trim() is var one && !one.Contains('\n') && Playlists.IsPlaylist(one)) { adding = false; ShowPlaylist(one); return; }
            if (SmartDownload.Detect(links) == DownloadEngine.Torrent) Dialogs.Torrent(this, manager, links, queueId ?? DownloadQueue.MainId);
            else Dialogs.Add(this, manager, links, queueId ?? DownloadQueue.MainId);
        }
        finally { adding = false; }
    }

    public void Bring()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void OnResume(object s, RoutedEventArgs e) { foreach (var r in Selected) manager.Resume(r); }
    void OnPause(object s, RoutedEventArgs e) { foreach (var r in Selected) manager.Pause(r); }
    void OnRemove(object s, RoutedEventArgs e) => ConfirmRemove(Selected, toRecycleBin: false);

    void OnSelectAll(object s, RoutedEventArgs e)
    {
        if (Grid.Items.Count > 0 && Grid.SelectedItems.Count == Grid.Items.Count) Grid.UnselectAll();
        else Grid.SelectAll();
        Grid.Focus();
    }

    /// <summary>Asks before anything is removed. Files stay unless the user chose the Recycle Bin.</summary>
    void ConfirmRemove(List<Row> rows, bool toRecycleBin)
    {
        if (rows.Count == 0) return;
        var what = rows.Count == 1 ? "this download" : $"these {rows.Count} downloads";
        var text = toRecycleBin
            ? $"Delete {what} and move the {(rows.Count == 1 ? "file" : "files")} to the Recycle Bin?\n\nYou can still restore them from the Recycle Bin."
            : $"Remove {what} from the list?\n\nThe downloaded files stay on your PC.";
        if (MessageBox.Show(this, text, "Delete", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        foreach (var r in rows)
        {
            if (toRecycleBin && r.Record.SavedPath is { } p && System.IO.File.Exists(p))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            manager.Remove(r);
        }
    }
    void OnPauseAll(object s, RoutedEventArgs e) => manager.PauseAll();
    void OnResumeAll(object s, RoutedEventArgs e) => manager.ResumeAll();
    void OnExit(object s, RoutedEventArgs e) { manager.Save(); WindowPlace.Save(this); manager.RequestQuit(); }
    void OnClear(object s, RoutedEventArgs e)
    {
        var done = manager.Rows.Where(r => r.Record.Status == DownloadStatus.Finished).ToList();  // only the ones counted in the question
        var n = done.Count;
        if (n == 0) return;
        if (MessageBox.Show(this, $"Remove {n} completed {(n == 1 ? "download" : "downloads")} from the list?\n\nThe downloaded files stay on your PC.",
                "Delete Completed", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK)
            foreach (var r in done) manager.Remove(r);
    }
    void OnScheduler(object s, RoutedEventArgs e) { Dialogs.Scheduler(this, manager, CurrentQueue); BuildSidebar(); }
    void OnStartQueue(object s, RoutedEventArgs e) { manager.StartQueue(CurrentQueue); BuildSidebar(); }
    void OnStopQueue(object s, RoutedEventArgs e) { manager.StopQueue(CurrentQueue); BuildSidebar(); }
    void OnGrabber(object s, RoutedEventArgs e) => Dialogs.Grabber(this, manager, CurrentQueue);
    readonly FeedRunner feeds = new();

    /// <summary>A new feed item: files and videos are added and started; torrents too when torrents are on.</summary>
    async Task AddFeedItem(FeedSubscription feed, FeedItem item)
    {
        var folder = feed.Folder.Length > 0 ? feed.Folder : manager.Settings.Folder;
        if (SmartDownload.Detect(item.Link) == DownloadEngine.Torrent)
        {
            if (TorrentRuntime.Unavailable(manager.Settings) is { } reason) throw new InvalidOperationException(reason);
            var metadata = await TorrentRuntime.PreviewAsync(item.Link, default, manager.Settings.Proxy);
            await Dispatcher.InvokeAsync(() => manager.AddTorrent(item.Link, metadata, new AddOptions { Folder = folder, Start = true }));
        }
        else await Dispatcher.InvokeAsync(() => manager.AddLinks(item.Link, new AddOptions { Folder = folder, Start = true }));
    }

    void OnFeeds(object s, RoutedEventArgs e) => Dialogs.Feeds(this, manager, feeds, AddFeedItem);

    void OnBatch(object s, RoutedEventArgs e) => Dialogs.Batch(this, manager, CurrentQueue);
    void OnMetalink(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = "Open Metalink File", Filter = "Metalink (*.meta4;*.metalink)|*.meta4;*.metalink" };
        if (d.ShowDialog(this) != true) return;
        var info = new System.IO.FileInfo(d.FileName);
        var n = info.Length <= 4_000_000 ? manager.AddMetalink(System.IO.File.ReadAllText(d.FileName), new AddOptions { QueueId = CurrentQueue }) : 0;
        if (n == 0) MessageBox.Show(this, "This file has no downloads in it, or it is not a Metalink file.", "Open Metalink File", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    void OnSettings(object s, RoutedEventArgs e) { Dialogs.Options(this, manager); BuildSidebar(); }
    void OnHelp(object s, RoutedEventArgs e) => HelpWindows.ShowHelp(this);
    void OnAbout(object s, RoutedEventArgs e) => HelpWindows.ShowAbout(this);
    void OnSupport(object s, RoutedEventArgs e) => HelpWindows.Open(HelpWindows.Support);

    void OnRemoveFile(object s, RoutedEventArgs e) => ConfirmRemove(Selected, toRecycleBin: true);

    void OnOpen(object s, RoutedEventArgs e)
    {
        if (One?.Record.SavedPath is { } p) Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
    }

    /// <summary>Double-click: a finished download opens its file; any other shows its progress window.</summary>
    void OnRowDoubleClick(object s, RoutedEventArgs e)
    {
        if (One is not { } row) return;
        if (row.Record.Status == DownloadStatus.Finished && row.Record.SavedPath is { } p) Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
        else DownloadWindows.ShowProgress(manager, row);
    }

    void OnShowInFolder(object s, RoutedEventArgs e)
    {
        if (One?.Record.SavedPath is { } p) Process.Start("explorer.exe", $"/select,\"{p}\"");
    }

    /// <summary>Windows' "Open with" list for a finished file.</summary>
    void OnOpenWith(object s, RoutedEventArgs e)
    {
        if (One?.Record.SavedPath is { } p && System.IO.File.Exists(p))
            Process.Start(new ProcessStartInfo("rundll32.exe") { ArgumentList = { "shell32.dll,OpenAs_RunDLL", p }, UseShellExecute = false });
    }

    void OnShowProgress(object s, RoutedEventArgs e) { if (One is { } r) DownloadWindows.ShowProgress(manager, r); }
    void OnGallery(object s, RoutedEventArgs e) => Dialogs.Gallery(this, manager);
    void OnLinkGrabber(object s, RoutedEventArgs e) => Dialogs.LinkGrabber(this, manager);
    void OnCrashReports(object s, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(CrashReports.Folder);
        Process.Start("explorer.exe", $"\"{CrashReports.Folder}\"");
    }
    void OnDiagnostics(object s, RoutedEventArgs e) => Dialogs.Diagnostics(this, manager, One?.Record.Url);
    void OnRecheck(object s, RoutedEventArgs e) { if (One is { Record.Torrent: not null } r) manager.Recheck(r); }
    void OnShowQr(object s, RoutedEventArgs e) => Dialogs.ShowQr(this, Selected.Select(r => r.Record.Url).ToList());
    void OnChecksum(object s, RoutedEventArgs e)
    {
        var row = One;
        var path = row != null && row.Record.Status == DownloadStatus.Finished ? row.Record.SavedPath : null;
        Dialogs.VerifyChecksumDialog(this, path, row?.Record.Checksum);
    }
    void OnCopyLink(object s, RoutedEventArgs e) { if (One is { } r) Clipboard.SetText(r.Record.Url); }
    void OnCopyCurl(object s, RoutedEventArgs e) { if (One is { } r) Clipboard.SetText(CurlCommand.ToCommand(r.Record)); }
    void OnRefreshLink(object s, RoutedEventArgs e) { if (One is { } r) Dialogs.RefreshLink(this, manager, r); }
    void OnProperties(object s, RoutedEventArgs e) { if (One is { } r) Dialogs.Properties(this, manager, r); }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        MoveMenu.Items.Clear();
        RecheckItem.IsEnabled = One?.Record.Torrent is { MetadataPending: false };
        foreach (var q in manager.Queues)
        {
            var item = new MenuItem { Header = q.Name };
            var id = q.Id;
            item.Click += (_, _) =>
            {
                foreach (var r in Selected)
                    manager.SetDetails(r, r.Record.Username ?? "", r.Record.Password ?? "", r.Record.Checksum ?? "", r.Record.SpeedLimitKBps ?? 0, id);
            };
            MoveMenu.Items.Add(item);
        }
        base.OnContextMenuOpening(e);
    }

    /// <summary>A dropped .torrent file opens Add Torrent; dropped links (or text) open Add Download.</summary>
    void OnDrop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var torrent = files.FirstOrDefault(p => p.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(p));
            if (torrent != null) Dialogs.Torrent(this, manager, torrent, CurrentQueue);
            return;
        }
        if (e.Data.GetData(DataFormats.Text) is string text) ShowAdd(text, CurrentQueue);
    }

    void OnDragOver(object s, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    sealed class Cmd(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => run();
    }
}

/// <summary>One bar for the whole file; each range shows how much of it has arrived.</summary>
public sealed class SegmentBar : FrameworkElement
{
    List<(long Start, long Written, bool Done)> parts = new();
    long size;
    static readonly Brush Back = new SolidColorBrush(Color.FromRgb(0xE3, 0xE8, 0xEF));
    static readonly Brush Busy = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB));
    static readonly Brush Done = new SolidColorBrush(Color.FromRgb(0x2D, 0xA4, 0x4E));

    public void Update(DownloadRecord r)
    {
        lock (r.Segments) parts = r.Segments.Select(s => (s.Start, s.Written, s.IsComplete)).ToList();
        size = r.Size ?? 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Back, null, new Rect(0, 0, ActualWidth, ActualHeight), 3, 3);
        if (size <= 0) return;
        foreach (var (start, written, done) in parts)
        {
            var x = ActualWidth * start / size;
            var w = Math.Max(0.5, ActualWidth * written / size);
            dc.DrawRectangle(done ? Done : Busy, null, new Rect(x, 0, w, ActualHeight));
        }
    }
}

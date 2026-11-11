using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>Playlists and channels, and the Media Library (Milestone 8).</summary>
public static partial class Dialogs
{
    // ---- Profile Batch ----

    /// <summary>
    /// Profile Batch: one link to a profile, channel, playlist or page; every video found there that anyone can open without an
    /// account is listed (following the site's pages), filtered, and downloaded into a queue of its own in the chosen quality.
    /// </summary>
    public static void ProfileBatchWindow(Window owner, DownloadManager m, string link)
    {
        var f = new Form(owner, "Profile Batch", 660);
        f.Note("Paste one link to a profile, channel, playlist, gallery or page on any site the video engine supports. Nexonic finds every video " +
               "that is open to everyone (no login needed), skips the ones you already have and downloads the rest in their own queue.");
        var box = f.Text("Link", link.Length > 0 ? link : ClipboardLink());
        var quality = f.Field("Quality", new ComboBox { ItemsSource = ProfileBatch.Qualities.Select(q => L.T(q.Title)).ToList(), SelectedIndex = 0 });
        var mode = f.Field("Download", new ComboBox { ItemsSource = new[] { L.T("All videos I don't have yet"), L.T("Only new videos (not seen on this profile before)"), L.T("Let me preview and choose") }, SelectedIndex = 0 });
        var autoStart = f.Check("Start downloading straight away", true);
        var folder = f.Folder(m.Settings.StartFolder);
        var own = f.Check("Put each profile in a folder of its own", true);
        var parallel = f.Field("Downloads at the same time", new ComboBox { ItemsSource = new[] { "1", "2", "3", "4", "5", "6" }, SelectedIndex = 1 });
        DatePicker? from = null, to = null; TextBox? words = null, minMin = null, maxMin = null, maxCount = null; ComboBox? kind = null, check = null;
        f.Collapsed("Filters", () =>
        {
            from = f.Field("Published on or after", new DatePicker());
            to = f.Field("Published on or before", new DatePicker());
            words = f.Text("Title contains (words; !word leaves it out)", "");
            minMin = f.Text("Shortest, in minutes (empty = any)", "");
            maxMin = f.Text("Longest, in minutes (empty = any)", "");
            kind = f.Field("Type", new ComboBox { ItemsSource = new[] { L.T("Everything"), L.T("Videos (no shorts or live streams)"), L.T("Shorts and reels"), L.T("Live streams") }, SelectedIndex = 0 });
            maxCount = f.Text($"At most this many videos (up to {ProfileBatch.Limit})", ProfileBatch.DefaultMax.ToString());
            f.Note("A site that doesn't say when a video was published or how long it is: those videos are kept.");
        });
        f.Collapsed("Check this profile again", () =>
        {
            check = f.Field("Look for new videos", new ComboBox { ItemsSource = new[] { L.T("Never (only now)"), L.T("Every hour"), L.T("Every 6 hours"), L.T("Every day"), L.T("Every week") }, SelectedIndex = 0 });
            f.Note("Saved profiles are checked while Nexonic runs; new videos are downloaded in the same quality and folder.");
        });
        var status = f.Note("");
        var busy = new ProgressBar { IsIndeterminate = true, Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
        f.Body.Children.Add(busy);
        var list = new ListBox { Height = 240, Visibility = Visibility.Collapsed };
        f.Body.Children.Add(list);
        var checks = new List<(CheckBox Box, BatchEntry Entry)>();
        ProfileBatch.Scan? scan = null;
        System.Threading.CancellationTokenSource? cancel = null;
        Button? go = null, export = null;
        int[] hours = { 0, 1, 6, 24, 168 };

        BatchFilter Filter() => new()
        {
            From = from?.SelectedDate, To = to?.SelectedDate, Keywords = words?.Text ?? "",
            MinSeconds = int.TryParse(minMin?.Text, out var a) && a > 0 ? a * 60 : null, MaxSeconds = int.TryParse(maxMin?.Text, out var b) && b > 0 ? b * 60 : null,
            Kind = new[] { "all", "videos", "shorts", "live" }[Math.Max(0, kind?.SelectedIndex ?? 0)],
            Max = int.TryParse(maxCount?.Text, out var c) && c > 0 ? Math.Min(c, ProfileBatch.Limit) : ProfileBatch.DefaultMax,
        };
        string QualityKey() => ProfileBatch.Qualities[Math.Max(0, quality.SelectedIndex)].Key;
        string Target() => own.IsChecked == true && scan != null ? ProfileBatch.FolderFor(folder.Text.Trim(), scan.Owner, scan.Url) : folder.Text.Trim();
        SavedProfile? Saved(string url) => m.Profiles.FirstOrDefault(p => ProfileBatch.Key(p.Url) == ProfileBatch.Key(url));

        void Download(List<BatchEntry> take, BatchCounts counts)
        {
            var url = scan!.Url;
            var added = m.QueueBatch(scan.Owner, take, Target(), QualityKey(), (parallel.SelectedIndex + 1), autoStart.IsChecked == true);
            status.Text = (counts with { Queued = added }).Text;
            var every = hours[Math.Max(0, check?.SelectedIndex ?? 0)];
            var saved = Saved(url);
            if (saved == null && (every > 0 || mode.SelectedIndex == 1)) m.Profiles.Add(saved = new SavedProfile { Url = url });
            if (saved != null)
            {
                saved.Title = scan.Owner; saved.Folder = Target(); saved.Quality = QualityKey(); saved.Filter = Filter(); saved.CheckHours = every;
                saved.LastChecked = DateTime.Now; saved.LastResult = status.Text;
                foreach (var e in scan.Entries) if (!saved.Seen.Contains(ProfileBatch.Key(e.Url))) saved.Seen.Add(ProfileBatch.Key(e.Url));
                m.SaveProfiles();
            }
        }

        async void Read()
        {
            var url = box.Text.Trim();
            if (!new DownloadJob(url).IsValid) { status.Text = L.T("Paste a web link (http or https)."); return; }
            cancel?.Cancel();
            cancel = new System.Threading.CancellationTokenSource();
            var token = cancel.Token;
            var filter = Filter();
            busy.Visibility = Visibility.Visible;
            list.Visibility = Visibility.Collapsed;
            if (export != null) export.IsEnabled = false;
            var earlier = ProfileBatch.LoadScan(url) is { Complete: false } part ? part.Entries.Count : 0;
            status.Text = earlier > 0 ? string.Format(L.T("Continuing an interrupted scan ({0} found before)…"), earlier) : L.T("Looking for videos… (large profiles can take a few minutes)");
            var proxy = HostProfiles.Match(m.Settings.HostProfiles, url)?.Proxy ?? m.Settings.Proxy;
            try
            {
                scan = await Task.Run(() => ProfileBatch.Discover(url, proxy, filter.Max,
                    (e, n) => { if (n % 10 == 0) f.Window.Dispatcher.BeginInvoke(() => status.Text = string.Format(L.T("{0} found so far…"), n)); }, cancel: token));
                var seen = mode.SelectedIndex == 1 && Saved(url) is { } p ? new HashSet<string>(p.Seen) : null;
                var (take, counts) = ProfileBatch.Choose(scan.Entries, filter, m.AlreadyDownloaded, seen);
                if (export != null) export.IsEnabled = true;
                if (mode.SelectedIndex == 2)
                {
                    checks.Clear(); list.Items.Clear();
                    foreach (var e in take)
                    {
                        var c = new CheckBox { IsChecked = true, ToolTip = e.Url,
                            Content = e.Title + (e.Duration is double d && d > 0 ? $"   ({TimeSpan.FromSeconds(d):g})" : "") + (e.Date is { } dt ? $"   {dt:d}" : "") };
                        checks.Add((c, e)); list.Items.Add(c);
                    }
                    list.Visibility = Visibility.Visible;
                    status.Text = (scan.Owner.Length > 0 ? scan.Owner + ": " : "") + (counts with { Queued = 0 }).Text;
                    if (go != null) go.Content = L.T("Download Selected");
                }
                else if (take.Count == 0) status.Text = (scan.Owner.Length > 0 ? scan.Owner + ": " : "") + (counts with { Queued = 0 }).Text;
                else Download(take, counts);
            }
            catch (OperationCanceledException) { status.Text = L.T("Stopped. The videos found so far are kept; Find Videos continues from there."); }
            catch (Exception e) { if (!token.IsCancellationRequested) status.Text = e.Message; }
            finally { busy.Visibility = Visibility.Collapsed; }
        }

        f.Window.Closed += (_, _) => cancel?.Cancel();
        f.AddButton("Close", f.Close, isCancel: true);
        f.AddButton("Stop", () => cancel?.Cancel());
        export = f.AddButton("Export…", () =>
        {
            if (scan == null) return;
            var d = new Microsoft.Win32.SaveFileDialog { Title = "Export Links", FileName = FileNames.Sanitize(scan.Owner.Length > 0 ? scan.Owner : "videos"), Filter = "Text (*.txt)|*.txt|CSV (*.csv)|*.csv|JSON (*.json)|*.json" };
            if (d.ShowDialog(f.Window) != true) return;
            File.WriteAllText(d.FileName, ProfileBatch.Export(scan.Entries, d.FilterIndex switch { 2 => "csv", 3 => "json", _ => "txt" }));
        });
        export.IsEnabled = false;
        go = f.AddButton("Find Videos", () =>
        {
            if (mode.SelectedIndex == 2 && scan != null && checks.Count > 0)
            {
                var chosen = checks.Where(c => c.Box.IsChecked == true).Select(c => c.Entry).ToList();
                var (take, counts) = ProfileBatch.Choose(chosen, new BatchFilter { Max = ProfileBatch.Limit }, m.AlreadyDownloaded);
                Download(take, counts);
                list.Visibility = Visibility.Collapsed; checks.Clear(); go!.Content = L.T("Find Videos");
                return;
            }
            Read();
        }, isDefault: true);
        f.Show();
    }

    // ---- Media Library ----

    static Window? library;

    /// <summary>Finished videos and music with preview pictures: search, play, show in folder.</summary>
    public static void MediaLibraryWindow(Window owner, DownloadManager m)
    {
        if (library is { IsLoaded: true }) { library.Activate(); return; }
        var search = new TextBox { Width = 240, Padding = new Thickness(4, 3, 4, 3), ToolTip = "Search by name" };
        var kind = new ComboBox { ItemsSource = new[] { "Videos and music", "Videos", "Music" }, SelectedIndex = 0, Margin = new Thickness(8, 0, 0, 0), MinWidth = 140 };
        var count = new TextBlock { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 12, 12, 8) };
        top.Children.Add(new TextBlock { Text = "Search ", VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(search);
        top.Children.Add(kind);
        top.Children.Add(count);
        var list = new ListBox { BorderThickness = new Thickness(0), Margin = new Thickness(8, 0, 8, 0) };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
        var open = new Button { Content = "Play", MinWidth = 90, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        var folder = new Button { Content = "Show in Folder", MinWidth = 110, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        var close = new Button { Content = "Close", MinWidth = 90, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        bottom.Children.Add(open);
        bottom.Children.Add(folder);
        bottom.Children.Add(close);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(list);
        var w = library = new Window
        {
            Title = "Media Library", Width = 900, Height = 640, MinWidth = 520, MinHeight = 360, Content = root,
            WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner.IsVisible ? owner : null,
        };
        var version = 0;
        string? Selected() => (list.SelectedItem as FrameworkElement)?.Tag as string;
        void Play(string? path) { if (path != null && File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        void Fill()
        {
            var v = ++version;
            FileCategory? only = kind.SelectedIndex switch { 1 => FileCategory.Video, 2 => FileCategory.Music, _ => null };
            var items = MediaLibrary.Items(m.Rows.Select(r => r.Record).ToList(), search.Text.Trim(), only);
            count.Text = items.Count == 1 ? "1 file" : $"{items.Count} files";
            list.Items.Clear();
            var pictures = new List<(Image Image, string Path)>();
            foreach (var i in items)
            {
                var picture = new Image { Width = 200, Height = 112, Stretch = Stretch.UniformToFill };
                var placeholder = new TextBlock
                {
                    Text = i.Kind == FileCategory.Music ? "" : "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 40, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                var frame = new Grid { Width = 200, Height = 112, Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)) };
                frame.Children.Add(placeholder);
                frame.Children.Add(picture);
                var size = i.Record.Size is long s ? Row.Bytes(s) : "";
                var card = new StackPanel { Width = 200, Margin = new Thickness(6), Tag = i.Path, ToolTip = i.Path, Cursor = Cursors.Hand };
                card.Children.Add(new Border { Child = frame, CornerRadius = new CornerRadius(6), ClipToBounds = true });
                card.Children.Add(new TextBlock { Text = i.Record.FileName, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
                card.Children.Add(new TextBlock { Text = $"{size}   {(i.Record.Finished ?? i.Record.Added):d MMM yyyy}", Foreground = Muted, FontSize = 11 });
                card.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) Play(i.Path); };
                list.Items.Add(card);
                if (i.Kind == FileCategory.Video) pictures.Add((picture, i.Path));
            }
            // Preview pictures are made one at a time in the background and shown as they arrive.
            _ = Task.Run(() =>
            {
                foreach (var (image, path) in pictures)
                {
                    if (v != version) return;
                    if (MediaLibrary.Thumbnail(path) is not { } jpg) continue;
                    w.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.UriSource = new Uri(jpg);
                            bmp.EndInit();
                            image.Source = bmp;
                        }
                        catch { }
                    });
                }
            });
        }
        search.TextChanged += (_, _) => Fill();
        kind.SelectionChanged += (_, _) => Fill();
        open.Click += (_, _) => Play(Selected());
        folder.Click += (_, _) => { if (Selected() is { } p && File.Exists(p)) Process.Start("explorer.exe", $"/select,\"{p}\""); };
        close.Click += (_, _) => w.Close();
        w.Closed += (_, _) => { version++; library = null; };
        Fill();
        w.Show();
    }
}

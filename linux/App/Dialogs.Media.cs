using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>Profile Batch and the Media Library, the same as the Windows app's.</summary>
public static partial class Dialogs
{
    // ---- Profile Batch ----

    /// <summary>
    /// Profile Batch: one link to a profile, channel, playlist or page; every video found there that anyone can open without an
    /// account is listed (following the site's pages), filtered, and downloaded into a queue of its own in the chosen quality.
    /// </summary>
    public static async Task ProfileBatchWindow(Window owner, DownloadManager m, string link)
    {
        var f = new Form(owner, "Profile Batch", 680);
        f.Note("Paste one link to a profile, channel, playlist, gallery or page on any site the video engine supports. Nexonic finds every video " +
               "that is open to everyone (no login needed) as soon as you paste, skips the ones you already have and downloads the rest in their own queue. " +
               "Instagram and Facebook show visitors only the latest posts; for all of them, use the Nexonic extension's Download all on the profile in your signed-in browser.");
        var box = f.Text("Link", link.Length > 0 ? link : await Ui.ClipboardLink(owner));
        var quality = f.Field("Quality", new ComboBox { ItemsSource = ProfileBatch.Qualities.Select(q => L.T(q.Title)).ToList(), SelectedIndex = 0, MinWidth = 260 });
        var mode = f.Field("Download", new ComboBox { ItemsSource = new[] { L.T("All videos I don't have yet"), L.T("Only new videos (not seen on this profile before)"), L.T("Let me preview and choose") }, SelectedIndex = 0, MinWidth = 260 });
        var folder = f.Folder(m.Settings.StartFolder);
        var own = f.Check("Put each profile in a folder of its own", true);
        var parallel = f.Field("Downloads at the same time", new ComboBox { ItemsSource = new[] { "1", "2", "3", "4", "5", "6" }, SelectedIndex = 1 });
        CalendarDatePicker? from = null, to = null; TextBox? words = null, minMin = null, maxMin = null, maxCount = null; ComboBox? kind = null, check = null;
        f.Collapsed("Filters", () =>
        {
            from = f.Field("Published on or after", new CalendarDatePicker());
            to = f.Field("Published on or before", new CalendarDatePicker());
            words = f.Text("Title contains (words; !word leaves it out)", "");
            minMin = f.Text("Shortest, in minutes (empty = any)", "");
            maxMin = f.Text("Longest, in minutes (empty = any)", "");
            kind = f.Field("Type", new ComboBox { ItemsSource = new[] { L.T("Everything"), L.T("Videos (no shorts or live streams)"), L.T("Shorts and reels"), L.T("Live streams") }, SelectedIndex = 0, MinWidth = 260 });
            maxCount = f.Text($"At most this many videos (up to {ProfileBatch.Limit})", ProfileBatch.DefaultMax.ToString());
            f.Note("A site that doesn't say when a video was published or how long it is: those videos are kept.");
        });
        f.Collapsed("Check this profile again", () =>
        {
            check = f.Field("Look for new videos", new ComboBox { ItemsSource = new[] { L.T("Never (only now)"), L.T("Every hour"), L.T("Every 6 hours"), L.T("Every day"), L.T("Every week") }, SelectedIndex = 0, MinWidth = 260 });
            f.Note("Saved profiles are checked while Nexonic runs; new videos are downloaded in the same quality and folder.");
        });
        var status = f.Note("");
        var busy = new ProgressBar { IsIndeterminate = true, Height = 4, IsVisible = false, Margin = new Thickness(0, 0, 0, 6) };
        f.Body.Children.Add(busy);
        var rows = new StackPanel();
        var scroll = new ScrollViewer { Content = rows, Height = 240, IsVisible = false };
        f.Body.Children.Add(scroll);
        var checks = new List<(CheckBox Box, BatchEntry Entry)>();
        ProfileBatch.Scan? scan = null;
        CancellationTokenSource? cancel = null;
        Button? go = null, export = null, now = null, later = null;
        List<BatchEntry> pending = new();
        BatchCounts? pendingCounts = null;
        int[] hours = { 0, 1, 6, 24, 168 };

        BatchFilter Filter() => new()
        {
            From = from?.SelectedDate, To = to?.SelectedDate, Keywords = words?.Text ?? "",
            MinSeconds = int.TryParse(minMin?.Text, out var a) && a > 0 ? a * 60 : null, MaxSeconds = int.TryParse(maxMin?.Text, out var b) && b > 0 ? b * 60 : null,
            Kind = new[] { "all", "videos", "shorts", "live" }[Math.Max(0, kind?.SelectedIndex ?? 0)],
            Max = int.TryParse(maxCount?.Text, out var c) && c > 0 ? Math.Min(c, ProfileBatch.Limit) : ProfileBatch.DefaultMax,
        };
        string QualityKey() => ProfileBatch.Qualities[Math.Max(0, quality.SelectedIndex)].Key;
        string Base() => (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : m.Settings.Folder;
        string Target() => own.IsChecked == true && scan != null ? ProfileBatch.FolderFor(Base(), scan.Owner, scan.Url) : Base();
        SavedProfile? Saved(string url) => m.Profiles.FirstOrDefault(p => ProfileBatch.Key(p.Url) == ProfileBatch.Key(url));

        // Nothing starts by itself: after Find Videos the user picks Download Now or Add to Queue.
        void Offer(List<BatchEntry> take, BatchCounts counts)
        {
            pending = take; pendingCounts = counts;
            if (now != null) now.IsEnabled = take.Count > 0;
            if (later != null) later.IsEnabled = take.Count > 0;
            status.Text = (scan!.Owner.Length > 0 ? scan.Owner + ": " : "") + (counts with { Queued = 0 }).Text
                + (take.Count > 0 ? "\n" + L.T("Choose Download Now, or Add to Queue to start them later.") : "");
        }

        void Hand(bool start)
        {
            if (scan == null) return;
            var take = pending;
            var counts = pendingCounts ?? new BatchCounts(take.Count, 0, 0, 0, 0);
            if (mode.SelectedIndex == 2 && checks.Count > 0)
            {
                var chosen = checks.Where(c => c.Box.IsChecked == true).Select(c => c.Entry).ToList();
                (take, counts) = ProfileBatch.Choose(chosen, new BatchFilter { Max = ProfileBatch.Limit }, m.AlreadyDownloaded);
                scroll.IsVisible = false; checks.Clear();
            }
            if (take.Count == 0) return;
            Download(take, counts, start);
            // A search still running: what it finds later goes the same way (folder, quality, now or later).
            if (m.Listings.TryGetValue(ProfileBatch.Key(scan.Url), out var running) && running.Running)
                running.Follow = (Target(), QualityKey(), parallel.SelectedIndex + 1, start);
            pending = new(); pendingCounts = null;
            if (now != null) now.IsEnabled = false;
            if (later != null) later.IsEnabled = false;
        }

        void Download(List<BatchEntry> take, BatchCounts counts, bool start)
        {
            var url = scan!.Url;
            var added = m.QueueBatch(scan.Owner, take, Target(), QualityKey(), parallel.SelectedIndex + 1, start);
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
            var url = (box.Text ?? "").Trim();
            if (!new DownloadJob(url).IsValid) { status.Text = L.T("Paste a web link (http or https)."); return; }
            cancel?.Cancel();
            cancel = new CancellationTokenSource();
            var token = cancel.Token;
            var filter = Filter();
            busy.IsVisible = true;
            scroll.IsVisible = false;
            if (export != null) export.IsEnabled = false;
            var earlier = ProfileBatch.LoadScan(url) is { Complete: false } part ? part.Entries.Count : 0;
            status.Text = earlier > 0 ? string.Format(L.T("Continuing an interrupted scan ({0} found before)…"), earlier) : L.T("Looking for videos… (large profiles can take a few minutes)");
            var proxy = HostProfiles.Match(m.Settings.HostProfiles, url)?.Proxy ?? m.Settings.Proxy;
            try
            {
                // Opened by the extension's Download all: the gallery engine lists every photo and video with the browser's
                // sign-in for the site, a request every 1-2 seconds, in the background (the manager's StartListing); this
                // window may close meanwhile. Nothing is downloaded until Download Now or Add to Queue.
                var key = ProfileBatch.Key(url);
                var searching = L.T("Listing every photo and video with your browser's sign-in, a request every 1–2 seconds. A large profile takes a few minutes: you can close this window, Nexonic keeps going and opens Profile Batch again when it's done.");
                if (m.Listings.TryGetValue(key, out var listing) && listing.Scan is { } listed && (!listing.Running || listing.Follow == null))
                    scan = listed;  // done, or the page's own posts while the rest is found (they can be downloaded now)
                else if (listing is { Running: true }) { status.Text = searching; return; }
                else if (m.ProfileCookies.ContainsKey(key) && GalleryList.IsProfile(url))
                {
                    status.Text = searching;
                    m.StartListing(url, filter.Max);
                    return;
                }
                else
                {
                    // Instagram and Facebook show visitors only their latest posts (the video engine takes those); the note
                    // says how to get the rest (the extension's Download all in the user's signed-in browser).
                    var social = SocialPages.Find(url);
                    scan = await Task.Run(() => ProfileBatch.Discover(url, proxy, filter.Max,
                        (e, n) => { if (n % 10 == 0) Avalonia.Threading.Dispatcher.UIThread.Post(() => status.Text = string.Format(L.T("{0} found so far…"), n)); }, cancel: token));
                    if (social != null) scan.Note += (scan.Note.Length > 0 ? "\n" : "") + L.T(SocialPages.More(social.Value.Site));
                }
                var seen = mode.SelectedIndex == 1 && Saved(url) is { } p ? new HashSet<string>(p.Seen) : null;
                var (take, counts) = ProfileBatch.Choose(scan.Entries, filter, m.AlreadyDownloaded, seen);
                if (export != null) export.IsEnabled = true;
                if (mode.SelectedIndex == 2)
                {
                    checks.Clear(); rows.Children.Clear();
                    foreach (var e in take)
                    {
                        var c = new CheckBox { IsChecked = true,
                            Content = e.Title + (e.Duration is double d && d > 0 ? $"   ({TimeSpan.FromSeconds(d):g})" : "") + (e.Date is { } dt ? $"   {dt:d}" : "") };
                        ToolTip.SetTip(c, e.Url);
                        checks.Add((c, e)); rows.Children.Add(c);
                    }
                    scroll.IsVisible = true;
                }
                Offer(take, counts);
                if (scan.Note.Length > 0) status.Text += "\n" + scan.Note;  // what the site doesn't show visitors
                if (listing is { Running: true }) status.Text += "\n" + L.T("Still searching with your browser's sign-in; what it finds later follows your choice.");
            }
            catch (OperationCanceledException) { status.Text = L.T("Stopped. The videos found so far are kept; Find Videos continues from there."); }
            catch (Exception e) { if (!token.IsCancellationRequested) status.Text = e.Message; }
            finally { busy.IsVisible = false; }
        }

        // A background search of this window's profile ended: show what it found here (no second window).
        var shownKey = ProfileBatch.Key(box.Text.Trim());
        void Done(string k) { if (k == ProfileBatch.Key(box.Text.Trim())) Read(); }
        m.ProfileWindows.Add(shownKey);
        m.ListingDone += Done;
        f.Window.Closed += (_, _) => { cancel?.Cancel(); m.ListingDone -= Done; m.ProfileWindows.Remove(shownKey); };
        // Opened with a link (the extension's Download all, or a link handed over): Find Videos starts by itself.
        if (link.Length > 0) f.Window.Opened += (_, _) => Read();
        f.AddButton("Close", f.Close, isCancel: true);
        f.AddButton("Stop", () =>
        {
            cancel?.Cancel();
            if (m.Listings.TryGetValue(ProfileBatch.Key((box.Text ?? "").Trim()), out var listing) && listing.Running) listing.Stop.Cancel();
        });
        export = f.AddButton("Export…", async () =>
        {
            if (scan == null) return;
            var file = await f.Window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export Links", SuggestedFileName = FileNames.Sanitize(scan.Owner.Length > 0 ? scan.Owner : "videos") + ".txt",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Text") { Patterns = new[] { "*.txt" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
                },
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            await File.WriteAllTextAsync(path, ProfileBatch.Export(scan.Entries, ext is "csv" or "json" ? ext : "txt"));
        });
        export.IsEnabled = false;
        later = f.AddButton("Add to Queue", () => Hand(false));
        now = f.AddButton("Download Now", () => Hand(true));
        later.IsEnabled = now.IsEnabled = false;
        go = f.AddButton("Find Videos", Read, isDefault: true);
        // Pasting a whole link starts Find Videos straight away (typing one doesn't, until Enter or Find Videos).
        var lastLink = box.Text ?? "";
        box.TextChanged += (_, _) =>
        {
            var text = box.Text ?? "";
            var pasted = text.Length - lastLink.Length > 8 && new DownloadJob(text.Trim()).IsValid;
            lastLink = text;
            if (pasted) Read();
        };
        await f.Show();
    }

    // ---- Media Library ----

    static Window? library;

    /// <summary>Finished videos and music with preview pictures: search, play, show in folder.</summary>
    public static void MediaLibraryWindow(Window owner, DownloadManager m)
    {
        if (library != null) { library.Activate(); return; }
        var search = new TextBox { Width = 240, Watermark = "Search by name" };
        var kind = new ComboBox { ItemsSource = new[] { "Videos and music", "Videos", "Music" }, SelectedIndex = 0, Margin = new Thickness(8, 0, 0, 0), MinWidth = 150 };
        var count = new TextBlock { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 12, 12, 8), Children = { search, kind, count } };
        var grid = new WrapPanel { Margin = new Thickness(8, 0, 8, 0) };
        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        string? selected = null;
        Border? selectedCard = null;
        void Play(string? path) { if (path != null && File.Exists(path)) LinuxSystem.Open(path); }
        var open = Ui.Button("Play", () => Play(selected));
        var folder = Ui.Button("Show in Folder", () => { if (selected is { } p && File.Exists(p)) LinuxSystem.ShowInFolder(p); });
        var w = library = new Window
        {
            Title = "Media Library", Width = 920, Height = 660, MinWidth = 520, MinHeight = 360, Icon = Ui.WindowIcon,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        var close = Ui.Button("Close", () => w.Close(), isCancel: true);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Margin = new Thickness(12), Children = { open, folder, close } };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(scroll);
        w.Content = root;
        var version = 0;
        void Fill()
        {
            var v = ++version;
            FileCategory? only = kind.SelectedIndex switch { 1 => FileCategory.Video, 2 => FileCategory.Music, _ => null };
            var items = MediaLibrary.Items(m.Rows.Select(r => r.Record).ToList(), (search.Text ?? "").Trim(), only);
            count.Text = items.Count == 1 ? "1 file" : $"{items.Count} files";
            grid.Children.Clear();
            selected = null;
            selectedCard = null;
            var pictures = new List<(Image Image, string Path)>();
            foreach (var i in items)
            {
                var picture = new Image { Width = 200, Height = 112, Stretch = Stretch.UniformToFill };
                var frame = new Panel { Width = 200, Height = 112, Background = Ui.Shade, Children = { new Border { Child = Ui.FileBadge(i.Record.FileName, 44), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, picture } };
                var size = i.Record.Size is long s ? Ui.Bytes(s) : "";
                var info = new StackPanel
                {
                    Children =
                    {
                        new Border { Child = frame, CornerRadius = new CornerRadius(6), ClipToBounds = true },
                        new TextBlock { Text = i.Record.FileName, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) },
                        new TextBlock { Text = $"{size}   {(i.Record.Finished ?? i.Record.Added):d MMM yyyy}", Opacity = 0.7, FontSize = 11 },
                    },
                };
                var card = new Border { Width = 212, Padding = new Thickness(6), Margin = new Thickness(2), CornerRadius = new CornerRadius(8), Child = info, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
                ToolTip.SetTip(card, i.Path);
                var path = i.Path;
                card.PointerPressed += (_, e) =>
                {
                    if (selectedCard != null) selectedCard.Background = Brushes.Transparent;
                    selectedCard = card;
                    card.Background = Ui.Shade;
                    selected = path;
                    if (e.ClickCount == 2) Play(path);
                };
                grid.Children.Add(card);
                if (i.Kind == FileCategory.Video) pictures.Add((picture, path));
            }
            // Preview pictures are made one at a time in the background and shown as they arrive.
            _ = Task.Run(() =>
            {
                foreach (var (image, path) in pictures)
                {
                    if (v != version) return;
                    if (MediaLibrary.Thumbnail(path) is not { } jpg) continue;
                    Dispatcher.UIThread.Post(() => { try { image.Source = new Bitmap(jpg); } catch { } });
                }
            });
        }
        search.TextChanged += (_, _) => Fill();
        kind.SelectionChanged += (_, _) => Fill();
        w.Closed += (_, _) => { version++; library = null; };
        Fill();
        w.Show();
    }
}

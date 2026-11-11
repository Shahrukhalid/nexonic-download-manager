using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>The app's small windows, the same as the Windows app's, built in code so each one reads top to bottom.</summary>
public static partial class Dialogs
{
    // ---- Add ----

    public static async Task Add(Window owner, DownloadManager m, string links, string queueId)
    {
        var f = new Form(owner, "Add Download", 580);
        f.Note("Paste one or more links, one per line.");
        var text = f.Text("Links", links, multi: true);
        var engine = f.Field("Download engine", new ComboBox { ItemsSource = new[] { "Automatic", "File (HTTP/HTTPS)", "HLS stream", "Video site", "Torrent / magnet" }, SelectedIndex = 0 });
        var name = f.Text("File name (one link only; empty = from the link)");
        var folder = f.Folder(m.Settings.StartFolder);
        var queue = f.Queue(m, queueId);
        // One video link: looked up while the window is open, with its qualities and sizes. Several links: sound only or not.
        var qualityPanel = new StackPanel { IsVisible = false };
        qualityPanel.Children.Add(new TextBlock { Text = "Quality", Margin = new Thickness(0, 8, 0, 3) });
        var quality = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        qualityPanel.Children.Add(quality);
        var qualityNote = Ui.Muted("");
        qualityNote.Margin = new Thickness(0, 4, 0, 4);
        qualityPanel.Children.Add(qualityNote);
        f.Body.Children.Add(qualityPanel);
        var audio = f.Check("Sound only (for YouTube and other video sites)", false);
        f.Note("Video sites are saved in the best quality available, with picture and sound joined.");
        var tags = new List<int> { 0 };
        int Chosen() => quality.SelectedIndex >= 0 && quality.SelectedIndex < tags.Count ? tags[quality.SelectedIndex] : 0;
        string? OneVideo()
        {
            var ls = Links.Split(text.Text ?? "");
            return ls.Count == 1 && SiteVideo.IsSupported(ls[0]) && engine.SelectedIndex is 0 or 3 ? ls[0] : null;
        }
        VideoPrefetch? prefetch = null;
        void ShowNote()
        {
            if (prefetch == null) return;
            var size = VideoPrefetch.SizeFor(prefetch.Info, prefetch.Choices, Chosen());
            qualityNote.Text = prefetch.Busy ? "Finding the qualities on offer…"
                : prefetch.Info is { } info ? info.Title + (size is long b ? " · " + Ui.Bytes(b) : "")
                : "The qualities are found when the download starts.";
        }
        void Refresh()
        {
            var one = OneVideo() != null;
            qualityPanel.IsVisible = one;
            audio.IsVisible = !one;
            if (!one || prefetch == null) return;
            var keep = Chosen();
            var options = VideoPrefetch.Options(prefetch.Choices);
            tags = options.Select(o => o.Tag).ToList();
            quality.ItemsSource = options.Select(o => o.Label).ToList();
            quality.SelectedIndex = Math.Max(0, tags.IndexOf(keep));
            ShowNote();
        }
        var pf = prefetch = new VideoPrefetch(() => m.Settings.Proxy, () => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh));
        quality.SelectionChanged += (_, _) => ShowNote();
        text.TextChanged += (_, _) => { pf.Want(OneVideo()); Refresh(); };
        engine.SelectionChanged += (_, _) => { pf.Want(OneVideo()); Refresh(); };
        f.Window.Closed += (_, _) => pf.Dispose();
        pf.Want(OneVideo());
        Refresh();
        var inner = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        TextBox Inner(string label, bool password = false)
        {
            inner.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) });
            var t = new TextBox();
            if (password) t.PasswordChar = '•';
            inner.Children.Add(t);
            return t;
        }
        var user = Inner("User name");
        var pass = Inner("Password", password: true);
        var sum = Inner("Checksum (MD5, SHA-1, SHA-256 or SHA-512)");
        var mirrorsBox = Inner("Mirrors: more addresses of the same file, one per line (downloaded from all at once)");
        mirrorsBox.AcceptsReturn = true;
        mirrorsBox.MinHeight = 48;
        var limit = Inner("Speed limit in KB/s (0 = none)");
        f.Body.Children.Add(new Expander { Header = "Login, checksum and speed limit", Content = inner, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch });
        var error = f.Error();
        async void Go(bool start)
        {
            var input = (text.Text ?? "").Trim();
            if (engine.SelectedIndex == 4 || engine.SelectedIndex == 0 && SmartDownload.Detect(input) == DownloadEngine.Torrent)
            {
                f.Close();
                await Torrent(owner, m, input, queueId);
                return;
            }
            if ((sum.Text ?? "").Trim().Length > 0 && Checksum.Parse(sum.Text!) == null) { error.Text = "The checksum is not a valid value."; return; }
            m.RememberFolder(folder.Text);
            // Links that repeat a download you have: ask, skip or download again (under a new name), as set in Options.
            var target = (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : m.Settings.Folder;
            var repeats = CurlCommand.Looks(input) ? new List<string>() : Links.Split(input).Where(l => m.IsDuplicate(l, null, target)).ToList();
            var skipRepeats = m.Settings.DuplicateAction == "skip";
            if (repeats.Count > 0 && m.Settings.DuplicateAction == "ask")
                skipRepeats = !await Ui.Confirm(f.Window, "Already downloaded",
                    $"{repeats.Count} of these links repeat downloads you have:\n{string.Join("\n", repeats.Take(5))}\n\nDownload them again? They are saved under a new name; nothing is overwritten.",
                    "Download Again", "Skip Them");
            var n = m.AddLinks(text.Text ?? "", new AddOptions
            {
                SkipDuplicates = skipRepeats,
                Folder = (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : m.Settings.Folder,
                FileName = (name.Text ?? "").Trim(),
                QueueId = Form.QueueId(queue),
                Start = start,
                Username = (user.Text ?? "").Trim(),
                Password = pass.Text ?? "",
                Checksum = (sum.Text ?? "").Trim(),
                Mirrors = mirrorsBox.Text ?? "",
                SpeedLimitKBps = Ui.Number(limit.Text),
                AudioOnly = OneVideo() != null ? Chosen() == -1 : audio.IsChecked == true,
                MaxHeight = OneVideo() != null && Chosen() > 0 ? Chosen() : null,
                // Reuse the details looked up while the window was open (not when a login is needed to see the video).
                InfoJson = OneVideo() is { } one && (user.Text ?? "").Trim().Length == 0 ? pf.Take(one) : null,
                Engine = engine.SelectedIndex switch { 1 => DownloadEngine.File, 2 => DownloadEngine.Hls, 3 => DownloadEngine.Video, _ => DownloadEngine.Auto },
            });
            if (n == 0)
            {
                error.Text = CurlCommand.Looks(text.Text) && CurlCommand.Parse(text.Text!).Problem is { } why ? why
                    : repeats.Count > 0 && skipRepeats ? "All of these links are downloads you have already." : "Paste at least one http or https link.";
                return;
            }
            f.Close();
        }
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Download Later", () => Go(false));
        f.AddButton("Start Download", () => Go(true), isDefault: true);
        await f.Show();
    }

    // ---- Batch ----

    public static async Task Batch(Window owner, DownloadManager m, string queueId)
    {
        var f = new Form(owner, "Batch Download", 620);
        f.Note("Put a range in square brackets: [001-100] keeps three digits, [1-100] does not, [a-z] uses letters and [0-100:5] counts in fives. A list in braces, {jpg,png}, tries each. Several ranges multiply.");
        var pattern = f.Text("Pattern", "https://example.com/photos/img[001-050].jpg");
        pattern.FontFamily = new FontFamily("monospace");
        var filter = f.Text("Keep only (optional)", "");
        f.Note("Words find links that contain them; * and ? are wildcards; a leading ! leaves links out. Example: *.jpg !*thumb*");
        var links = f.Text("Links to download (you can edit them)", "", multi: true);
        links.Height = 160;
        var preview = f.Note("");
        void Update()
        {
            var n = BatchPattern.Count((pattern.Text ?? ""));
            if (n > BatchPattern.Limit) { preview.Text = $"{n} links – the limit is {BatchPattern.Limit}"; links.Text = ""; return; }
            var kept = LinkFilter.Apply(BatchPattern.Expand((pattern.Text ?? "")), (filter.Text ?? ""));
            links.Text = string.Join("\n", kept);
            preview.Text = kept.Count == n ? $"{n} link{(n == 1 ? "" : "s")}" : $"{kept.Count} of {n} links";
        }
        pattern.TextChanged += (_, _) => Update();
        filter.TextChanged += (_, _) => Update();
        Update();
        var folder = f.Folder(m.Settings.Folder);
        var queue = f.Queue(m, queueId);
        void Go(bool start)
        {
            var chosen = (links.Text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
            if (chosen.Count == 0 || chosen.Count > BatchPattern.Limit) { preview.Text = chosen.Count == 0 ? "No links to download." : $"The limit is {BatchPattern.Limit} links."; return; }
            m.AddLinks(string.Join("\n", chosen), new AddOptions
            {
                Folder = (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : m.Settings.Folder, QueueId = Form.QueueId(queue), Start = start,
            });
            f.Close();
        }
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Download Later", () => Go(false));
        f.AddButton("Start Download", () => Go(true), isDefault: true);
        await f.Show();
    }

    // ---- Scheduler ----

    static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    public static async Task Scheduler(Window owner, DownloadManager m, string selectedId)
    {
        var f = new Form(owner, "Scheduler", 740);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*") };
        var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0), Spacing = 6 };
        var list = new ListBox { Height = 340 };
        var addBtn = new Button { Content = "New Queue", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        var delBtn = new Button { Content = "Delete Queue", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        left.Children.Add(list);
        left.Children.Add(addBtn);
        left.Children.Add(delBtn);
        var editor = new StackPanel();
        Grid.SetColumn(editor, 1);
        grid.Children.Add(left);
        grid.Children.Add(editor);
        f.Body.Children.Add(grid);
        var queues = new List<DownloadQueue>();

        void Refill(DownloadQueue? select)
        {
            queues = m.Queues.ToList();
            list.ItemsSource = queues.Select(q => q.Name + (q.Running ? "  (running)" : "")).ToList();
            var i = select == null ? 0 : queues.IndexOf(select);
            list.SelectedIndex = i >= 0 ? i : 0;
        }

        void Edit(DownloadQueue q)
        {
            editor.Children.Clear();
            void Label(string t) => editor.Children.Add(new TextBlock { Text = t, Margin = new Thickness(0, 10, 0, 3) });
            Label("Name");
            var name = new TextBox { Text = q.Name, IsEnabled = !q.IsMain };
            editor.Children.Add(name);

            var start = new CheckBox { Content = "Start at (HH:mm)", IsChecked = q.StartEnabled, Margin = new Thickness(0, 10, 0, 2) };
            var startTime = new TextBox { Text = q.StartTime, Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
            var days = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            for (var d = 1; d <= 7; d++)
            {
                var day = d;
                var c = new CheckBox { Content = DayNames[d - 1], IsChecked = q.Days.Contains(d), Margin = new Thickness(0, 0, 10, 0) };
                c.Click += (_, _) =>
                {
                    var set = q.Days.ToHashSet();
                    if (c.IsChecked == true) set.Add(day); else set.Remove(day);
                    q.Days = set.OrderBy(x => x).ToList();
                    m.SaveQueues();
                };
                days.Children.Add(c);
            }
            var stop = new CheckBox { Content = "Stop at (HH:mm)", IsChecked = q.StopEnabled, Margin = new Thickness(0, 10, 0, 2) };
            var stopTime = new TextBox { Text = q.StopTime, Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
            // Waking from sleep: offered only when this computer allows the app to set its wake-up clock.
            var wakeProblem = LinuxSystem.WakeUnavailable();
            var wake = new CheckBox { Content = "Wake the computer from sleep to start", IsChecked = q.WakeToStart && wakeProblem == null, IsEnabled = wakeProblem == null, Margin = new Thickness(0, 6, 0, 0) };
            if (wakeProblem != null) ToolTip.SetTip(wake, wakeProblem);
            var wakeNote = new TextBlock { Text = wakeProblem ?? "", TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, IsVisible = wakeProblem != null };
            foreach (var e in new Control[] { start, startTime, days, wake, wakeNote, stop, stopTime }) editor.Children.Add(e);

            Label("Downloads at the same time");
            var active = new TextBox { Text = (q.IsMain ? m.Settings.MaxActive : q.MaxActive).ToString(), Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
            editor.Children.Add(active);
            Label("When the queue is done");
            var after = new ComboBox { ItemsSource = Enum.GetValues<AfterQueue>().Select(DownloadQueue.AfterTitle).ToList(), SelectedIndex = (int)q.AfterDone, MinWidth = 260 };
            editor.Children.Add(after);
            var waiting = m.Rows.Count(r => r.Record.Queue == q.Id && r.Record.Status != DownloadStatus.Finished);
            var info = Ui.Muted($"{waiting} file(s) waiting · {(q.Running ? "Running" : "Stopped")}");
            info.Margin = new Thickness(0, 10, 0, 0);
            editor.Children.Add(info);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };
            var apply = new Button { Content = "Apply" };
            var go = new Button { Content = "Start Now" };
            var halt = new Button { Content = "Stop" };
            row.Children.Add(apply);
            row.Children.Add(go);
            row.Children.Add(halt);
            editor.Children.Add(row);
            var error = new TextBlock { Foreground = Ui.Bad, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            editor.Children.Add(error);

            static bool IsTime(string? t) => TimeSpan.TryParseExact((t ?? "").Trim(), @"hh\:mm", null, out var v) && v < TimeSpan.FromDays(1);
            bool Apply()
            {
                if (!IsTime(startTime.Text) || !IsTime(stopTime.Text)) { error.Text = "Write times as HH:mm, for example 02:30."; return false; }
                error.Text = "";
                if ((name.Text ?? "").Trim().Length > 0 && !q.IsMain) q.Name = name.Text!.Trim();
                q.StartEnabled = start.IsChecked == true;
                q.WakeToStart = wake.IsChecked == true && wakeProblem == null;
                q.StartTime = startTime.Text!.Trim();
                q.StopEnabled = stop.IsChecked == true;
                q.StopTime = stopTime.Text!.Trim();
                q.AfterDone = (AfterQueue)Math.Max(0, after.SelectedIndex);
                var n = Math.Clamp(Ui.Number(active.Text, 3), 1, 10);
                if (q.IsMain)
                {
                    var s = m.Settings;
                    s.MaxActive = n;
                    m.ApplySettings(s);
                }
                else q.MaxActive = n;
                m.SaveQueues();
                Refill(q);
                return true;
            }
            apply.Click += (_, _) => Apply();
            go.Click += (_, _) => { if (Apply()) { m.StartQueue(q.Id); Refill(q); } };
            halt.Click += (_, _) => { m.StopQueue(q.Id); Refill(q); };
        }

        list.SelectionChanged += (_, _) => { if (list.SelectedIndex >= 0 && list.SelectedIndex < queues.Count) Edit(queues[list.SelectedIndex]); };
        addBtn.Click += (_, _) => Refill(m.NewQueue());
        delBtn.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= queues.Count || queues[list.SelectedIndex] is not { IsMain: false } q) return;
            if (!await Ui.Confirm(f.Window, "Delete Queue", $"Delete {q.Name}? Its downloads move to the main queue.", "Delete")) return;
            m.DeleteQueue(q);
            Refill(null);
        };
        Refill(m.Queues.FirstOrDefault(q => q.Id == selectedId));
        f.AddButton("Done", f.Close, isDefault: true, isCancel: true);
        await f.Show();
    }

    // ---- Grabber ----

    public static async Task Grabber(Window owner, DownloadManager m, string queueId)
    {
        var f = new Form(owner, "Site Grabber", 660);
        f.Note("Explores a website section and lists the files it links to. Only use it on sites you are allowed to download from.");
        var start = f.Text("Start page", await Ui.ClipboardLink(owner));
        var depth = f.Text("Follow links this many levels deep (0-5)", "1");
        var types = f.Text("File types (empty = every file)", "zip rar 7z pdf mp3 mp4 jpg png");
        var stay = f.Check("Stay inside the start page's folder", true);
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
        var explore = new Button { Content = "Explore" };
        var status = new TextBlock { Opacity = 0.7, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var all = new Button { Content = "All", Margin = new Thickness(4, 0, 0, 0) };
        var none = new Button { Content = "None", Margin = new Thickness(4, 0, 0, 0) };
        DockPanel.SetDock(explore, Dock.Left);
        DockPanel.SetDock(none, Dock.Right);
        DockPanel.SetDock(all, Dock.Right);
        bar.Children.Add(explore);
        bar.Children.Add(none);
        bar.Children.Add(all);
        bar.Children.Add(status);
        f.Body.Children.Add(bar);
        var results = new StackPanel();
        f.Body.Children.Add(new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Height = 190,
            Child = new ScrollViewer { Content = results, Padding = new Thickness(6, 2) },
        });
        var folder = f.Folder(m.Settings.Folder);
        var queue = f.Queue(m, queueId);
        CancellationTokenSource? cts = null;

        List<CheckBox> Boxes() => results.Children.OfType<CheckBox>().ToList();
        all.Click += (_, _) => Boxes().ForEach(b => b.IsChecked = true);
        none.Click += (_, _) => Boxes().ForEach(b => b.IsChecked = false);
        explore.Click += async (_, _) =>
        {
            if (cts != null) { cts.Cancel(); return; }
            var link = (start.Text ?? "").Trim();
            if (!new DownloadJob(link).IsValid) { status.Text = "Enter an http or https address."; return; }
            var o = new GrabOptions
            {
                Start = new Uri(link), Depth = Math.Clamp(Ui.Number(depth.Text, 1), 0, 5),
                Types = FileTypes.Parse(types.Text), StayInFolder = stay.IsChecked == true,
            };
            cts = new CancellationTokenSource();
            explore.Content = "Stop";
            results.Children.Clear();
            status.Text = "Exploring…";
            try
            {
                var handler = Network.Handler(m.Settings.Proxy);
                handler.AllowAutoRedirect = true;
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                IProgress<(int Pages, int Files)> report = new Progress<(int Pages, int Files)>(p => status.Text = $"{p.Pages} pages read, {p.Files} files found");
                var files = await SiteGrabber.ExploreAsync(o, http, (p, n) => report.Report((p, n)), cts.Token);
                foreach (var u in files) results.Children.Add(new CheckBox { Content = u.AbsoluteUri, IsChecked = true });
                status.Text = $"{files.Count} files found";
            }
            catch (Exception e) { status.Text = "Could not explore: " + e.Message; }
            finally
            {
                explore.Content = "Explore";
                cts = null;
            }
        };
        void Go(bool startNow)
        {
            var links = Boxes().Where(b => b.IsChecked == true).Select(b => (string)b.Content!).ToList();
            if (links.Count == 0) return;
            m.AddLinks(string.Join("\n", links), new AddOptions
            {
                Folder = (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : m.Settings.Folder, QueueId = Form.QueueId(queue), Start = startNow,
            });
            cts?.Cancel();
            f.Close();
        }
        f.AddButton("Close", () => { cts?.Cancel(); f.Close(); }, isCancel: true);
        f.AddButton("Download Later", () => Go(false));
        f.AddButton("Download Checked", () => Go(true), isDefault: true);
        f.Window.Closed += (_, _) => cts?.Cancel();
        await f.Show();
    }

    // ---- Properties ----

    public static async Task Properties(Window owner, DownloadManager m, Row row)
    {
        var r = row.Record;
        var f = new Form(owner, "Properties", 580);
        f.Note($"{r.FileName}\n{r.Url}\nFolder: {r.Folder}\nSize: {(r.Size is long s ? Row.Bytes(s) : "Unknown")}   Status: {row.Status}");
        var queue = f.Queue(m, r.Queue);
        var user = f.Text("User name", r.Username ?? "");
        var pass = f.Text("Password", r.Password ?? "", password: true);
        var sum = f.Text("Expected checksum (MD5, SHA-1, SHA-256 or SHA-512)", r.Checksum ?? "");
        var result = f.Note(r.ChecksumResult ?? "");
        if (r.Status == DownloadStatus.Finished)
        {
            var check = new Button { Content = "Check Now", HorizontalAlignment = HorizontalAlignment.Left };
            check.Click += async (_, _) =>
            {
                result.Text = "Checking…";
                result.Text = await m.VerifyAsync(row, sum.Text ?? "");
            };
            f.Body.Children.Add(check);
        }
        var limit = f.Text("Speed limit in KB/s (0 = none)", (r.SpeedLimitKBps ?? 0).ToString());
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Save", () =>
        {
            m.SetDetails(row, (user.Text ?? "").Trim(), pass.Text ?? "", Checksum.Parse(sum.Text ?? "") == null ? "" : sum.Text!.Trim(), Ui.Number(limit.Text),
                Form.QueueId(queue));
            f.Close();
        }, isDefault: true);
        await f.Show();
    }

    // ---- Refresh link ----

    public static async Task RefreshLink(Window owner, DownloadManager m, Row row)
    {
        var f = new Form(owner, "Refresh Link", 560);
        f.Note("Links from many sites stop working after a while. Paste a fresh link for the same file and the download continues from where it stopped. " +
               "Or choose Wait for Browser, then start the same download again in your browser: the extension hands the new link over automatically.");
        var link = f.Text("New link", await Ui.ClipboardLink(owner));
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Wait for Browser", () => { m.Refresh(row, null); f.Close(); });
        f.AddButton("Use This Link", () =>
        {
            var l = (link.Text ?? "").Trim();
            if (new DownloadJob(l).IsValid) { m.Refresh(row, l); f.Close(); }
        }, isDefault: true);
        await f.Show();
    }

    // ---- Options ----

    public static async Task Options(Window owner, DownloadManager m)
    {
        var s = m.Settings;
        var oldScale = s.InterfaceScale;
        var prefs = Prefs.Load();
        var f = new Form(owner, "Options", 640);
        var tabs = new TabControl { Height = 470 };
        f.Body.Children.Add(tabs);
        StackPanel Tab(string header)
        {
            var p = new StackPanel { Margin = new Thickness(4, 8, 12, 8) };
            tabs.Items.Add(new TabItem { Header = header, FontSize = 14, Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            return p;
        }
        TextBox Box(StackPanel p, string label, string value, bool password = false)
        {
            p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
            var t = new TextBox { Text = value };
            if (password) t.PasswordChar = '•';
            p.Children.Add(t);
            return t;
        }
        CheckBox Tick(StackPanel p, string label, bool value)
        {
            var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 0, 0) };
            p.Children.Add(c);
            return c;
        }
        TextBlock Hint(StackPanel p, string text)
        {
            var t = Ui.Muted(text, 12);
            t.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(t);
            return t;
        }
        ComboBox Pick(StackPanel p, string label, string[] titles, string[] values, string current)
        {
            p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 3) });
            var c = new ComboBox { ItemsSource = titles.Select(L.T).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(values, current)), MinWidth = 220 };
            AutomationProperties.SetName(c, label);
            p.Children.Add(c);
            return c;
        }
        void Heading(StackPanel p, string text) => p.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 16, 0, 0) });

        var general = Tab("General");
        general.Children.Add(new TextBlock { Text = "When a link repeats a download I have", Margin = new Thickness(0, 4, 0, 2) });
        var dupChoices = new[] { "ask", "skip", "again" };
        var dupAction = new ComboBox { ItemsSource = new[] { "Ask me", "Skip it", "Download again under a new name" }, SelectedIndex = Math.Max(0, Array.IndexOf(dupChoices, s.DuplicateAction)) };
        general.Children.Add(dupAction);
        var autoConn = Tick(general, "Choose the number of connections automatically (recommended)", s.AutoConnections);
        var autoMax = Box(general, "Most connections in automatic mode (1-256)", s.AutoMaxConnections.ToString());
        var conn = Box(general, "Connections per download when not automatic (1-256)", s.Connections.ToString());
        Hint(general, "Automatic starts with a few connections and adds more only while the download gets faster; it slows down when " +
                      "a server asks it to. Many servers limit connections, so very high numbers can make downloads slower or get them refused.");
        var active = Box(general, "Downloads at the same time (main queue)", s.MaxActive.ToString());
        general.Children.Add(new TextBlock { Text = "Save to folder", Margin = new Thickness(0, 8, 0, 3) });
        var (folderRow, folder) = Form.FolderRow(f.Window, s.Folder);
        general.Children.Add(folderRow);
        var sort = Tick(general, "Put each type in its own folder (Video, Music, Documents, Programs, Compressed, Images)", s.SortIntoCategoryFolders);
        var torrents = Tick(general, "Enable experimental torrents (built-in torrent engine)", s.TorrentsEnabled);
        var rememberFolder = Tick(general, "Start the Add window in the folder I used last time", s.RememberLastFolder);
        var notify = Tick(general, "Notifications", s.Notifications);
        var updates = Tick(general, "Check for updates when the app opens", s.CheckForUpdates);
        general.Children.Add(new TextBlock { Text = "Language", Margin = new Thickness(0, 10, 0, 3) });
        var languageChoices = new List<string> { "" }.Concat(L.Languages.Select(l => l.Code)).ToList();
        var language = new ComboBox
        {
            ItemsSource = new List<string> { L.T("Same as the system") }.Concat(L.Languages.Select(l => l.Reviewed ? l.Name : l.Name + " (machine translation)")).ToList(),
            SelectedIndex = Math.Max(0, languageChoices.IndexOf(s.Language)), MinWidth = 220,
        };
        general.Children.Add(language);
        general.Children.Add(new TextBlock { Text = "Appearance", Margin = new Thickness(0, 10, 0, 3) });
        var customJson = prefs.SchemeJson ?? "";
        string[] ThemeChoices() => new[] { "system", "light", "dark" }.Concat(ColorScheme.BuiltIn.Select(b => "scheme:" + b.Name))
            .Concat(ColorScheme.Import(customJson).Scheme != null ? new[] { "custom" } : Array.Empty<string>()).ToArray();
        static string ThemeTitle(string c) => c switch
        {
            "light" => "Light", "dark" => "Dark", "custom" => "My colour scheme", _ when c.StartsWith("scheme:") => c[7..], _ => "Same as the system",
        };
        var themeChoices = ThemeChoices();
        var theme = new ComboBox { ItemsSource = themeChoices.Select(ThemeTitle).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(themeChoices, prefs.Theme)), MinWidth = 220 };
        general.Children.Add(theme);
        var schemeNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        var schemeRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        Button SchemeButton(string text, Func<Task> click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) };
            b.Click += async (_, _) => { try { await click(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { schemeNote.Text = e.Message; } };
            schemeRow.Children.Add(b);
            return b;
        }
        SchemeButton("Import Colour Scheme…", async () =>
        {
            var files = await f.Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Colour Scheme", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Colour scheme") { Patterns = new[] { "*.json" } } },
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            var (scheme, problem) = new FileInfo(path).Length <= 64 * 1024 ? ColorScheme.Import(await File.ReadAllTextAsync(path)) : (null, "This file is too large to be a colour scheme.");
            if (scheme == null) { schemeNote.Text = problem ?? ""; return; }
            customJson = scheme.Export();
            themeChoices = ThemeChoices();
            theme.ItemsSource = themeChoices.Select(ThemeTitle).ToList();
            theme.SelectedIndex = Array.IndexOf(themeChoices, "custom");
            schemeNote.Text = $"{scheme.Name} is ready; it is used when you press OK.";
        });
        SchemeButton("Export Colour Scheme…", async () =>
        {
            var c = themeChoices[Math.Max(0, theme.SelectedIndex)];
            var chosen = c == "custom" ? ColorScheme.Import(customJson).Scheme : c.StartsWith("scheme:") ? ColorScheme.BuiltIn.FirstOrDefault(b => b.Name == c[7..]) : null;
            if (chosen == null) { schemeNote.Text = "Choose a colour scheme in the list to export it."; return; }
            var file = await f.Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export Colour Scheme", SuggestedFileName = chosen.Name + ".json" });
            if (file?.TryGetLocalPath() is not { } target) return;
            await File.WriteAllTextAsync(target, chosen.Export());
            schemeNote.Text = "Saved.";
        });
        general.Children.Add(schemeRow);
        general.Children.Add(schemeNote);
        general.Children.Add(new TextBlock { Text = "Font", Margin = new Thickness(0, 10, 0, 3) });
        var fontNames = new List<string> { "Same as the system" }.Concat(Avalonia.Media.FontManager.Current.SystemFonts.Select(x => x.Name).Distinct().OrderBy(x => x)).ToList();
        var font = new ComboBox { ItemsSource = fontNames, SelectedIndex = Math.Max(0, fontNames.IndexOf(prefs.Font ?? "")), MinWidth = 220 };
        general.Children.Add(font);
        general.Children.Add(new TextBlock { Text = "Interface size", Margin = new Thickness(0, 10, 0, 3) });
        var scales = new[] { 75, 90, 100, 110, 125, 150, 175, 200 };
        var scale = new ComboBox { ItemsSource = scales.Select(v => v + "%").ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(scales, s.InterfaceScale) is var si && si >= 0 ? si : 2), MinWidth = 220 };
        AutomationProperties.SetName(scale, "Interface size");
        general.Children.Add(scale);
        var sizeUnits = Pick(general, "Sizes", new[] { "KB, MB, GB (1 KB = 1024 bytes)", "KiB, MiB, GiB (1 KiB = 1024 bytes)", "KB, MB, GB (1 KB = 1000 bytes)" }, Units.SizeStyles, s.SizeUnits);
        var speedUnits = Pick(general, "Speeds", new[] { "Bytes per second (MB/s)", "Bits per second (Mbit/s)" }, Units.SpeedStyles, s.SpeedUnits);
        var dateStyle = Pick(general, "Dates in the list", new[] { "Date and time", "How long ago (5 min ago)" }, Units.DateStyles, s.DateStyle);
        Heading(general, "Browser downloads");
        var askDialog = Tick(general, "Show the download window when a link is clicked in the browser", s.ShowDownloadDialog);
        var progressWin = Tick(general, "Show a progress window for each download I start", s.ShowProgressWindow);
        var toFront = Tick(general, "Bring the main window to the front for browser downloads", s.BringToFrontOnBrowserDownload);
        Hint(general, "With the download window off, downloads start right away. Closing the main window keeps the app running in the tray " +
                      "(on GNOME the tray needs the AppIndicator extension); opening the app again from the menu brings the window back.");
        Heading(general, "Video tools");
        var ytdlpPath = Box(general, "Your own yt-dlp program (full path)", s.YtDlpPath ?? "");
        var ffmpegPath = Box(general, "Your own FFmpeg program (full path)", s.FFmpegPath ?? "");
        Hint(general, "Leave empty to use the ones the app downloads and keeps current. Your own yt-dlp is never updated or replaced by the app; FFmpeg needs ffprobe in the same folder.");
        var audioFormat = Pick(general, "Sound-only downloads", new[] { "Original format (no conversion)", "MP3", "Opus", "FLAC" }, AudioConversion.Formats, s.AudioFormat);
        Hint(general, "Converting needs FFmpeg. MP3 uses the best variable quality; FLAC keeps every detail of the sound the site sends.");

        var connection = Tab("Connection");
        var manualActive = Box(connection, "Downloads I resume myself: run this many at once beside the queues (0 = they wait for a queue slot)", s.MaxManualActive.ToString());
        var limit = Box(connection, "Speed limit for all downloads in KB/s (0 = none)", s.SpeedLimitKBps.ToString());
        var activeLimit = Box(connection, "While I use the computer, limit downloads to KB/s (0 = off)", s.ActiveSpeedLimitKBps.ToString());
        connection.Children.Add(new TextBlock { Text = "Speed limits by time of day (one rule per line)", Margin = new Thickness(0, 10, 0, 2) });
        var schedule = new TextBox { Text = s.SpeedScheduleText ?? "", AcceptsReturn = true, MinHeight = 48 };
        connection.Children.Add(schedule);
        Hint(connection, "days start-end KB/s, for example Mon-Fri 09:00-17:00 300 or Daily 23:00-07:00 0 (0 = no limit). The first rule that " +
                         "applies replaces the speed limit above; days can be Daily, Weekdays, Weekends, Mon-Fri or Sat,Sun.");
        Hint(connection, "Full speed again after a minute without keyboard or mouse input.");
        var capMB = Box(connection, "Download limit: at most this many MB (0 = no limit)", s.DataCapMB.ToString());
        var capHours = Box(connection, "… every this many hours", s.DataCapHours.ToString());
        Hint(connection, "When the amount is reached, downloads stop and continue by themselves when the next period starts." +
                         (m.DataCapStatus is { } used ? " Now: " + used + "." : ""));
        Heading(connection, "Debrid service (optional)");
        var debridService = new ComboBox { ItemsSource = Enum.GetValues<DebridService>().Select(d => L.T(DebridSettings.Title(d))).ToList(), SelectedIndex = (int)s.Debrid.Service, MinWidth = 220 };
        connection.Children.Add(debridService);
        var debridKey = Box(connection, "Your API key for the service", s.Debrid.Key, password: true);
        var debridHosts = Box(connection, "Sites whose links go through it (comma separated, e.g. example-host.com)", s.Debrid.Hosts);
        Hint(connection, "Off by default. Uses your own account; links from other sites are never sent to the service. The key is saved encrypted and never in backups.");
        connection.Children.Add(new TextBlock { Text = "Proxy", Margin = new Thickness(0, 12, 0, 3) });
        var mode = new ComboBox { ItemsSource = Enum.GetValues<ProxyMode>().Select(ProxySettings.Title).ToList(), SelectedIndex = (int)s.Proxy.Mode, MinWidth = 220 };
        connection.Children.Add(mode);
        var host = Box(connection, "Proxy address (for a proxy script: its web address or file)", s.Proxy.Host);
        var port = Box(connection, "Port", s.Proxy.Port.ToString());
        var puser = Box(connection, "Proxy user name (optional)", s.Proxy.Username);
        var ppass = Box(connection, "Proxy password", s.Proxy.Password, password: true);
        Hint(connection, "Proxy changes apply to downloads started afterwards. Passwords and cookies are saved encrypted with a key kept in your " +
                         "desktop's keyring (GNOME Keyring, KWallet); without a keyring they are kept only until the app closes.");
        Heading(connection, "Advanced");
        var net = s.Network;
        var minSeg = Box(connection, "Smallest part of a file in KB (64 or more)", net.MinSegmentKB.ToString());
        var retries = Box(connection, "Tries per part before a download stops", net.Retries.ToString());
        var connectTimeout = Box(connection, "Connection timeout in seconds", net.ConnectTimeoutSeconds.ToString());
        var readTimeout = Box(connection, "Reconnect a part that receives nothing for (seconds)", net.ReadTimeoutSeconds.ToString());
        var doh = Box(connection, "DNS over HTTPS server (empty = the system's DNS)", net.DnsOverHttps);
        Hint(connection, "For example " + string.Join(" or ", NetworkOptions.DohServers) + ". Not used through a proxy.");
        var dot = Box(connection, "DNS over TLS server (address#certificate name; used instead of DNS over HTTPS)", net.DnsOverTls ?? "");
        Hint(connection, "For example 1.1.1.1#cloudflare-dns.com, 9.9.9.9#dns.quad9.net or 8.8.8.8#dns.google. Not used through a proxy.");
        var strictDns = Tick(connection, "Strict: never use the system's DNS", net.StrictDns);
        Hint(connection, "With strict mode, a name that encrypted DNS cannot find is not looked up any other way, and the video engine, the " +
                         "gallery engine and torrents only run through a proxy (they look names up themselves). Give the DNS over HTTPS server " +
                         "by its address (https://1.1.1.1/dns-query).");
        connection.Children.Add(new TextBlock { Text = "Internet addresses", Margin = new Thickness(0, 10, 0, 2) });
        var ipVersions = new[] { "any", "ipv4", "ipv6" };
        var ipVersion = new ComboBox { ItemsSource = new[] { "IPv4 and IPv6 (IPv4 first)", "IPv4 only", "IPv6 only" }, SelectedIndex = Math.Max(0, Array.IndexOf(ipVersions, net.IpVersion)), MinWidth = 220 };
        AutomationProperties.SetName(ipVersion, "Internet addresses");
        connection.Children.Add(ipVersion);
        Hint(connection, "Use IPv4 only when a site or network has a broken IPv6 route. Help → Network Diagnostics checks a link step by step.");
        connection.Children.Add(new TextBlock { Text = "Network adapter", Margin = new Thickness(0, 10, 0, 2) });
        var adapterNames = Adapters.Choices(net.BindAdapter);
        var adapter = new ComboBox { ItemsSource = adapterNames.Select(n => n.Length == 0 ? L.T("Any adapter") : n).ToList(),
            SelectedIndex = Math.Max(0, adapterNames.FindIndex(n => string.Equals(n, net.BindAdapter, StringComparison.OrdinalIgnoreCase))), MinWidth = 220 };
        connection.Children.Add(adapter);
        Hint(connection, "Downloads, videos and torrents use only this adapter (for example your VPN). If it disconnects, downloads stop instead of using another connection.");
        var blocklist = Box(connection, "Torrent blocklist: a file or an https:// address (empty = none)", net.TorrentBlocklist);
        Hint(connection, "Torrents never connect to the addresses in this list. P2P, DAT and CIDR lists work, also as .gz. An address is downloaded again once a day.");
        var checkResult = Hint(connection, "");
        var check = new Button { Content = "Check the Connection", Margin = new Thickness(0, 8, 0, 0) };
        check.Click += async (_, _) =>
        {
            checkResult.Text = "Checking…";
            var keep = Network.Options;
            Network.Options = new NetworkOptions { ConnectTimeoutSeconds = Ui.Number(connectTimeout.Text, 30), DnsOverHttps = doh.Text ?? "", DnsOverTls = dot.Text ?? "", BindAdapter = adapterNames[Math.Max(0, adapter.SelectedIndex)] }.Clamped();
            try
            {
                var proxy = new ProxySettings { Mode = (ProxyMode)Math.Max(0, mode.SelectedIndex), Host = (host.Text ?? "").Trim(), Port = Ui.Number(port.Text, 8080), Username = (puser.Text ?? "").Trim(), Password = ppass.Text ?? "" };
                checkResult.Text = string.Join("\n", await NetworkCheck.RunAsync(proxy));
            }
            finally { Network.Options = keep; }
        };
        connection.Children.Add(check);

        var sites = Tab("Sites");
        Hint(sites, "Settings used for every download from a website: connections, speed limit, tries, timeout, user agent, folder, queue, login and proxy. " +
                    "A site also covers its subdomains (example.com covers www.example.com). Logins are only sent to that site and are saved encrypted.");
        var profiles = s.HostProfiles.Select(Clone).ToList();
        var siteList = new ListBox { Height = 200, Margin = new Thickness(0, 8, 0, 0) };
        void FillSites() => siteList.ItemsSource = profiles.Select(p => (p.Enabled ? "" : "(off) ") + HostProfiles.Normalize(p.Host) + Summary(p)).ToList();
        FillSites();
        sites.Children.Add(siteList);
        var siteButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void SiteButton(string text, Func<Task> click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
            b.Click += async (_, _) => await click();
            siteButtons.Children.Add(b);
        }
        SiteButton("Add Site…", async () => { if (await EditSite(f.Window, new HostProfile()) is { } p) { profiles.Add(p); FillSites(); } });
        SiteButton("Edit…", async () =>
        {
            var i = siteList.SelectedIndex;
            if (i >= 0 && await EditSite(f.Window, Clone(profiles[i])) is { } p) { profiles[i] = p; FillSites(); }
        });
        SiteButton("Remove", async () =>
        {
            var i = siteList.SelectedIndex;
            if (i < 0 || !await Ui.Confirm(f.Window, "Remove Site", $"Remove the settings for {HostProfiles.Normalize(profiles[i].Host)}?", "Remove")) return;
            profiles.RemoveAt(i);
            FillSites();
        });
        sites.Children.Add(siteButtons);
        siteList.DoubleTapped += async (_, _) =>
        {
            var i = siteList.SelectedIndex;
            if (i >= 0 && await EditSite(f.Window, Clone(profiles[i])) is { } p) { profiles[i] = p; FillSites(); }
        };

        var categories = Tab("Categories");
        Hint(categories, "Your own categories, one per line, in the order they are checked: [icon] Name: types @ sites > folder. " +
                         "The icon, the sites and the folder can be left out. Sites limit a category to files from those addresses " +
                         "(example.com includes its subdomains; * matches anything, as in github.com/*/releases); with sites and no types, " +
                         "every file from those sites goes there. The folder can be a full path or a folder inside the download folder " +
                         "(left out: the category's name). Rename or reorder a category by editing its line. Your categories come before the " +
                         "built-in types, appear in the sidebar and apply even when \"Put each type in its own folder\" is off. " +
                         "Example: 📚 Comics: cbz cbr cb7 > Comics");
        var categoryText = new TextBox { Text = CustomCategory.ToLines(s.CustomCategories), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            Height = 220, Margin = new Thickness(0, 8, 0, 0), Watermark = "Comics: cbz cbr cb7 > Comics\nE-books: epub mobi azw3 > /home/me/Books" };
        AutomationProperties.SetName(categoryText, "Your categories, one per line");
        categories.Children.Add(categoryText);
        // The same categories as a list: select one and move it up or down.
        var categoryList = new ListBox { Height = 110, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(categoryList, "Your categories in the order they are checked");
        void RefreshCategoryList()
        {
            var keep = categoryList.SelectedIndex;
            categoryList.ItemsSource = CustomCategory.Lines(categoryText.Text);
            categoryList.SelectedIndex = Math.Min(keep, categoryList.ItemCount - 1);
        }
        void MoveCategory(int from, int to)
        {
            if (from < 0) return;
            categoryText.Text = CustomCategory.Move(categoryText.Text, from, to);
            categoryList.SelectedIndex = Math.Clamp(to, 0, Math.Max(0, categoryList.ItemCount - 1));
        }
        categoryText.TextChanged += (_, _) => RefreshCategoryList();
        categories.Children.Add(categoryList);
        var categoryButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (label, act) in new (string, Action)[]
                 {
                     ("Move Up", () => MoveCategory(categoryList.SelectedIndex, categoryList.SelectedIndex - 1)),
                     ("Move Down", () => MoveCategory(categoryList.SelectedIndex, categoryList.SelectedIndex + 1)),
                     ("Remove", () => { if (categoryList.SelectedIndex >= 0) categoryText.Text = CustomCategory.Remove(categoryText.Text, categoryList.SelectedIndex); }),
                 })
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0) };
            b.Click += (_, _) => act();
            categoryButtons.Children.Add(b);
        }
        categories.Children.Add(categoryButtons);
        RefreshCategoryList();

        var remoteTab = Tab("Remote");
        var rs = s.Remote;
        var remoteOn = Tick(remoteTab, "Let my phone and other devices on this network manage downloads", rs.Enabled);
        var remotePort = Box(remoteTab, "Port", rs.Port.ToString());
        Hint(remoteTab, (m.RemoteProblem.Length > 0 ? m.RemoteProblem + " " : "") +
                        "Off by default. After saving, choose Pair a Phone and scan the code with the phone's camera: the phone opens a page that lists, " +
                        "adds, stops and resumes downloads. Only paired devices are let in. Use it on your home or office network only, not on public Wi-Fi " +
                        "(the connection inside the network is not encrypted). Your firewall may ask to allow the port.");
        var remoteButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var pair = new Button { Content = "Pair a Phone…", Margin = new Thickness(0, 0, 6, 6) };
        pair.Click += async (_, _) =>
        {
            if (m.PairingUrl() is not { } url) { await Ui.Message(f.Window, "Pair a Phone", "Turn on remote access and choose Save first, then open Options again."); return; }
            await PairQr(f.Window, url);
        };
        var unpair = new Button { Content = "Unpair All Devices", Margin = new Thickness(0, 0, 6, 6) };
        unpair.Click += async (_, _) =>
        {
            if (!await Ui.Confirm(f.Window, "Unpair All Devices", "Paired phones lose access until they scan the new code.", "Unpair")) return;
            m.ApplyRemote(newKey: true);
        };
        remoteButtons.Children.Add(pair);
        remoteButtons.Children.Add(unpair);
        remoteTab.Children.Add(remoteButtons);

        var backup = Tab("Backup");
        Hint(backup, "A backup holds your settings, site settings, queues and the downloads list. Passwords and cookies are never put in a backup.");
        var backupResult = Hint(backup, "");
        var backupButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var takeSettings = new CheckBox { Content = "When importing, also take the backup's settings", Margin = new Thickness(0, 8, 0, 0) };
        void BackupButton(string text, Func<Task> click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
            b.Click += async (_, _) => { try { await click(); } catch (Exception e) { backupResult.Text = e.Message; } };
            backupButtons.Children.Add(b);
        }
        BackupButton("Export Backup…", async () =>
        {
            var file = await f.Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Backup", SuggestedFileName = $"nexonic-backup-{DateTime.Now:yyyy-MM-dd}.json", DefaultExtension = "json",
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            await File.WriteAllTextAsync(path, m.ExportBackup(HelpWindows.Version));
            backupResult.Text = $"Saved {Path.GetFileName(path)}: {m.Rows.Count} downloads, {m.Queues.Count} queues.";
        });
        BackupButton("Import Backup…", async () =>
        {
            var files = await f.Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Backup", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Backup") { Patterns = new[] { "*.json" } } },
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            var n = m.ImportBackup(await File.ReadAllTextAsync(path), takeSettings.IsChecked == true);
            backupResult.Text = $"Added {n} download{(n == 1 ? "" : "s")}" + (takeSettings.IsChecked == true ? " and took the backup's settings. Reopen Options to see them." : ".");
            if (takeSettings.IsChecked == true) f.Close();
        });
        BackupButton("Export Links…", async () =>
        {
            var file = await f.Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Links", SuggestedFileName = "nexonic-links.txt", DefaultExtension = "txt",
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            await File.WriteAllLinesAsync(path, m.Rows.Select(r => r.Record.Url));
            backupResult.Text = $"Saved {m.Rows.Count} links to {Path.GetFileName(path)}.";
        });
        backup.Children.Add(backupButtons);
        backup.Children.Add(takeSettings);
        Heading(backup, "Missing files");
        var removeMissing = Tick(backup, "When the app starts, remove finished downloads whose file was deleted or moved", s.RemoveMissingOnStart);
        var missingNow = new Button { Content = "Remove Missing Files Now", Margin = new Thickness(0, 6, 0, 0) };
        var missingResult = Hint(backup, $"{m.Rows.Count(r => MissingFiles.IsMissing(r.Record))} finished downloads have no file now (shown as File missing).");
        missingNow.Click += (_, _) => missingResult.Text = $"Removed {m.RemoveMissing()} from the list. Files on disk are not touched.";
        backup.Children.Add(missingNow);
        Heading(backup, "Data folder");
        var dataFolder = Box(backup, "Settings, the download list and unfinished parts are kept in", AppPaths.Support);
        var dataResult = Hint(backup, DataLocation.Pending(AppPaths.Default) is { } waiting ? $"Moves to {waiting} the next time the app starts." : m.DataMoveProblem ?? "");
        var dataButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var chooseData = new Button { Content = "Choose…", Margin = new Thickness(0, 0, 6, 0) };
        chooseData.Click += async (_, _) =>
        {
            var picked = await f.Window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Data folder", AllowMultiple = false });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) dataFolder.Text = path;
        };
        var moveData = new Button { Content = "Move at Next Start", Margin = new Thickness(0, 0, 6, 0) };
        moveData.Click += (_, _) => dataResult.Text = DataLocation.Request(AppPaths.Default, (dataFolder.Text ?? "").Trim()) is { } why ? L.T(why)
            : L.T("The data moves the next time the app starts. Everything is copied and checked before the old copy is removed.");
        var usualData = new Button { Content = "Use the Usual Folder" };
        usualData.Click += (_, _) => dataFolder.Text = AppPaths.Default;
        dataButtons.Children.Add(chooseData);
        dataButtons.Children.Add(moveData);
        dataButtons.Children.Add(usualData);
        backup.Children.Add(dataButtons);

        var clip = Tab("Clipboard");
        var watch = Tick(clip, "Watch the clipboard for copied download links", s.MonitorClipboard);
        var clipTypes = Box(clip, "File types", s.ClipboardTypes);
        clipTypes.TextWrapping = TextWrapping.Wrap;
        Hint(clip, "When you copy a link that ends in one of these types, the Add window opens with it.");

        var after = Tab("After Download");
        var complete = Tick(after, "Show the Download Complete window", s.ShowCompleteDialog);
        var sound = Tick(after, "Play a sound", s.PlaySound);
        var soundDone = Box(after, "Sound when a download finishes (empty = the system's sound, none = silent, or a sound file)", s.SoundFinished ?? "");
        var soundFailed = Box(after, "Sound when a download fails (empty = silent, or a sound file)", s.SoundFailed ?? "");
        var soundQueue = Box(after, "Sound when a queue is done (empty = the system's sound, none = silent, or a sound file)", s.SoundQueueDone ?? "");
        var quiet = Box(after, "Quiet hours, no sounds (for example 22:00-07:00; empty = none)", s.QuietHours ?? "");
        var mark = Tick(after, "Record where each file came from (shown in the file's properties, like browser downloads)", s.MarkDownloads);
        var scan = Tick(after, "Scan each finished file with a virus scanner", s.ScanEnabled);
        var scanCmd = Box(after, "Scanner command (the file is added at the end)", s.ScanCommand);
        Hint(after, "The default uses ClamAV (install the clamav package). Expected checksums are checked automatically when a download finishes. " +
                    "Queues can also quit the app, sleep or shut down the computer when they are done (Scheduler).");

        var browsers = Tab("Browsers & Command Line");
        var integration = new Integration();
        var registered = Hint(browsers, "");
        void ShowRegistered()
        {
            var found = integration.HostFolders().Where(h => File.Exists(Path.Combine(h.Folder, NativeMessaging.HostName + ".json")))
                .Select(h => BrowserName(h.Folder)).Distinct().ToList();
            registered.Text = found.Count > 0
                ? "The browser helper is set up for: " + string.Join(", ", found) + "."
                : "No browser is set up yet. Open each browser once, then choose Set Up Browsers Again.";
        }
        ShowRegistered();
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Button Small(string text, Action click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => click();
            buttons.Children.Add(b);
            return b;
        }
        Small("Set Up Browsers Again", () => { integration.Install(); ShowRegistered(); });
        Small("Open the Extension Folder", () => LinuxSystem.Open(integration.ExtensionFolder));
        browsers.Children.Add(buttons);
        Hint(browsers, "Chrome, Chromium, Edge, Brave, Vivaldi, Opera: open the browser's extensions page, switch on Developer mode, choose " +
                       "Load unpacked and pick the Extension folder (Open the Extension Folder shows it).");
        Hint(browsers, "Firefox: open \"Firefox Add-on.xpi\" from the same folder with Firefox. Firefox installs add-ons that are signed; see the README.");
        Hint(browsers, "Command line: open a new terminal and type ndm help. Example: ndm https://example.com/file.zip --queue Night");
        Heading(browsers, "Automation");
        var auto = s.Automation;
        var apiOn = Tick(browsers, "Let programs on this computer control downloads (local API)", auto.Enabled);
        var apiPort = Box(browsers, "Port", auto.Port.ToString());
        var apiKey = Box(browsers, "API key (programs send it as Authorization: Bearer <key>)", auto.Key);
        apiKey.IsReadOnly = true;
        var keyButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var newKey = new Button { Content = "New Key", Margin = new Thickness(0, 0, 6, 0) };
        newKey.Click += (_, _) => apiKey.Text = AutomationSettings.NewKey();
        var copyKey = new Button { Content = "Copy Key" };
        copyKey.Click += (_, _) => { if (!string.IsNullOrEmpty(apiKey.Text)) _ = f.Window.Clipboard?.SetTextAsync(apiKey.Text); };
        keyButtons.Children.Add(newKey);
        keyButtons.Children.Add(copyKey);
        browsers.Children.Add(keyButtons);
        Hint(browsers, (m.ApiProblem.Length > 0 ? m.ApiProblem + " " : "") +
                       "Only this computer can connect (127.0.0.1), and only with the key. Add, pause, resume and remove downloads and start or stop queues; see docs/API.md. The key is saved encrypted and never in backups.");
        Heading(browsers, "Remove from this computer");
        Hint(browsers, "Removes the browser helper, the ndm command and the menu entry this app added for your user. Your downloads and settings stay.");
        var remove = new Button { Content = "Remove Desktop Integration…", Margin = new Thickness(0, 8, 0, 0) };
        remove.Click += async (_, _) =>
        {
            if (!await Ui.Confirm(f.Window, "Remove Desktop Integration",
                    "The browser extension stops handing downloads to the app and the ndm command is removed. They come back the next time the app starts.", "Remove")) return;
            integration.Remove();
            ShowRegistered();
        };
        browsers.Children.Add(remove);

        var error = f.Error();
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Save", () =>
        {
            var p = Ui.Number(port.Text, -1);
            if (p is < 1 or > 65535) { error.Text = "The proxy port must be between 1 and 65535."; return; }
            if ((ProxyMode)Math.Max(0, mode.SelectedIndex) == ProxyMode.Pac && !Pac.AddressOk(host.Text))
            { error.Text = "Enter the proxy script's web address (http:// or https://) or the full path of the file."; return; }
            s.Connections = Math.Clamp(Ui.Number(conn.Text, 8), 1, Limits.MaxConnections);
            s.AutoConnections = autoConn.IsChecked == true;
            s.DuplicateAction = dupChoices[Math.Max(0, dupAction.SelectedIndex)];
            s.AutoMaxConnections = Math.Clamp(Ui.Number(autoMax.Text, 32), 1, Limits.MaxConnections);
            s.MaxActive = Math.Clamp(Ui.Number(active.Text, 3), 1, 10);
            s.MaxManualActive = Math.Clamp(Ui.Number(manualActive.Text, 0), 0, 10);
            s.SizeUnits = Units.SizeStyles[Math.Max(0, sizeUnits.SelectedIndex)];
            s.SpeedUnits = Units.SpeedStyles[Math.Max(0, speedUnits.SelectedIndex)];
            s.DateStyle = Units.DateStyles[Math.Max(0, dateStyle.SelectedIndex)];
            s.AudioFormat = AudioConversion.Formats[Math.Max(0, audioFormat.SelectedIndex)];
            s.RemoveMissingOnStart = removeMissing.IsChecked == true;
            s.Folder = (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : s.Folder;
            s.SortIntoCategoryFolders = sort.IsChecked == true;
            s.TorrentsEnabled = torrents.IsChecked == true;
            s.Notifications = notify.IsChecked == true;
            s.CheckForUpdates = updates.IsChecked == true;
            s.Language = languageChoices[Math.Max(0, language.SelectedIndex)];
            if (L.Resolve(s.Language) != L.Code)
            {
                L.Use(s.Language);
                Avalonia.Threading.Dispatcher.UIThread.Post(Translate.Refresh);
            }
            s.ShowDownloadDialog = askDialog.IsChecked == true;
            s.ShowProgressWindow = progressWin.IsChecked == true;
            s.BringToFrontOnBrowserDownload = toFront.IsChecked == true;
            s.SpeedLimitKBps = Ui.Number(limit.Text);
            s.ActiveSpeedLimitKBps = Ui.Number(activeLimit.Text);
            if (SpeedSchedule.Parse(schedule.Text).Problem is { } scheduleProblem) { error.Text = scheduleProblem; return; }
            s.SpeedScheduleText = schedule.Text ?? "";
            s.DataCapMB = Math.Max(0, Ui.Number(capMB.Text));
            s.DataCapHours = Math.Clamp(Ui.Number(capHours.Text, 24), 1, 24 * 31);
            s.RememberLastFolder = rememberFolder.IsChecked == true;
            if (!s.RememberLastFolder) s.LastFolder = "";
            s.Debrid = new DebridSettings { Service = (DebridService)Math.Max(0, debridService.SelectedIndex), Key = (debridKey.Text ?? "").Trim(), Hosts = (debridHosts.Text ?? "").Trim() };
            s.Proxy = new ProxySettings
            {
                Mode = (ProxyMode)Math.Max(0, mode.SelectedIndex), Host = (host.Text ?? "").Trim(), Port = p,
                Username = (puser.Text ?? "").Trim(), Password = ppass.Text ?? "",
            };
            s.MonitorClipboard = watch.IsChecked == true;
            s.ClipboardTypes = clipTypes.Text ?? "";
            s.ShowCompleteDialog = complete.IsChecked == true;
            s.PlaySound = sound.IsChecked == true;
            var failedText = (soundFailed.Text ?? "").Trim();
            if ((Sounds.Problem(soundDone.Text, quiet.Text) ?? Sounds.Problem(failedText, null) ?? Sounds.Problem(soundQueue.Text, null)) is { } soundProblem) { error.Text = soundProblem; return; }
            s.SoundFinished = (soundDone.Text ?? "").Trim();
            s.SoundFailed = failedText;
            s.SoundQueueDone = (soundQueue.Text ?? "").Trim();
            s.QuietHours = (quiet.Text ?? "").Trim();
            s.MarkDownloads = mark.IsChecked == true;
            s.ScanEnabled = scan.IsChecked == true;
            s.ScanCommand = (scanCmd.Text ?? "").Trim();
            if (NetworkOptions.DnsProblem(doh.Text, dot.Text, strictDns.IsChecked == true) is { } dnsProblem) { error.Text = dnsProblem; return; }
            if (Adapters.BlocklistProblem(blocklist.Text) is { } blockProblem) { error.Text = blockProblem; return; }
            if ((EnginePaths.Problem(ytdlpPath.Text, "yt-dlp") ?? EnginePaths.Problem(ffmpegPath.Text, "FFmpeg")) is { } toolProblem) { error.Text = toolProblem; return; }
            var (customList, categoryProblem) = CustomCategory.ParseLines(categoryText.Text);
            if (categoryProblem != null) { error.Text = categoryProblem; tabs.SelectedIndex = 3; return; }
            var remotePortNumber = Ui.Number(remotePort.Text, -1);
            if (remoteOn.IsChecked == true && remotePortNumber is < 1024 or > 65535) { error.Text = "The remote port must be between 1024 and 65535."; return; }
            s.CustomCategories = customList;
            s.YtDlpPath = (ytdlpPath.Text ?? "").Trim();
            s.FFmpegPath = (ffmpegPath.Text ?? "").Trim();
            s.InterfaceScale = scales[Math.Max(0, scale.SelectedIndex)];
            s.Remote = new RemoteSettings
            {
                Enabled = remoteOn.IsChecked == true, Port = remotePortNumber is >= 1024 and <= 65535 ? remotePortNumber : 47813, Key = s.Remote.Key,
            };
            s.Network = new NetworkOptions
            {
                MinSegmentKB = Ui.Number(minSeg.Text, 512), Retries = Ui.Number(retries.Text, 6),
                ConnectTimeoutSeconds = Ui.Number(connectTimeout.Text, 30), ReadTimeoutSeconds = Ui.Number(readTimeout.Text, 60), DnsOverHttps = doh.Text ?? "", DnsOverTls = dot.Text ?? "", StrictDns = strictDns.IsChecked == true,
                BindAdapter = adapterNames[Math.Max(0, adapter.SelectedIndex)], TorrentBlocklist = (blocklist.Text ?? ""),
                IpVersion = ipVersions[Math.Max(0, ipVersion.SelectedIndex)],
            }.Clamped();
            s.HostProfiles = profiles;
            var apiPortNumber = Ui.Number(apiPort.Text, -1);
            if (apiOn.IsChecked == true && apiPortNumber is < 1024 or > 65535) { error.Text = "The automation port must be between 1024 and 65535."; return; }
            var keyText = apiKey.Text ?? "";
            s.Automation = new AutomationSettings
            {
                Enabled = apiOn.IsChecked == true, Port = apiPortNumber is >= 1024 and <= 65535 ? apiPortNumber : 47812,
                Key = keyText.Length >= 16 ? keyText : apiOn.IsChecked == true ? AutomationSettings.NewKey() : "",
            };
            m.ApplySettings(s);
            m.ApplyAutomation();
            var scaleChanged = s.InterfaceScale != oldScale;
            m.ApplyRemote();
            prefs.Theme = themeChoices[Math.Max(0, theme.SelectedIndex)];
            prefs.SchemeJson = customJson;
            prefs.Font = font.SelectedIndex > 0 ? fontNames[font.SelectedIndex] : "";
            prefs.Save();
            App.ApplyTheme(prefs);
            f.Close();
            if (scaleChanged) _ = Ui.Message(owner, "Interface Size", "Quit the app (Tasks → Quit) and open it again to use the new interface size.");
        }, isDefault: true);
        await f.Show();
    }

    static HostProfile Clone(HostProfile p) => System.Text.Json.JsonSerializer.Deserialize<HostProfile>(System.Text.Json.JsonSerializer.Serialize(p))!;

    static string Summary(HostProfile p)
    {
        var parts = new List<string>();
        if (p.MaxConnections is int c) parts.Add($"{c} connections");
        if (p.SpeedLimitKBps is int l and > 0) parts.Add($"{l} KB/s");
        if (!string.IsNullOrEmpty(p.Folder)) parts.Add("own folder");
        if (!string.IsNullOrEmpty(p.Username)) parts.Add("login");
        if (p.Proxy != null) parts.Add("own proxy");
        return parts.Count > 0 ? "   ·   " + string.Join(", ", parts) : "";
    }

    static int? Optional(string? text, int min, int max) =>
        int.TryParse((text ?? "").Trim(), out var n) ? Math.Clamp(n, min, max) : null;

    /// <summary>Edits one site's settings; null when cancelled.</summary>
    static async Task<HostProfile?> EditSite(Window owner, HostProfile p)
    {
        HostProfile? result = null;
        var f = new Form(owner, "Site Settings", 560);
        f.Note("Leave a field empty to use the general setting.");
        var host = f.Text("Website (for example example.com)", p.Host);
        var enabled = f.Check("Use these settings", p.Enabled);
        var conns = f.Text("Connections per download (1-256)", p.MaxConnections?.ToString() ?? "");
        var limit = f.Text("Speed limit in KB/s (0 = none)", p.SpeedLimitKBps?.ToString() ?? "");
        var tries = f.Text("Tries per part (1-50)", p.Retries?.ToString() ?? "");
        var timeout = f.Text("Reconnect after this many silent seconds", p.TimeoutSeconds?.ToString() ?? "");
        var ua = f.Text("User agent (how the app introduces itself to the site)", p.UserAgent ?? "");
        var (folderRow, folder) = Form.FolderRow(f.Window, p.Folder ?? "");
        f.Field("Save to (empty = the usual folder)", folderRow);
        var queue = f.Text("Queue (name; empty = the one chosen when adding)", p.QueueName ?? "");
        var later = f.Check("Add downloads from this site stopped (start them yourself or with a queue)", p.StartAtOnce == false);
        var user = f.Text("User name", p.Username ?? "");
        var pass = f.Text("Password", p.Password ?? "", password: true);
        var ownProxy = f.Check("Use its own proxy", p.Proxy != null);
        var proxyMode = new ComboBox { ItemsSource = Enum.GetValues<ProxyMode>().Select(ProxySettings.Title).ToList(), SelectedIndex = (int)(p.Proxy?.Mode ?? ProxyMode.None), MinWidth = 220 };
        f.Field("Proxy", proxyMode);
        var proxyHost = f.Text("Proxy address", p.Proxy?.Host ?? "");
        var proxyPort = f.Text("Port", (p.Proxy?.Port ?? 8080).ToString());
        var error = f.Error();
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("OK", () =>
        {
            var h = HostProfiles.Normalize(host.Text);
            if (h.Length == 0 || Uri.CheckHostName(h) == UriHostNameType.Unknown) { error.Text = "Enter the website's address, for example example.com."; return; }
            result = new HostProfile
            {
                Host = h, Enabled = enabled.IsChecked == true,
                MaxConnections = Optional(conns.Text, 1, Limits.MaxConnections), SpeedLimitKBps = Optional(limit.Text, 0, 10_000_000),
                Retries = Optional(tries.Text, 1, 50), TimeoutSeconds = Optional(timeout.Text, 5, 3600),
                UserAgent = string.IsNullOrWhiteSpace(ua.Text) ? null : ua.Text.Trim(),
                Folder = string.IsNullOrWhiteSpace(folder.Text) ? null : folder.Text.Trim(),
                QueueName = string.IsNullOrWhiteSpace(queue.Text) ? null : queue.Text.Trim(),
                StartAtOnce = later.IsChecked == true ? false : null,
                Username = string.IsNullOrWhiteSpace(user.Text) ? null : user.Text.Trim(),
                Password = string.IsNullOrEmpty(pass.Text) ? null : pass.Text,
                Proxy = ownProxy.IsChecked == true
                    ? new ProxySettings { Mode = (ProxyMode)Math.Max(0, proxyMode.SelectedIndex), Host = (proxyHost.Text ?? "").Trim(), Port = Ui.Number(proxyPort.Text, 8080), Password = p.Proxy?.Password ?? "", Username = p.Proxy?.Username ?? "" }
                    : null,
            };
            f.Close();
        }, isDefault: true);
        await f.Show();
        return result;
    }

    static string BrowserName(string folder) => folder switch
    {
        _ when folder.Contains("google-chrome") => "Chrome",
        _ when folder.Contains("chromium") => "Chromium",
        _ when folder.Contains("microsoft-edge") => "Edge",
        _ when folder.Contains("BraveSoftware") => "Brave",
        _ when folder.Contains("vivaldi") => "Vivaldi",
        _ when folder.Contains("opera") => "Opera",
        _ => "Firefox",
    };

    // ---- QR code ----

    /// <summary>The downloads as a QR code: the phone's camera opens them in Nexonic on Android or iPhone.</summary>
    public static async Task ShowQr(Window owner, List<string> urls)
    {
        // As many codes as the links need: Previous and Next go through them.
        var codes = ShareLinks.Codes(urls);
        if (codes.Count == 0) return;
        var f = new Form(owner, "Download on Your Phone", 400);
        var holder = new ContentControl { Width = 300, Height = 300, Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        f.Body.Children.Add(holder);
        var caption = f.Note("");
        var index = 0;
        void Show()
        {
            var (link, count) = codes[index];
            holder.Content = new QrView(QrCode.Encode(link)) { Width = 300, Height = 300 };
            caption.Text = (codes.Count > 1 ? $"Code {index + 1} of {codes.Count}: " : "") +
                (count == 1 ? "scan with your phone's camera to download this file with the Nexonic app on your phone."
                            : $"scan with your phone's camera to download these {count} files with the Nexonic app on your phone.");
        }
        var total = codes.Sum(c => c.Count);
        if (total < urls.Count) f.Note($"{urls.Count - total} links are too long for a code and were left out.");
        if (urls.Any(ShareLinks.LooksPrivate))
            f.Note("A link seems to include a private access key (for example a token or signature). Anyone who scans the code can use it until it expires, so share it only with your own phone.");
        f.Note("Only the links are shared: no cookies or passwords. Downloads that need you to be signed in may not work on the phone.");
        if (codes.Count > 1)
        {
            f.AddButton("Previous", () => { index = (index + codes.Count - 1) % codes.Count; Show(); });
            f.AddButton("Next", () => { index = (index + 1) % codes.Count; Show(); });
        }
        f.AddButton("Close", f.Close, isDefault: true, isCancel: true);
        Show();
        await f.Show();
    }

    /// <summary>The pairing code for remote access: the phone's camera opens the remote page already paired.</summary>
    public static async Task PairQr(Window owner, string url)
    {
        var f = new Form(owner, "Pair a Phone", 420);
        var view = new QrView(QrCode.Encode(url)) { Width = 300, Height = 300, Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(view, "Pairing QR code");
        f.Body.Children.Add(view);
        f.Note("Scan with your phone's camera while the phone is on the same network as this computer. The page it opens stays paired; add it to your home screen to open it again.");
        f.Note("Anyone with this code can manage your downloads until you choose Unpair All Devices, so show it only to your own devices.");
        var addresses = RemoteAccess.LanAddresses();
        if (addresses.Count > 1) f.Note("Other addresses of this computer: " + string.Join(", ", addresses.Skip(1)));
        f.AddButton("Close", f.Close, isDefault: true, isCancel: true);
        await f.Show();
    }

    /// <summary>Draws a QR code on white, with a quiet zone, at whole-pixel module sizes.</summary>
    sealed class QrView(QrCode qr) : Control
    {
        public override void Render(DrawingContext dc)
        {
            var size = Math.Min(Bounds.Width, Bounds.Height);
            dc.FillRectangle(Brushes.White, new Rect(0, 0, size, size));
            var module = Math.Floor(size / (qr.Size + 8));
            var offset = Math.Floor((size - module * qr.Size) / 2);
            for (var y = 0; y < qr.Size; y++)
                for (var x = 0; x < qr.Size; x++)
                    if (qr[x, y]) dc.FillRectangle(Brushes.Black, new Rect(offset + x * module, offset + y * module, module, module));
        }
    }

    // ---- Download complete ----

    public static void Complete(DownloadManager m, DownloadRecord r)
    {
        var w = new Window
        {
            Title = "Download Complete", Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, Icon = Ui.WindowIcon,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ShowActivated = true,
        };
        var p = new StackPanel { Margin = new Thickness(20) };
        var head = new DockPanel();
        var badge = Ui.FileBadge(r.FileName, 40);
        badge.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = r.FileName, FontWeight = FontWeight.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(Ui.Muted($"{(r.Size is long s ? Row.Bytes(s) : "")} saved to {r.SavedPath}"));
        head.Children.Add(texts);
        p.Children.Add(head);
        if (r.ChecksumResult is { } c)
            p.Children.Add(new TextBlock { Text = c, Foreground = c.Contains("not") ? Ui.Bad : Ui.Good, Margin = new Thickness(0, 6, 0, 0) });
        var never = new CheckBox { Content = "Don't show this again", Margin = new Thickness(0, 12, 0, 0) };
        p.Children.Add(never);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        void Close()
        {
            if (never.IsChecked == true)
            {
                var st = m.Settings;
                st.ShowCompleteDialog = false;
                m.ApplySettings(st);
            }
            w.Close();
        }
        buttons.Children.Add(Ui.Button("Close", Close, isCancel: true));
        buttons.Children.Add(Ui.Button("Open Folder", () => { if (r.SavedPath is { } path) LinuxSystem.ShowInFolder(path); Close(); }));
        buttons.Children.Add(Ui.Button("Open", () => { if (r.SavedPath is { } path) LinuxSystem.Open(path); Close(); }, isDefault: true));
        p.Children.Add(buttons);
        w.Content = p;
        w.Show();
    }
}

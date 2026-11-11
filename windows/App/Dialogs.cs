using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>The app's small windows, built in code so each one reads top to bottom.</summary>
public static partial class Dialogs
{
    static readonly Brush Muted = Brushes.Gray;

    public static string ClipboardLink()
    {
        try
        {
            var t = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            return new DownloadJob(t).IsValid ? t : "";
        }
        catch { return ""; }
    }

    // ---- building blocks ----

    sealed class Form
    {
        public readonly Window Window;
        public readonly StackPanel Body = new() { Margin = new Thickness(16) };
        readonly WrapPanel buttons = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        /// <summary>Where Note, Field and Check add their controls: the window's body, or a section made by <see cref="Collapsed"/>.</summary>
        public Panel Target;

        public Form(Window owner, string title, double width = 520)
        {
            Target = Body;
            Body.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            var outer = new StackPanel();
            outer.Children.Add(Body);
            Window = new Window
            {
                Title = title, Owner = owner, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Content = new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 760 },
            };
            Body.Loaded += (_, _) => { if (!Body.Children.Contains(buttons)) Body.Children.Add(buttons); };
        }

        public TextBlock Note(string text)
        {
            var t = new TextBlock { Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            Target.Children.Add(t);
            return t;
        }

        public T Field<T>(string label, T control) where T : FrameworkElement
        {
            Target.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
            control.Margin = new Thickness(0, 0, 0, 4);
            Target.Children.Add(control);
            return control;
        }

        public TextBox Text(string label, string value = "", bool multi = false)
        {
            var box = new TextBox { Text = value, Padding = new Thickness(4, 3, 4, 3) };
            if (multi)
            {
                box.AcceptsReturn = true;
                box.Height = 110;
                box.TextWrapping = TextWrapping.NoWrap;
                box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                box.FontFamily = new FontFamily("Consolas");
            }
            return Field(label, box);
        }

        public CheckBox Check(string label, bool value)
        {
            var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 0, 2) };
            Target.Children.Add(c);
            return c;
        }

        /// <summary>A section that starts closed; the controls <paramref name="build"/> adds go inside it.</summary>
        public Expander Collapsed(string header, Action build)
        {
            var inner = new StackPanel { Margin = new Thickness(18, 4, 0, 0) };
            var section = new Expander { Header = header, Content = inner, IsExpanded = false, Margin = new Thickness(0, 8, 0, 4) };
            Target.Children.Add(section);
            var outer = Target; Target = inner;
            try { build(); } finally { Target = outer; }
            return section;
        }

        public ComboBox Queue(DownloadManager m, string selected)
        {
            var box = new ComboBox { DisplayMemberPath = "Name", ItemsSource = m.Queues.ToList() };
            box.SelectedItem = m.Queues.FirstOrDefault(q => q.Id == selected) ?? m.Queues.First();
            return Field("Queue", box);
        }

        public TextBox Folder(string value)
        {
            var box = new TextBox { Text = value, Padding = new Thickness(4, 3, 4, 3) };
            var pick = new Button { Content = "Choose…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            pick.Click += (_, _) =>
            {
                var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Directory.Exists(box.Text) ? box.Text : "" };
                if (d.ShowDialog(Window) == true) box.Text = d.FolderName;
            };
            var row = new DockPanel();
            DockPanel.SetDock(pick, Dock.Right);
            row.Children.Add(pick);
            row.Children.Add(box);
            Field("Save to", row);
            return box;
        }

        public Button AddButton(string text, Action click, bool isDefault = false, bool isCancel = false)
        {
            var b = new Button { Content = text, MinWidth = 90, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
            b.Click += (_, _) => click();
            buttons.Children.Add(b);
            return b;
        }

        public void Show() => Window.ShowDialog();
        public void Close() => Window.Close();
    }

    static int Number(string text, int fallback = 0) => int.TryParse(text.Trim(), out var n) && n >= 0 ? n : fallback;

    // ---- Add ----

    public static void Add(Window owner, DownloadManager m, string links, string queueId)
    {
        var f = new Form(owner, "Add Download", 560);
        f.Note("Paste one or more links, one per line.");
        var text = f.Text("Links", links, multi: true);
        var name = f.Text("File name (one link only; empty = from the link)");
        var folder = f.Folder(m.Settings.StartFolder);
        var queue = f.Queue(m, queueId);
        // One video link: looked up while the window is open, with its qualities and sizes. Several links: sound only or not.
        var qualityPanel = new StackPanel { Visibility = Visibility.Collapsed };
        qualityPanel.Children.Add(new TextBlock { Text = "Quality", Margin = new Thickness(0, 6, 0, 2) });
        var quality = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
        qualityPanel.Children.Add(quality);
        var qualityNote = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        qualityPanel.Children.Add(qualityNote);
        f.Body.Children.Add(qualityPanel);
        var audio = f.Check("Sound only (for YouTube and other video sites)", false);
        f.Note("Video sites are saved in the best quality available, with picture and sound joined.");
        var tags = new List<int> { 0 };
        int Chosen() => quality.SelectedIndex >= 0 && quality.SelectedIndex < tags.Count ? tags[quality.SelectedIndex] : 0;
        string? OneVideo()
        {
            var ls = Links.Split(text.Text);
            return ls.Count == 1 && SiteVideo.IsSupported(ls[0]) ? ls[0] : null;
        }
        VideoPrefetch? prefetch = null;
        void Refresh()
        {
            var one = OneVideo() != null;
            qualityPanel.Visibility = one ? Visibility.Visible : Visibility.Collapsed;
            audio.Visibility = one ? Visibility.Collapsed : Visibility.Visible;
            if (!one || prefetch == null) return;
            var keep = Chosen();
            var options = VideoPrefetch.Options(prefetch.Choices);
            tags = options.Select(o => o.Tag).ToList();
            quality.ItemsSource = options.Select(o => o.Label).ToList();
            quality.SelectedIndex = Math.Max(0, tags.IndexOf(keep));
            var size = VideoPrefetch.SizeFor(prefetch.Info, prefetch.Choices, Chosen());
            qualityNote.Text = prefetch.Busy ? "Finding the qualities on offer…"
                : prefetch.Info is { } info ? info.Title + (size is long b ? " · " + Row.Bytes(b) : "")
                : "The qualities are found when the download starts.";
        }
        var pf = prefetch = new VideoPrefetch(() => m.Settings.Proxy, () => f.Window.Dispatcher.BeginInvoke(new Action(Refresh)));
        quality.SelectionChanged += (_, _) =>
        {
            var size = pf.Info == null ? null : VideoPrefetch.SizeFor(pf.Info, pf.Choices, Chosen());
            if (pf.Info is { } info) qualityNote.Text = info.Title + (size is long b ? " · " + Row.Bytes(b) : "");
        };
        text.TextChanged += (_, _) => { pf.Want(OneVideo()); Refresh(); };
        f.Window.Closed += (_, _) => pf.Dispose();
        pf.Want(OneVideo());
        Refresh();
        var more = new Expander { Header = "Login, checksum and speed limit", Margin = new Thickness(0, 8, 0, 0) };
        var inner = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        TextBox Inner(string label)
        {
            inner.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
            var t = new TextBox { Padding = new Thickness(4, 3, 4, 3) };
            inner.Children.Add(t);
            return t;
        }
        var user = Inner("User name");
        inner.Children.Add(new TextBlock { Text = "Password", Margin = new Thickness(0, 6, 0, 2) });
        var pass = new PasswordBox { Padding = new Thickness(4, 3, 4, 3) };
        inner.Children.Add(pass);
        var sum = Inner("Checksum (MD5, SHA-1, SHA-256 or SHA-512)");
        var mirrorsBox = Inner("Mirrors: more addresses of the same file, one per line (downloaded from all at once)");
        mirrorsBox.AcceptsReturn = true;
        mirrorsBox.MinHeight = 48;
        var limit = Inner("Speed limit in KB/s (0 = none)");
        more.Content = inner;
        f.Body.Children.Add(more);
        var error = f.Note("");
        error.Foreground = Brushes.Firebrick;
        void Go(bool start)
        {
            if (sum.Text.Trim().Length > 0 && Checksum.Parse(sum.Text) == null) { error.Text = "The checksum is not a valid value."; return; }
            m.RememberFolder(folder.Text);
            // Links that repeat a download you have: ask, skip or download again (under a new name), as set in Options.
            var target = folder.Text.Trim().Length > 0 ? folder.Text.Trim() : m.Settings.Folder;
            var repeats = CurlCommand.Looks(text.Text) ? new List<string>() : Links.Split(text.Text).Where(l => m.IsDuplicate(l, null, target)).ToList();
            var skipRepeats = m.Settings.DuplicateAction == "skip";
            if (repeats.Count > 0 && m.Settings.DuplicateAction == "ask")
            {
                var answer = MessageBox.Show(f.Window, $"{repeats.Count} of these links repeat downloads you have:\n{string.Join("\n", repeats.Take(5))}" +
                    "\n\nYes: download them again (saved under a new name; nothing is overwritten).\nNo: skip them.", "Already downloaded",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) return;
                skipRepeats = answer == MessageBoxResult.No;
            }
            var n = m.AddLinks(text.Text, new AddOptions
            {
                SkipDuplicates = skipRepeats,
                Folder = folder.Text.Trim().Length > 0 ? folder.Text.Trim() : m.Settings.Folder,
                FileName = name.Text.Trim(),
                QueueId = (queue.SelectedItem as DownloadQueue)?.Id ?? DownloadQueue.MainId,
                Start = start,
                Username = user.Text.Trim(),
                Password = pass.Password,
                Checksum = sum.Text.Trim(),
                Mirrors = mirrorsBox.Text,
                SpeedLimitKBps = Number(limit.Text),
                AudioOnly = OneVideo() != null ? Chosen() == -1 : audio.IsChecked == true,
                MaxHeight = OneVideo() != null && Chosen() > 0 ? Chosen() : null,
                // Reuse the details looked up while the window was open (not when a login is needed to see the video).
                InfoJson = OneVideo() is { } one && user.Text.Trim().Length == 0 ? prefetch!.Take(one) : null,
            });
            if (n == 0)
            {
                error.Text = CurlCommand.Looks(text.Text) && CurlCommand.Parse(text.Text).Problem is { } why ? why
                    : repeats.Count > 0 && skipRepeats ? "All of these links are downloads you have already." : "Paste at least one http or https link.";
                return;
            }
            f.Close();
        }
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Download Later", () => Go(false));
        f.AddButton("Start Download", () => Go(true), isDefault: true);
        f.Show();
    }

    // ---- Batch ----

    public static void Batch(Window owner, DownloadManager m, string queueId)
    {
        var f = new Form(owner, "Batch Download", 620);
        f.Note("Put a range in square brackets: [001-100] keeps three digits, [1-100] does not, [a-z] uses letters and [0-100:5] counts in fives. A list in braces, {jpg,png}, tries each. Several ranges multiply.");
        var pattern = f.Text("Pattern", "https://example.com/photos/img[001-050].jpg");
        pattern.FontFamily = new FontFamily("Consolas");
        var filter = f.Text("Keep only (optional)", "");
        f.Note("Words find links that contain them; * and ? are wildcards; a leading ! leaves links out. Example: *.jpg !*thumb*");
        var links = f.Text("Links to download (you can edit them)", "", multi: true);
        links.Height = 160;
        var preview = f.Note("");
        void Update()
        {
            var n = BatchPattern.Count(pattern.Text);
            if (n > BatchPattern.Limit) { preview.Text = $"{n} links – the limit is {BatchPattern.Limit}"; links.Text = ""; return; }
            var kept = LinkFilter.Apply(BatchPattern.Expand(pattern.Text), filter.Text);
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
            var chosen = links.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
            if (chosen.Count == 0 || chosen.Count > BatchPattern.Limit) { preview.Text = chosen.Count == 0 ? "No links to download." : $"The limit is {BatchPattern.Limit} links."; return; }
            m.AddLinks(string.Join("\n", chosen), new AddOptions
            {
                Folder = folder.Text.Trim(), QueueId = (queue.SelectedItem as DownloadQueue)?.Id ?? DownloadQueue.MainId, Start = start,
            });
            f.Close();
        }
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Download Later", () => Go(false));
        f.AddButton("Start Download", () => Go(true), isDefault: true);
        f.Show();
    }

    // ---- Scheduler ----

    static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    public static void Scheduler(Window owner, DownloadManager m, string selectedId)
    {
        var f = new Form(owner, "Scheduler", 700);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        var list = new ListBox { Height = 320, DisplayMemberPath = "Name" };
        var addBtn = new Button { Content = "New Queue", Margin = new Thickness(0, 6, 0, 0) };
        var delBtn = new Button { Content = "Delete Queue", Margin = new Thickness(0, 4, 0, 0) };
        left.Children.Add(list);
        left.Children.Add(addBtn);
        left.Children.Add(delBtn);
        var editor = new StackPanel();
        Grid.SetColumn(editor, 1);
        grid.Children.Add(left);
        grid.Children.Add(editor);
        f.Body.Children.Add(grid);

        void Refill(DownloadQueue? select)
        {
            list.ItemsSource = null;
            list.ItemsSource = m.Queues.ToList();
            list.SelectedItem = select ?? m.Queues.First();
        }

        void Edit(DownloadQueue q)
        {
            editor.Children.Clear();
            TextBlock Label(string t) { var l = new TextBlock { Text = t, Margin = new Thickness(0, 8, 0, 2) }; editor.Children.Add(l); return l; }
            Label("Name");
            var name = new TextBox { Text = q.Name, IsEnabled = !q.IsMain, Padding = new Thickness(4, 3, 4, 3) };
            name.LostFocus += (_, _) => { if (name.Text.Trim().Length > 0) { q.Name = name.Text.Trim(); m.SaveQueues(); } };
            editor.Children.Add(name);

            var start = new CheckBox { Content = "Start at (HH:mm)", IsChecked = q.StartEnabled, Margin = new Thickness(0, 10, 0, 2) };
            var startTime = new TextBox { Text = q.StartTime, Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
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
            var stopTime = new TextBox { Text = q.StopTime, Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            var wake = new CheckBox { Content = "Wake the computer from sleep to start", IsChecked = q.WakeToStart, Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Uses a Windows wake timer; Power Options must allow wake timers. The app has to be running (it can be in the tray)." };
            foreach (var e in new UIElement[] { start, startTime, days, wake, stop, stopTime }) editor.Children.Add(e);

            Label("Downloads at the same time");
            var active = new TextBox { Text = (q.IsMain ? m.Settings.MaxActive : q.MaxActive).ToString(), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            editor.Children.Add(active);
            Label("When the queue is done");
            var after = new ComboBox { ItemsSource = Enum.GetValues<AfterQueue>().Select(DownloadQueue.AfterTitle).ToList(), SelectedIndex = (int)q.AfterDone };
            editor.Children.Add(after);
            var waiting = m.Rows.Count(r => r.Record.Queue == q.Id && r.Record.Status != DownloadStatus.Finished);
            var info = new TextBlock { Text = $"{waiting} file(s) waiting · {(q.Running ? "Running" : "Stopped")}", Foreground = Muted, Margin = new Thickness(0, 10, 0, 0) };
            editor.Children.Add(info);
            var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var apply = new Button { Content = "Apply", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
            var go = new Button { Content = "Start Now", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
            var halt = new Button { Content = "Stop", Padding = new Thickness(12, 3, 12, 3) };
            row.Children.Add(apply);
            row.Children.Add(go);
            row.Children.Add(halt);
            editor.Children.Add(row);
            var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
            editor.Children.Add(error);

            static bool IsTime(string t) => TimeSpan.TryParseExact(t.Trim(), @"hh\:mm", null, out var v) && v < TimeSpan.FromDays(1);
            void Apply()
            {
                if (!IsTime(startTime.Text) || !IsTime(stopTime.Text)) { error.Text = "Write times as HH:mm, for example 02:30."; return; }
                error.Text = "";
                if (name.Text.Trim().Length > 0 && !q.IsMain) q.Name = name.Text.Trim();
                q.StartEnabled = start.IsChecked == true;
                q.StartTime = startTime.Text.Trim();
                q.WakeToStart = wake.IsChecked == true;
                q.StopEnabled = stop.IsChecked == true;
                q.StopTime = stopTime.Text.Trim();
                q.AfterDone = (AfterQueue)Math.Max(0, after.SelectedIndex);
                var n = Math.Clamp(Number(active.Text, 3), 1, 10);
                if (q.IsMain)
                {
                    var s = m.Settings;
                    s.MaxActive = n;
                    m.ApplySettings(s);
                }
                else q.MaxActive = n;
                m.SaveQueues();
                Refill(q);
            }
            apply.Click += (_, _) => Apply();
            go.Click += (_, _) => { Apply(); m.StartQueue(q.Id); Refill(q); };
            halt.Click += (_, _) => { m.StopQueue(q.Id); Refill(q); };
        }

        list.SelectionChanged += (_, _) => { if (list.SelectedItem is DownloadQueue q) Edit(q); };
        addBtn.Click += (_, _) => Refill(m.NewQueue());
        delBtn.Click += (_, _) => { if (list.SelectedItem is DownloadQueue { IsMain: false } q) { m.DeleteQueue(q); Refill(null); } };
        Refill(m.Queues.FirstOrDefault(q => q.Id == selectedId));
        f.AddButton("Done", f.Close, isDefault: true, isCancel: true);
        f.Show();
    }

    // ---- Grabber ----

    public static void Grabber(Window owner, DownloadManager m, string queueId)
    {
        var f = new Form(owner, "Site Grabber", 640);
        f.Note("Explores a website section and lists the files it links to. Only use it on sites you are allowed to download from.");
        var start = f.Text("Start page", ClipboardLink());
        var depth = f.Text("Follow links this many levels deep (0-5)", "1");
        var types = f.Text("File types (empty = every file)", "zip rar 7z pdf mp3 mp4 jpg png");
        var stay = f.Check("Stay inside the start page's folder", true);
        var bar = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
        var explore = new Button { Content = "Explore", Padding = new Thickness(12, 3, 12, 3) };
        var status = new TextBlock { Foreground = Muted, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var all = new Button { Content = "All", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        var none = new Button { Content = "None", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        DockPanel.SetDock(explore, Dock.Left);
        DockPanel.SetDock(none, Dock.Right);
        DockPanel.SetDock(all, Dock.Right);
        bar.Children.Add(explore);
        bar.Children.Add(none);
        bar.Children.Add(all);
        bar.Children.Add(status);
        f.Body.Children.Add(bar);
        var results = new ListBox { Height = 180 };
        f.Body.Children.Add(results);
        var folder = f.Folder(m.Settings.Folder);
        var queue = f.Queue(m, queueId);
        CancellationTokenSource? cts = null;

        List<CheckBox> Boxes() => results.Items.OfType<CheckBox>().ToList();
        all.Click += (_, _) => Boxes().ForEach(b => b.IsChecked = true);
        none.Click += (_, _) => Boxes().ForEach(b => b.IsChecked = false);
        explore.Click += async (_, _) =>
        {
            if (cts != null) { cts.Cancel(); return; }
            if (!new DownloadJob(start.Text.Trim()).IsValid) { status.Text = "Enter an http or https address."; return; }
            var o = new GrabOptions
            {
                Start = new Uri(start.Text.Trim()), Depth = Math.Clamp(Number(depth.Text, 1), 0, 5),
                Types = FileTypes.Parse(types.Text), StayInFolder = stay.IsChecked == true,
            };
            cts = new CancellationTokenSource();
            explore.Content = "Stop";
            results.Items.Clear();
            status.Text = "Exploring…";
            var handler = Network.Handler(m.Settings.Proxy);
            handler.AllowAutoRedirect = true;
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var progress = new Progress<(int Pages, int Files)>(p => status.Text = $"{p.Pages} pages read, {p.Files} files found");
            IProgress<(int, int)> report = progress;
            var files = await SiteGrabber.ExploreAsync(o, http, (p, n) => report.Report((p, n)), cts.Token);
            foreach (var u in files) results.Items.Add(new CheckBox { Content = u.AbsoluteUri, IsChecked = true });
            status.Text = $"{files.Count} files found";
            explore.Content = "Explore";
            cts = null;
        };
        void Go(bool startNow)
        {
            var links = Boxes().Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList();
            if (links.Count == 0) return;
            m.AddLinks(string.Join("\n", links), new AddOptions
            {
                Folder = folder.Text.Trim(), QueueId = (queue.SelectedItem as DownloadQueue)?.Id ?? DownloadQueue.MainId, Start = startNow,
            });
            cts?.Cancel();
            f.Close();
        }
        f.AddButton("Close", () => { cts?.Cancel(); f.Close(); }, isCancel: true);
        f.AddButton("Download Later", () => Go(false));
        f.AddButton("Download Checked", () => Go(true), isDefault: true);
        f.Show();
    }

    // ---- Properties ----

    public static void Properties(Window owner, DownloadManager m, Row row)
    {
        var r = row.Record;
        var f = new Form(owner, "Properties", 560);
        f.Note($"{r.FileName}\n{r.Url}\nFolder: {r.Folder}\nSize: {(r.Size is long s ? Row.Bytes(s) : "Unknown")}   Status: {row.Status}");
        var queue = f.Queue(m, r.Queue);
        var user = f.Text("User name", r.Username ?? "");
        var pass = f.Field("Password", new PasswordBox { Password = r.Password ?? "", Padding = new Thickness(4, 3, 4, 3) });
        var sum = f.Text("Expected checksum (MD5, SHA-1, SHA-256 or SHA-512)", r.Checksum ?? "");
        var result = f.Note(r.ChecksumResult ?? "");
        if (r.Status == DownloadStatus.Finished)
        {
            var check = new Button { Content = "Check Now", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 3, 12, 3) };
            check.Click += (_, _) => result.Text = m.Verify(row, sum.Text);
            f.Body.Children.Add(check);
        }
        var limit = f.Text("Speed limit in KB/s (0 = none)", (r.SpeedLimitKBps ?? 0).ToString());
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Save", () =>
        {
            m.SetDetails(row, user.Text.Trim(), pass.Password, Checksum.Parse(sum.Text) == null ? "" : sum.Text.Trim(), Number(limit.Text),
                (queue.SelectedItem as DownloadQueue)?.Id ?? DownloadQueue.MainId);
            f.Close();
        }, isDefault: true);
        f.Show();
    }

    // ---- Refresh link ----

    public static void RefreshLink(Window owner, DownloadManager m, Row row)
    {
        var f = new Form(owner, "Refresh Link", 540);
        f.Note("Links from many sites stop working after a while. Paste a fresh link for the same file and the download continues from where it stopped. " +
               "Or choose Wait for Browser, then start the same download again in your browser: the extension hands the new link over automatically.");
        var link = f.Text("New link", ClipboardLink());
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Wait for Browser", () => { m.Refresh(row, null); f.Close(); });
        f.AddButton("Use This Link", () => { if (new DownloadJob(link.Text.Trim()).IsValid) { m.Refresh(row, link.Text.Trim()); f.Close(); } }, isDefault: true);
        f.Show();
    }

    // ---- Options ----

    public static void Options(Window owner, DownloadManager m)
    {
        var s = m.Settings;
        var f = new Form(owner, "Options", 600);
        var tabs = new TabControl { Height = 430 };
        f.Body.Children.Add(tabs);
        StackPanel Tab(string header)
        {
            var p = new StackPanel { Margin = new Thickness(10) };
            tabs.Items.Add(new TabItem { Header = header, Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            return p;
        }
        TextBox Box(StackPanel p, string label, string value)
        {
            p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
            var t = new TextBox { Text = value, Padding = new Thickness(4, 3, 4, 3) };
            p.Children.Add(t);
            return t;
        }
        CheckBox Tick(StackPanel p, string label, bool value)
        {
            var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 8, 0, 0) };
            p.Children.Add(c);
            return c;
        }
        ComboBox Pick(StackPanel p, string label, string[] titles, string[] values, string current)
        {
            p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 2) });
            var c = new ComboBox { ItemsSource = titles.Select(L.T).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(values, current)) };
            System.Windows.Automation.AutomationProperties.SetName(c, label);
            p.Children.Add(c);
            return c;
        }
        TextBlock Hint(StackPanel p, string text)
        {
            var t = new TextBlock { Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 11 };
            p.Children.Add(t);
            return t;
        }

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
        var folder = Box(general, "Save to folder", s.Folder);
        var sort = Tick(general, "Put each type in its own folder (Video, Music, Documents, Programs, Compressed, Images)", s.SortIntoCategoryFolders);
        var rememberFolder = Tick(general, "Start the Add window in the folder I used last time", s.RememberLastFolder);
        var notify = Tick(general, "Notifications", s.Notifications);
        var updates = Tick(general, "Check for updates when the app opens", s.CheckForUpdates);
        general.Children.Add(new TextBlock { Text = "Language", Margin = new Thickness(0, 10, 0, 2) });
        var languageChoices = new List<string> { "" }.Concat(L.Languages.Select(l => l.Code)).ToList();
        var language = new ComboBox
        {
            ItemsSource = new List<string> { L.T("Same as the system") }.Concat(L.Languages.Select(l => l.Reviewed ? l.Name : l.Name + " (machine translation)")).ToList(),
            SelectedIndex = Math.Max(0, languageChoices.IndexOf(s.Language)),
        };
        general.Children.Add(language);
        general.Children.Add(new TextBlock { Text = "Appearance", Margin = new Thickness(0, 10, 0, 2) });
        var customJson = s.ColorSchemeJson ?? "";
        var themeChoices = AppTheme.AllChoices(customJson);
        var theme = new ComboBox { ItemsSource = themeChoices.Select(c => L.T(AppTheme.Title(c))).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(themeChoices, s.Theme)) };
        System.Windows.Automation.AutomationProperties.SetName(theme, "Appearance");
        general.Children.Add(theme);
        var schemeButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        Button SchemeButton(string text, Action click)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
            b.Click += (_, _) => click();
            schemeButtons.Children.Add(b);
            return b;
        }
        var schemeNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 0) };
        SchemeButton("Import Colour Scheme…", () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Import Colour Scheme", Filter = "Colour scheme (*.json)|*.json" };
            if (d.ShowDialog(f.Window) != true) return;
            var info = new System.IO.FileInfo(d.FileName);
            var (scheme, problem) = info.Length <= 64 * 1024 ? ColorScheme.Import(System.IO.File.ReadAllText(d.FileName)) : (null, "This file is too large to be a colour scheme.");
            if (scheme == null) { schemeNote.Text = problem; return; }
            customJson = scheme.Export();
            themeChoices = AppTheme.AllChoices(customJson);
            theme.ItemsSource = themeChoices.Select(c => L.T(AppTheme.Title(c))).ToList();
            theme.SelectedIndex = Array.IndexOf(themeChoices, "custom");
            schemeNote.Text = $"{scheme.Name} is ready; it is used when you press OK.";
        });
        SchemeButton("Export Colour Scheme…", () =>
        {
            var chosen = AppTheme.SchemeFor(themeChoices[Math.Max(0, theme.SelectedIndex)], customJson);
            if (chosen == null) { schemeNote.Text = "Choose a colour scheme in the list to export it."; return; }
            var d = new Microsoft.Win32.SaveFileDialog { Title = "Export Colour Scheme", Filter = "Colour scheme (*.json)|*.json", FileName = chosen.Name + ".json" };
            if (d.ShowDialog(f.Window) != true) return;
            System.IO.File.WriteAllText(d.FileName, chosen.Export());
            schemeNote.Text = "Saved.";
        });
        general.Children.Add(schemeButtons);
        general.Children.Add(schemeNote);
        general.Children.Add(new TextBlock { Text = "Font", Margin = new Thickness(0, 10, 0, 2) });
        var fontNames = new List<string> { L.T("Same as Windows") }.Concat(AppTheme.InstalledFonts()).ToList();
        var font = new ComboBox { ItemsSource = fontNames, SelectedIndex = Math.Max(0, fontNames.IndexOf(s.FontFamily ?? "")) };
        System.Windows.Automation.AutomationProperties.SetName(font, "Font");
        general.Children.Add(font);
        general.Children.Add(new TextBlock { Text = "Interface size", Margin = new Thickness(0, 10, 0, 2) });
        var scales = new[] { 75, 90, 100, 110, 125, 150, 175, 200 };
        var scale = new ComboBox { ItemsSource = scales.Select(v => v + "%").ToList(), SelectedIndex = Array.IndexOf(scales, s.InterfaceScale) is var si && si >= 0 ? si : 2 };
        System.Windows.Automation.AutomationProperties.SetName(scale, "Interface size");
        general.Children.Add(scale);
        var sizeUnits = Pick(general, "Sizes", new[] { "KB, MB, GB (1 KB = 1024 bytes)", "KiB, MiB, GiB (1 KiB = 1024 bytes)", "KB, MB, GB (1 KB = 1000 bytes)" }, Units.SizeStyles, s.SizeUnits);
        var speedUnits = Pick(general, "Speeds", new[] { "Bytes per second (MB/s)", "Bits per second (Mbit/s)" }, Units.SpeedStyles, s.SpeedUnits);
        var dateStyle = Pick(general, "Dates in the list", new[] { "Date and time", "How long ago (5 min ago)" }, Units.DateStyles, s.DateStyle);
        general.Children.Add(new TextBlock { Text = "Browser downloads", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
        var askDialog = Tick(general, "Show the download window when a link is clicked in the browser", s.ShowDownloadDialog);
        var progressWin = Tick(general, "Show a progress window for each download I start", s.ShowProgressWindow);
        var toFront = Tick(general, "Bring the main window to the front for browser downloads", s.BringToFrontOnBrowserDownload);
        Hint(general, "With the download window off, downloads start right away. Closing the main window keeps the app running in the tray.");
        general.Children.Add(new TextBlock { Text = "Video tools", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
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
        connection.Children.Add(new TextBlock { Text = "Debrid service (optional)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
        var debridService = new ComboBox { ItemsSource = Enum.GetValues<DebridService>().Select(d => L.T(DebridSettings.Title(d))).ToList(), SelectedIndex = (int)s.Debrid.Service };
        connection.Children.Add(debridService);
        connection.Children.Add(new TextBlock { Text = "Your API key for the service", Margin = new Thickness(0, 6, 0, 2) });
        var debridKey = new PasswordBox { Password = s.Debrid.Key, Padding = new Thickness(4, 3, 4, 3) };
        connection.Children.Add(debridKey);
        var debridHosts = Box(connection, "Sites whose links go through it (comma separated, e.g. example-host.com)", s.Debrid.Hosts);
        Hint(connection, "Off by default. Uses your own account; links from other sites are never sent to the service. The key is saved encrypted and never in backups.");
        connection.Children.Add(new TextBlock { Text = "Proxy", Margin = new Thickness(0, 10, 0, 2) });
        var mode = new ComboBox { ItemsSource = Enum.GetValues<ProxyMode>().Select(ProxySettings.Title).ToList(), SelectedIndex = (int)s.Proxy.Mode };
        connection.Children.Add(mode);
        var host = Box(connection, "Proxy address (for a proxy script: its web address or file)", s.Proxy.Host);
        var port = Box(connection, "Port", s.Proxy.Port.ToString());
        var puser = Box(connection, "Proxy user name (optional)", s.Proxy.Username);
        connection.Children.Add(new TextBlock { Text = "Proxy password", Margin = new Thickness(0, 6, 0, 2) });
        var ppass = new PasswordBox { Password = s.Proxy.Password, Padding = new Thickness(4, 3, 4, 3) };
        connection.Children.Add(ppass);
        Hint(connection, "Proxy changes apply to downloads started afterwards. Password-protected downloads: add the user name and password in the Add window or under Properties.");
        connection.Children.Add(new TextBlock { Text = "Advanced", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
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
        System.Windows.Automation.AutomationProperties.SetName(ipVersion, "Internet addresses");
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
        var check = new Button { Content = "Check the Connection", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 6, 0, 0) };
        check.Click += async (_, _) =>
        {
            checkResult.Text = "Checking…";
            var keep = Network.Options;
            Network.Options = new NetworkOptions { ConnectTimeoutSeconds = Number(connectTimeout.Text, 30), DnsOverHttps = doh.Text, DnsOverTls = dot.Text ?? "", BindAdapter = adapterNames[Math.Max(0, adapter.SelectedIndex)] }.Clamped();
            try
            {
                var proxy = new ProxySettings { Mode = (ProxyMode)Math.Max(0, mode.SelectedIndex), Host = host.Text.Trim(), Port = Number(port.Text, 8080), Username = puser.Text.Trim(), Password = ppass.Password };
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
        void SiteButton(string text, Action click)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => click();
            siteButtons.Children.Add(b);
        }
        void EditSelected()
        {
            var i = siteList.SelectedIndex;
            if (i >= 0 && EditSite(f.Window, Clone(profiles[i])) is { } p) { profiles[i] = p; FillSites(); }
        }
        SiteButton("Add Site…", () => { if (EditSite(f.Window, new HostProfile()) is { } p) { profiles.Add(p); FillSites(); } });
        SiteButton("Edit…", EditSelected);
        SiteButton("Remove", () =>
        {
            var i = siteList.SelectedIndex;
            if (i < 0 || MessageBox.Show(f.Window, $"Remove the settings for {HostProfiles.Normalize(profiles[i].Host)}?", "Remove Site",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            profiles.RemoveAt(i);
            FillSites();
        });
        sites.Children.Add(siteButtons);
        siteList.MouseDoubleClick += (_, _) => EditSelected();

        var categories = Tab("Categories");
        Hint(categories, "Your own categories, one per line, in the order they are checked: [icon] Name: types @ sites > folder. " +
                         "The icon, the sites and the folder can be left out. Sites limit a category to files from those addresses " +
                         "(example.com includes its subdomains; * matches anything, as in github.com/*/releases); with sites and no types, " +
                         "every file from those sites goes there. The folder can be a full path or a folder inside the download folder " +
                         "(left out: the category's name). Rename or reorder a category by editing its line. Your categories come before the " +
                         "built-in types, appear in the sidebar and apply even when \"Put each type in its own folder\" is off. " +
                         "Example: 📚 Comics: cbz cbr cb7 > Comics");
        var categoryText = new TextBox { Text = CustomCategory.ToLines(s.CustomCategories), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            Height = 200, Margin = new Thickness(0, 8, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas") };
        System.Windows.Automation.AutomationProperties.SetName(categoryText, "Your categories, one per line");
        categories.Children.Add(categoryText);
        // The same categories as a list: select one and move it, or drag it to its new place.
        var categoryList = new ListBox { Height = 110, Margin = new Thickness(0, 6, 0, 0), AllowDrop = true };
        System.Windows.Automation.AutomationProperties.SetName(categoryList, "Your categories in the order they are checked");
        void RefreshCategoryList()
        {
            var keep = categoryList.SelectedIndex;
            categoryList.ItemsSource = CustomCategory.Lines(categoryText.Text);
            categoryList.SelectedIndex = Math.Min(keep, categoryList.Items.Count - 1);
        }
        void MoveCategory(int from, int to)
        {
            if (from < 0) return;
            categoryText.Text = CustomCategory.Move(categoryText.Text, from, to);
            categoryList.SelectedIndex = Math.Clamp(to, 0, categoryList.Items.Count - 1);
        }
        categoryText.TextChanged += (_, _) => RefreshCategoryList();
        Point dragStart = default;
        categoryList.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(categoryList);
        categoryList.PreviewMouseMove += (_, e) =>
        {
            var at = e.GetPosition(categoryList);
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || categoryList.SelectedIndex < 0
                || Math.Abs(at.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            DragDrop.DoDragDrop(categoryList, categoryList.SelectedIndex, DragDropEffects.Move);
        };
        categoryList.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(int)) is not int from) return;
            var target = (e.OriginalSource as DependencyObject) is { } src && ItemsControl.ContainerFromElement(categoryList, src) is ListBoxItem item
                ? categoryList.ItemContainerGenerator.IndexFromContainer(item) : categoryList.Items.Count - 1;
            MoveCategory(from, target);
        };
        categories.Children.Add(categoryList);
        var categoryButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (label, act) in new (string, Action)[]
                 {
                     ("Move Up", () => MoveCategory(categoryList.SelectedIndex, categoryList.SelectedIndex - 1)),
                     ("Move Down", () => MoveCategory(categoryList.SelectedIndex, categoryList.SelectedIndex + 1)),
                     ("Remove", () => { if (categoryList.SelectedIndex >= 0) categoryText.Text = CustomCategory.Remove(categoryText.Text, categoryList.SelectedIndex); }),
                 })
        {
            var b = new Button { Content = label, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
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
                        "(the connection inside the network is not encrypted). Windows may ask to allow the app through the firewall.");
        var remoteButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var pair = new Button { Content = "Pair a Phone…", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 6) };
        pair.Click += (_, _) =>
        {
            if (m.PairingUrl() is not { } url) { MessageBox.Show(f.Window, "Turn on remote access and choose Save first, then open Options again.", "Pair a Phone"); return; }
            PairQr(f.Window, url);
        };
        var unpair = new Button { Content = "Unpair All Devices", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 6) };
        unpair.Click += (_, _) =>
        {
            if (MessageBox.Show(f.Window, "Paired phones lose access until they scan the new code.", "Unpair All Devices", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
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
        void BackupButton(string text, Action click)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => { try { click(); } catch (Exception e) { backupResult.Text = e.Message; } };
            backupButtons.Children.Add(b);
        }
        BackupButton("Export Backup…", () =>
        {
            var d = new Microsoft.Win32.SaveFileDialog { Title = "Export Backup", FileName = $"nexonic-backup-{DateTime.Now:yyyy-MM-dd}.json", Filter = "Backup (*.json)|*.json" };
            if (d.ShowDialog(f.Window) != true) return;
            File.WriteAllText(d.FileName, m.ExportBackup(HelpWindows.Version));
            backupResult.Text = $"Saved {Path.GetFileName(d.FileName)}: {m.Rows.Count} downloads, {m.Queues.Count} queues.";
        });
        BackupButton("Import Backup…", () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Import Backup", Filter = "Backup (*.json)|*.json" };
            if (d.ShowDialog(f.Window) != true) return;
            var n = m.ImportBackup(File.ReadAllText(d.FileName), takeSettings.IsChecked == true);
            backupResult.Text = $"Added {n} download{(n == 1 ? "" : "s")}" + (takeSettings.IsChecked == true ? " and took the backup's settings." : ".");
            if (takeSettings.IsChecked == true) f.Close();
        });
        BackupButton("Export Links…", () =>
        {
            var d = new Microsoft.Win32.SaveFileDialog { Title = "Export Links", FileName = "nexonic-links.txt", Filter = "Text (*.txt)|*.txt" };
            if (d.ShowDialog(f.Window) != true) return;
            File.WriteAllLines(d.FileName, m.Rows.Select(r => r.Record.Url));
            backupResult.Text = $"Saved {m.Rows.Count} links to {Path.GetFileName(d.FileName)}.";
        });
        backup.Children.Add(backupButtons);
        backup.Children.Add(takeSettings);
        backup.Children.Add(new TextBlock { Text = "Missing files", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
        var removeMissing = Tick(backup, "When the app starts, remove finished downloads whose file was deleted or moved", s.RemoveMissingOnStart);
        var missingNow = new Button { Content = "Remove Missing Files Now", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 6, 0, 0) };
        var missingResult = Hint(backup, $"{m.Rows.Count(r => MissingFiles.IsMissing(r.Record))} finished downloads have no file now (shown as File missing).");
        missingNow.Click += (_, _) => missingResult.Text = $"Removed {m.RemoveMissing()} from the list. Files on disk are not touched.";
        backup.Children.Add(missingNow);
        backup.Children.Add(new TextBlock { Text = "Data folder", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
        var dataFolder = Box(backup, "Settings, the download list and unfinished parts are kept in", AppPaths.Support);
        var dataButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var dataResult = Hint(backup, DataLocation.Pending(AppPaths.Default) is { } waiting ? $"Moves to {waiting} the next time the app starts." : m.DataMoveProblem ?? "");
        var chooseData = new Button { Content = "Choose…", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
        chooseData.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "Data folder" };
            if (d.ShowDialog(f.Window) == true) dataFolder.Text = d.FolderName;
        };
        var moveData = new Button { Content = "Move at Next Start", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
        moveData.Click += (_, _) => dataResult.Text = DataLocation.Request(AppPaths.Default, dataFolder.Text.Trim()) is { } why ? L.T(why)
            : L.T("The data moves the next time the app starts. Everything is copied and checked before the old copy is removed.");
        var usualData = new Button { Content = "Use the Usual Folder", Padding = new Thickness(12, 3, 12, 3) };
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
        var mark = Tick(after, "Mark files as downloaded from the internet so Windows checks them when opened", s.MarkDownloads);
        var scan = Tick(after, "Scan each finished file with a virus scanner", s.ScanEnabled);
        var scanCmd = Box(after, "Scanner command (the file is added at the end)", s.ScanCommand);
        Hint(after, "The default uses the virus scanner built into Windows. Expected checksums are checked automatically when a download finishes. Queues can also quit the app, sleep or shut down the computer when they are done (Scheduler).");

        var browsers = Tab("Browsers & Command Line");
        Hint(browsers, "Chrome, Edge, Brave, Vivaldi: open the browser's extensions page, switch on Developer mode, choose Load unpacked and pick the Extension folder in the app's folder.");
        var openFolder = new Button { Content = "Open the App Folder", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 6, 0, 0) };
        openFolder.Click += (_, _) => Process.Start("explorer.exe", $"\"{AppContext.BaseDirectory}\"");
        browsers.Children.Add(openFolder);
        Hint(browsers, "Firefox: drag \"Firefox Add-on.xpi\" from the app's folder onto a Firefox window. Firefox installs add-ons that are signed; see the README for signing your copy.");
        Hint(browsers, "Command line: open a new terminal and type ndm help. Example: ndm https://example.com/file.zip --queue Night");
        browsers.Children.Add(new TextBlock { Text = "Automation", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
        var auto = s.Automation;
        var apiOn = Tick(browsers, "Let programs on this computer control downloads (local API)", auto.Enabled);
        var apiPort = Box(browsers, "Port", auto.Port.ToString());
        var apiKey = Box(browsers, "API key (programs send it as Authorization: Bearer <key>)", auto.Key);
        apiKey.IsReadOnly = true;
        var keyButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var newKey = new Button { Content = "New Key", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
        newKey.Click += (_, _) => apiKey.Text = AutomationSettings.NewKey();
        var copyKey = new Button { Content = "Copy Key", Padding = new Thickness(12, 3, 12, 3) };
        copyKey.Click += (_, _) => { if (apiKey.Text.Length > 0) Clipboard.SetText(apiKey.Text); };
        keyButtons.Children.Add(newKey);
        keyButtons.Children.Add(copyKey);
        browsers.Children.Add(keyButtons);
        Hint(browsers, (m.ApiProblem.Length > 0 ? m.ApiProblem + " " : "") +
                       "Only this computer can connect (127.0.0.1), and only with the key. Add, pause, resume and remove downloads and start or stop queues; see docs/API.md. The key is saved encrypted and never in backups.");

        var error = f.Note("");
        error.Foreground = Brushes.Firebrick;
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Save", () =>
        {
            var p = Number(port.Text, -1);
            if (p is < 1 or > 65535) { error.Text = "The proxy port must be between 1 and 65535."; return; }
            if ((ProxyMode)Math.Max(0, mode.SelectedIndex) == ProxyMode.Pac && !Pac.AddressOk(host.Text))
            { error.Text = "Enter the proxy script's web address (http:// or https://) or the full path of the file."; return; }
            s.Connections = Math.Clamp(Number(conn.Text, 8), 1, Limits.MaxConnections);
            s.AutoConnections = autoConn.IsChecked == true;
            s.DuplicateAction = dupChoices[Math.Max(0, dupAction.SelectedIndex)];
            s.AutoMaxConnections = Math.Clamp(Number(autoMax.Text, 32), 1, Limits.MaxConnections);
            s.MaxActive = Math.Clamp(Number(active.Text, 3), 1, 10);
            s.MaxManualActive = Math.Clamp(Number(manualActive.Text, 0), 0, 10);
            s.SizeUnits = Units.SizeStyles[Math.Max(0, sizeUnits.SelectedIndex)];
            s.SpeedUnits = Units.SpeedStyles[Math.Max(0, speedUnits.SelectedIndex)];
            s.DateStyle = Units.DateStyles[Math.Max(0, dateStyle.SelectedIndex)];
            s.AudioFormat = AudioConversion.Formats[Math.Max(0, audioFormat.SelectedIndex)];
            s.RemoveMissingOnStart = removeMissing.IsChecked == true;
            s.Folder = folder.Text.Trim().Length > 0 ? folder.Text.Trim() : s.Folder;
            s.SortIntoCategoryFolders = sort.IsChecked == true;
            s.Notifications = notify.IsChecked == true;
            s.CheckForUpdates = updates.IsChecked == true;
            s.Language = languageChoices[Math.Max(0, language.SelectedIndex)];
            s.Theme = themeChoices[Math.Max(0, theme.SelectedIndex)];
            s.ColorSchemeJson = customJson;
            s.FontFamily = font.SelectedIndex > 0 ? fontNames[font.SelectedIndex] : "";
            AppTheme.Apply(s.Theme, s.ColorSchemeJson, s.FontFamily);
            if (L.Resolve(s.Language) != L.Code)
            {
                L.Use(s.Language);
                owner.Dispatcher.BeginInvoke(new Action(Translate.Refresh));
            }
            s.ShowDownloadDialog = askDialog.IsChecked == true;
            s.ShowProgressWindow = progressWin.IsChecked == true;
            s.BringToFrontOnBrowserDownload = toFront.IsChecked == true;
            s.SpeedLimitKBps = Number(limit.Text);
            s.ActiveSpeedLimitKBps = Number(activeLimit.Text);
            if (SpeedSchedule.Parse(schedule.Text).Problem is { } scheduleProblem) { error.Text = scheduleProblem; return; }
            s.SpeedScheduleText = schedule.Text ?? "";
            s.DataCapMB = Math.Max(0, Number(capMB.Text));
            s.DataCapHours = Math.Clamp(Number(capHours.Text, 24), 1, 24 * 31);
            s.RememberLastFolder = rememberFolder.IsChecked == true;
            if (!s.RememberLastFolder) s.LastFolder = "";
            s.Debrid = new DebridSettings { Service = (DebridService)Math.Max(0, debridService.SelectedIndex), Key = debridKey.Password.Trim(), Hosts = debridHosts.Text.Trim() };
            s.Proxy = new ProxySettings
            {
                Mode = (ProxyMode)Math.Max(0, mode.SelectedIndex), Host = host.Text.Trim(), Port = p,
                Username = puser.Text.Trim(), Password = ppass.Password,
            };
            s.MonitorClipboard = watch.IsChecked == true;
            s.ClipboardTypes = clipTypes.Text;
            s.ShowCompleteDialog = complete.IsChecked == true;
            s.PlaySound = sound.IsChecked == true;
            var failedText = (soundFailed.Text).Trim();
            if ((Sounds.Problem(soundDone.Text, quiet.Text) ?? Sounds.Problem(failedText, null) ?? Sounds.Problem(soundQueue.Text, null)) is { } soundProblem) { error.Text = soundProblem; return; }
            s.SoundFinished = (soundDone.Text).Trim();
            s.SoundFailed = failedText;
            s.SoundQueueDone = (soundQueue.Text).Trim();
            s.QuietHours = (quiet.Text).Trim();
            s.MarkDownloads = mark.IsChecked == true;
            s.ScanEnabled = scan.IsChecked == true;
            s.ScanCommand = scanCmd.Text.Trim();
            if (NetworkOptions.DnsProblem(doh.Text, dot.Text, strictDns.IsChecked == true) is { } dnsProblem) { error.Text = dnsProblem; return; }
            if (Adapters.BlocklistProblem(blocklist.Text) is { } blockProblem) { error.Text = blockProblem; return; }
            if ((EnginePaths.Problem(ytdlpPath.Text, "yt-dlp") ?? EnginePaths.Problem(ffmpegPath.Text, "FFmpeg")) is { } toolProblem) { error.Text = toolProblem; return; }
            var (customList, categoryProblem) = CustomCategory.ParseLines(categoryText.Text);
            if (categoryProblem != null) { error.Text = categoryProblem; tabs.SelectedItem = categories.Parent is ScrollViewer sv ? sv.Parent : null; return; }
            var remotePortNumber = Number(remotePort.Text, -1);
            if (remoteOn.IsChecked == true && remotePortNumber is < 1024 or > 65535) { error.Text = "The remote port must be between 1024 and 65535."; return; }
            s.CustomCategories = customList;
            s.YtDlpPath = ytdlpPath.Text.Trim();
            s.FFmpegPath = ffmpegPath.Text.Trim();
            s.InterfaceScale = scales[Math.Max(0, scale.SelectedIndex)];
            s.Remote = new RemoteSettings
            {
                Enabled = remoteOn.IsChecked == true, Port = remotePortNumber is >= 1024 and <= 65535 ? remotePortNumber : 47813, Key = s.Remote.Key,
            };
            s.Network = new NetworkOptions
            {
                MinSegmentKB = Number(minSeg.Text, 512), Retries = Number(retries.Text, 6),
                ConnectTimeoutSeconds = Number(connectTimeout.Text, 30), ReadTimeoutSeconds = Number(readTimeout.Text, 60), DnsOverHttps = doh.Text, DnsOverTls = dot.Text ?? "", StrictDns = strictDns.IsChecked == true,
                BindAdapter = adapterNames[Math.Max(0, adapter.SelectedIndex)], TorrentBlocklist = blocklist.Text,
                IpVersion = ipVersions[Math.Max(0, ipVersion.SelectedIndex)],
            }.Clamped();
            s.HostProfiles = profiles;
            var apiPortNumber = Number(apiPort.Text, -1);
            if (apiOn.IsChecked == true && apiPortNumber is < 1024 or > 65535) { error.Text = "The automation port must be between 1024 and 65535."; return; }
            s.Automation = new AutomationSettings
            {
                Enabled = apiOn.IsChecked == true, Port = apiPortNumber is >= 1024 and <= 65535 ? apiPortNumber : 47812,
                Key = apiKey.Text.Length >= 16 ? apiKey.Text : apiOn.IsChecked == true ? AutomationSettings.NewKey() : "",
            };
            m.ApplySettings(s);
            m.ApplyAutomation();
            m.ApplyRemote();
            App.ApplyScale(s.InterfaceScale);
            f.Close();
        }, isDefault: true);
        f.Show();
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

    static int? Optional(string text, int min, int max) => int.TryParse(text.Trim(), out var n) ? Math.Clamp(n, min, max) : null;

    /// <summary>Edits one site's settings; null when cancelled.</summary>
    static HostProfile? EditSite(Window owner, HostProfile p)
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
        var folder = f.Folder(p.Folder ?? "");
        var queue = f.Text("Queue (name; empty = the one chosen when adding)", p.QueueName ?? "");
        var later = f.Check("Add downloads from this site stopped (start them yourself or with a queue)", p.StartAtOnce == false);
        var user = f.Text("User name", p.Username ?? "");
        var pass = f.Field("Password", new PasswordBox { Password = p.Password ?? "", Padding = new Thickness(4, 3, 4, 3) });
        var ownProxy = f.Check("Use its own proxy", p.Proxy != null);
        var proxyMode = f.Field("Proxy", new ComboBox { ItemsSource = Enum.GetValues<ProxyMode>().Select(ProxySettings.Title).ToList(), SelectedIndex = (int)(p.Proxy?.Mode ?? ProxyMode.None) });
        var proxyHost = f.Text("Proxy address", p.Proxy?.Host ?? "");
        var proxyPort = f.Text("Port", (p.Proxy?.Port ?? 8080).ToString());
        var error = f.Note("");
        error.Foreground = Brushes.Firebrick;
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
                Password = string.IsNullOrEmpty(pass.Password) ? null : pass.Password,
                Proxy = ownProxy.IsChecked == true
                    ? new ProxySettings { Mode = (ProxyMode)Math.Max(0, proxyMode.SelectedIndex), Host = proxyHost.Text.Trim(), Port = Number(proxyPort.Text, 8080), Password = p.Proxy?.Password ?? "", Username = p.Proxy?.Username ?? "" }
                    : null,
            };
            f.Close();
        }, isDefault: true);
        f.Show();
        return result;
    }

    // ---- QR code ----

    /// <summary>The downloads as a QR code: the phone's camera opens them in Nexonic on Android or iPhone.</summary>
    public static void ShowQr(Window owner, List<string> urls)
    {
        // As many codes as the links need: Previous and Next go through them.
        var codes = ShareLinks.Codes(urls);
        if (codes.Count == 0) return;
        static DrawingImage Picture(string link)
        {
            var qr = QrCode.Encode(link);
            var geometry = new GeometryGroup();
            for (var y = 0; y < qr.Size; y++)
                for (var x = 0; x < qr.Size; x++)
                    if (qr[x, y]) geometry.Children.Add(new RectangleGeometry(new Rect(x, y, 1, 1)));
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(-4, -4, qr.Size + 8, qr.Size + 8))));
            drawing.Children.Add(new GeometryDrawing(Brushes.Black, null, geometry));
            return new DrawingImage(drawing);
        }
        var image = new Image { Width = 300, Height = 300, Margin = new Thickness(0, 4, 0, 8) };
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        var f = new Form(owner, "Download on Your Phone", 400);
        f.Body.Children.Add(image);
        var caption = f.Note("");
        var index = 0;
        void Show()
        {
            var (link, count) = codes[index];
            image.Source = Picture(link);
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
        f.Show();
    }

    /// <summary>The pairing code for remote access: the phone's camera opens the remote page already paired.</summary>
    public static void PairQr(Window owner, string url)
    {
        var qr = QrCode.Encode(url);
        var geometry = new GeometryGroup();
        for (var y = 0; y < qr.Size; y++)
            for (var x = 0; x < qr.Size; x++)
                if (qr[x, y]) geometry.Children.Add(new RectangleGeometry(new Rect(x, y, 1, 1)));
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(-4, -4, qr.Size + 8, qr.Size + 8))));
        drawing.Children.Add(new GeometryDrawing(Brushes.Black, null, geometry));
        var image = new Image { Source = new DrawingImage(drawing), Width = 300, Height = 300, Margin = new Thickness(0, 4, 0, 8) };
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        System.Windows.Automation.AutomationProperties.SetName(image, "Pairing QR code");
        var f = new Form(owner, "Pair a Phone", 420);
        f.Body.Children.Add(image);
        f.Note("Scan with your phone's camera while the phone is on the same network as this computer. The page it opens stays paired; add it to your home screen to open it again.");
        f.Note("Anyone with this code can manage your downloads until you choose Unpair All Devices, so show it only to your own devices.");
        var addresses = RemoteAccess.LanAddresses();
        if (addresses.Count > 1) f.Note("Other addresses of this computer: " + string.Join(", ", addresses.Skip(1)));
        f.AddButton("Close", f.Close, isDefault: true, isCancel: true);
        f.Show();
    }

    // ---- Download complete ----

    public static void Complete(DownloadManager m, DownloadRecord r)
    {
        var w = new Window
        {
            Title = "Download Complete", Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ShowActivated = true,
        };
        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = r.FileName, FontWeight = FontWeights.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        p.Children.Add(new TextBlock
        {
            Text = $"{(r.Size is long s ? Row.Bytes(s) : "")} saved to {r.SavedPath}", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        if (r.ChecksumResult is { } c)
            p.Children.Add(new TextBlock { Text = c, Foreground = c.Contains("not") ? Brushes.Firebrick : Brushes.SeaGreen, Margin = new Thickness(0, 4, 0, 0) });
        var never = new CheckBox { Content = "Don't show this again", Margin = new Thickness(0, 10, 0, 0) };
        p.Children.Add(never);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
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
        Button B(string text, Action a, bool def = false, bool cancel = false)
        {
            var b = new Button { Content = text, MinWidth = 90, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), IsDefault = def, IsCancel = cancel };
            b.Click += (_, _) => a();
            buttons.Children.Add(b);
            return b;
        }
        B("Close", Close, cancel: true);
        B("Open Folder", () => { if (r.SavedPath is { } path) Process.Start("explorer.exe", $"/select,\"{path}\""); Close(); });
        B("Open", () => { if (r.SavedPath is { } path) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); Close(); }, def: true);
        p.Children.Add(buttons);
        w.Content = p;
        w.Show();
    }

    public static void PowerCountdown(Window owner, DownloadQueue q, AfterQueue action, Action execute)
    {
        var actionName = action switch
        {
            AfterQueue.Sleep => "sleep",
            AfterQueue.ShutDown => "shut down",
            AfterQueue.Quit => "quit",
            _ => "finish",
        };
        var w = new Window
        {
            Title = "Queue Completed",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            ShowActivated = true,
        };
        var p = new StackPanel { Margin = new Thickness(20) };
        p.Children.Add(new TextBlock { Text = $"Queue '{q.Name}' Finished", FontSize = 16, FontWeight = FontWeights.SemiBold });
        var msg = new TextBlock
        {
            Text = $"All downloads completed. The computer will {actionName} in 30 seconds.",
            Margin = new Thickness(0, 8, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        };
        p.Children.Add(msg);

        var countdownText = new TextBlock
        {
            Text = "30",
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x09, 0x69, 0xDA)),
            Margin = new Thickness(0, 0, 0, 14),
        };
        p.Children.Add(countdownText);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var cancelBtn = new Button
        {
            Content = $"Cancel {char.ToUpper(actionName[0])}{actionName[1..]}",
            MinWidth = 120,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(4),
            IsCancel = true,
        };
        var nowBtn = new Button
        {
            Content = $"{char.ToUpper(actionName[0])}{actionName[1..]} Now",
            MinWidth = 100,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(4),
            IsDefault = true,
        };
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(nowBtn);
        p.Children.Add(buttons);
        w.Content = p;

        var seconds = 30;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            seconds--;
            countdownText.Text = seconds.ToString();
            msg.Text = $"All downloads completed. The computer will {actionName} in {seconds} seconds.";
            if (seconds <= 0)
            {
                timer.Stop();
                w.Close();
                execute();
            }
        };

        cancelBtn.Click += (_, _) =>
        {
            timer.Stop();
            if (action == AfterQueue.ShutDown)
            {
                try { Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
            }
            w.Close();
        };

        nowBtn.Click += (_, _) =>
        {
            timer.Stop();
            w.Close();
            execute();
        };

        w.Closed += (_, _) => timer.Stop();
        timer.Start();
        w.Show();
    }

    public static void VerifyChecksumDialog(Window owner, string? filePath = null, string? expectedHash = null)
    {
        var f = new Form(owner, "Verify File Checksum", 580);
        f.Note("Calculate cryptographic hashes (MD5, SHA-1, SHA-256, SHA-512) and compare against an expected checksum.");

        var pathBox = new TextBox { Text = filePath ?? "", Padding = new Thickness(4, 3, 4, 3) };
        var browseBtn = new Button { Content = "Browse…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        var pathRow = new DockPanel();
        DockPanel.SetDock(browseBtn, Dock.Right);
        pathRow.Children.Add(browseBtn);
        pathRow.Children.Add(pathBox);
        f.Field("File path", pathRow);

        browseBtn.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Select file to verify" };
            if (d.ShowDialog(f.Window) == true)
            {
                pathBox.Text = d.FileName;
            }
        };

        TextBox HashRow(string label, out Button copyBtn)
        {
            var t = new TextBox { IsReadOnly = true, Padding = new Thickness(4, 3, 4, 3), FontFamily = new FontFamily("Consolas") };
            var b = new Button { Content = "Copy", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            b.Click += (_, _) => { if (!string.IsNullOrEmpty(t.Text)) Clipboard.SetText(t.Text); };
            var dock = new DockPanel();
            DockPanel.SetDock(b, Dock.Right);
            dock.Children.Add(b);
            dock.Children.Add(t);
            f.Field(label, dock);
            copyBtn = b;
            return t;
        }

        var md5Box = HashRow("MD5", out _);
        var sha1Box = HashRow("SHA-1", out _);
        var sha256Box = HashRow("SHA-256", out _);
        var sha512Box = HashRow("SHA-512", out _);

        var expectedBox = f.Text("Expected Checksum (from website or clipboard)", expectedHash ?? "");
        var statusBlock = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        f.Body.Children.Add(statusBlock);

        void UpdateComparison()
        {
            var exp = expectedBox.Text.Trim();
            if (string.IsNullOrEmpty(exp))
            {
                statusBlock.Text = "Paste or type an expected checksum to compare.";
                statusBlock.Foreground = Muted;
                return;
            }
            var parsed = Nexonic.Core.Checksum.Parse(exp);
            if (parsed == null)
            {
                statusBlock.Text = "Not a recognized hash format (expected 32, 40, 64 or 128 hex characters).";
                statusBlock.Foreground = Brushes.Firebrick;
                return;
            }
            var (algo, hex) = parsed.Value;
            var actual = algo switch
            {
                ChecksumAlgorithm.Md5 => md5Box.Text,
                ChecksumAlgorithm.Sha1 => sha1Box.Text,
                ChecksumAlgorithm.Sha256 => sha256Box.Text,
                _ => sha512Box.Text,
            };
            if (string.IsNullOrEmpty(actual) || actual == "Calculating…")
            {
                statusBlock.Text = "Waiting for hash calculation to finish…";
                statusBlock.Foreground = Muted;
                return;
            }
            if (string.Equals(actual, hex, StringComparison.OrdinalIgnoreCase))
            {
                statusBlock.Text = $"✔ MATCH: {Nexonic.Core.Checksum.Name(algo)} matches expected checksum!";
                statusBlock.Foreground = Brushes.SeaGreen;
            }
            else
            {
                statusBlock.Text = $"✖ MISMATCH: {Nexonic.Core.Checksum.Name(algo)} does not match.";
                statusBlock.Foreground = Brushes.Firebrick;
            }
        }

        async void CalculateHashes(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                md5Box.Text = ""; sha1Box.Text = ""; sha256Box.Text = ""; sha512Box.Text = "";
                statusBlock.Text = "Select a valid file to verify.";
                statusBlock.Foreground = Muted;
                return;
            }
            md5Box.Text = "Calculating…"; sha1Box.Text = "Calculating…"; sha256Box.Text = "Calculating…"; sha512Box.Text = "Calculating…";
            statusBlock.Text = "Calculating hashes…";
            statusBlock.Foreground = Muted;
            try
            {
                var hashes = await Task.Run(() => Nexonic.Core.Checksum.ComputeAll(path));
                md5Box.Text = hashes.Md5;
                sha1Box.Text = hashes.Sha1;
                sha256Box.Text = hashes.Sha256;
                sha512Box.Text = hashes.Sha512;
                UpdateComparison();
            }
            catch (Exception ex)
            {
                md5Box.Text = "Error"; sha1Box.Text = "Error"; sha256Box.Text = "Error"; sha512Box.Text = "Error";
                statusBlock.Text = "Could not read file: " + ex.Message;
                statusBlock.Foreground = Brushes.Firebrick;
            }
        }

        pathBox.TextChanged += (_, _) => CalculateHashes(pathBox.Text.Trim());
        expectedBox.TextChanged += (_, _) => UpdateComparison();

        f.AddButton("Close", f.Close, isCancel: true);
        if (!string.IsNullOrEmpty(pathBox.Text)) CalculateHashes(pathBox.Text.Trim());
        f.Show();
    }
}

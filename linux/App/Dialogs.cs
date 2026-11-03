using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>The app's small windows, the same as the Windows app's, built in code so each one reads top to bottom.</summary>
public static class Dialogs
{
    // ---- Add ----

    public static async Task Add(Window owner, DownloadManager m, string links, string queueId)
    {
        var f = new Form(owner, "Add Download", 580);
        f.Note("Paste one or more links, one per line.");
        var text = f.Text("Links", links, multi: true);
        var name = f.Text("File name (one link only; empty = from the link)");
        var folder = f.Folder(m.Settings.Folder);
        var queue = f.Queue(m, queueId);
        var audio = f.Check("Sound only (for YouTube and other video sites)", false);
        f.Note("Video sites are saved in the best quality available, with picture and sound joined.");
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
        var limit = Inner("Speed limit in KB/s (0 = none)");
        f.Body.Children.Add(new Expander { Header = "Login, checksum and speed limit", Content = inner, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch });
        var error = f.Error();
        void Go(bool start)
        {
            if ((sum.Text ?? "").Trim().Length > 0 && Checksum.Parse(sum.Text!) == null) { error.Text = "The checksum is not a valid value."; return; }
            var n = m.AddLinks(text.Text ?? "", new AddOptions
            {
                Folder = (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : m.Settings.Folder,
                FileName = (name.Text ?? "").Trim(),
                QueueId = Form.QueueId(queue),
                Start = start,
                Username = (user.Text ?? "").Trim(),
                Password = pass.Text ?? "",
                Checksum = (sum.Text ?? "").Trim(),
                SpeedLimitKBps = Ui.Number(limit.Text),
                AudioOnly = audio.IsChecked == true,
            });
            if (n == 0) { error.Text = "Paste at least one http or https link."; return; }
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
        f.Note("Put a range in square brackets: [001-100] keeps three digits, [1-100] does not, [a-z] uses letters and [0-100:5] counts in fives. Several ranges multiply.");
        var pattern = f.Text("Pattern", "https://example.com/photos/img[001-050].jpg");
        pattern.FontFamily = new FontFamily("monospace");
        var preview = f.Note("");
        void Update()
        {
            var p = pattern.Text ?? "";
            var n = BatchPattern.Count(p);
            if (n > BatchPattern.Limit) { preview.Text = $"{n} links – the limit is {BatchPattern.Limit}"; return; }
            var all = BatchPattern.Expand(p);
            preview.Text = n > 1 ? $"{n} links\nFirst: {all[0]}\nLast: {all[^1]}" : $"{n} link";
        }
        pattern.TextChanged += (_, _) => Update();
        Update();
        var folder = f.Folder(m.Settings.Folder);
        var queue = f.Queue(m, queueId);
        void Go(bool start)
        {
            var p = pattern.Text ?? "";
            if (BatchPattern.Count(p) > BatchPattern.Limit) return;
            m.AddLinks(string.Join("\n", BatchPattern.Expand(p)), new AddOptions
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
            foreach (var e in new Control[] { start, startTime, days, stop, stopTime }) editor.Children.Add(e);

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
        void Heading(StackPanel p, string text) => p.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 16, 0, 0) });

        var general = Tab("General");
        var conn = Box(general, "Connections per download (1-32)", s.Connections.ToString());
        var active = Box(general, "Downloads at the same time (main queue)", s.MaxActive.ToString());
        general.Children.Add(new TextBlock { Text = "Save to folder", Margin = new Thickness(0, 8, 0, 3) });
        var (folderRow, folder) = Form.FolderRow(f.Window, s.Folder);
        general.Children.Add(folderRow);
        var sort = Tick(general, "Put each type in its own folder (Video, Music, Documents, Programs, Compressed, Images)", s.SortIntoCategoryFolders);
        var notify = Tick(general, "Notifications", s.Notifications);
        var updates = Tick(general, "Check for updates when the app opens", s.CheckForUpdates);
        general.Children.Add(new TextBlock { Text = "Appearance", Margin = new Thickness(0, 10, 0, 3) });
        var theme = new ComboBox { ItemsSource = new[] { "Same as the system", "Light", "Dark" }, SelectedIndex = prefs.Theme switch { "light" => 1, "dark" => 2, _ => 0 }, MinWidth = 220 };
        general.Children.Add(theme);
        Heading(general, "Browser downloads");
        var askDialog = Tick(general, "Show the download window when a link is clicked in the browser", s.ShowDownloadDialog);
        var progressWin = Tick(general, "Show a progress window for each download I start", s.ShowProgressWindow);
        var toFront = Tick(general, "Bring the main window to the front for browser downloads", s.BringToFrontOnBrowserDownload);
        Hint(general, "With the download window off, downloads start right away. Closing the main window keeps the app running in the tray " +
                      "(on GNOME the tray needs the AppIndicator extension); opening the app again from the menu brings the window back.");

        var connection = Tab("Connection");
        var limit = Box(connection, "Speed limit for all downloads in KB/s (0 = none)", s.SpeedLimitKBps.ToString());
        connection.Children.Add(new TextBlock { Text = "Proxy", Margin = new Thickness(0, 12, 0, 3) });
        var mode = new ComboBox { ItemsSource = Enum.GetValues<ProxyMode>().Select(ProxySettings.Title).ToList(), SelectedIndex = (int)s.Proxy.Mode, MinWidth = 220 };
        connection.Children.Add(mode);
        var host = Box(connection, "Proxy address", s.Proxy.Host);
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
        var checkResult = Hint(connection, "");
        var check = new Button { Content = "Check the Connection", Margin = new Thickness(0, 8, 0, 0) };
        check.Click += async (_, _) =>
        {
            checkResult.Text = "Checking…";
            var keep = Network.Options;
            Network.Options = new NetworkOptions { ConnectTimeoutSeconds = Ui.Number(connectTimeout.Text, 30), DnsOverHttps = doh.Text ?? "" }.Clamped();
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

        var clip = Tab("Clipboard");
        var watch = Tick(clip, "Watch the clipboard for copied download links", s.MonitorClipboard);
        var clipTypes = Box(clip, "File types", s.ClipboardTypes);
        clipTypes.TextWrapping = TextWrapping.Wrap;
        Hint(clip, "When you copy a link that ends in one of these types, the Add window opens with it.");

        var after = Tab("After Download");
        var complete = Tick(after, "Show the Download Complete window", s.ShowCompleteDialog);
        var sound = Tick(after, "Play a sound", s.PlaySound);
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
            s.Connections = Math.Clamp(Ui.Number(conn.Text, 8), 1, 32);
            s.MaxActive = Math.Clamp(Ui.Number(active.Text, 3), 1, 10);
            s.Folder = (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : s.Folder;
            s.SortIntoCategoryFolders = sort.IsChecked == true;
            s.Notifications = notify.IsChecked == true;
            s.CheckForUpdates = updates.IsChecked == true;
            s.ShowDownloadDialog = askDialog.IsChecked == true;
            s.ShowProgressWindow = progressWin.IsChecked == true;
            s.BringToFrontOnBrowserDownload = toFront.IsChecked == true;
            s.SpeedLimitKBps = Ui.Number(limit.Text);
            s.Proxy = new ProxySettings
            {
                Mode = (ProxyMode)Math.Max(0, mode.SelectedIndex), Host = (host.Text ?? "").Trim(), Port = p,
                Username = (puser.Text ?? "").Trim(), Password = ppass.Text ?? "",
            };
            s.MonitorClipboard = watch.IsChecked == true;
            s.ClipboardTypes = clipTypes.Text ?? "";
            s.ShowCompleteDialog = complete.IsChecked == true;
            s.PlaySound = sound.IsChecked == true;
            s.MarkDownloads = mark.IsChecked == true;
            s.ScanEnabled = scan.IsChecked == true;
            s.ScanCommand = (scanCmd.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(doh.Text) && !NetworkOptions.DnsOverHttpsUrlOk(doh.Text)) { error.Text = "The DNS over HTTPS server must be an https:// address."; return; }
            s.Network = new NetworkOptions
            {
                MinSegmentKB = Ui.Number(minSeg.Text, 512), Retries = Ui.Number(retries.Text, 6),
                ConnectTimeoutSeconds = Ui.Number(connectTimeout.Text, 30), ReadTimeoutSeconds = Ui.Number(readTimeout.Text, 60), DnsOverHttps = doh.Text ?? "",
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
            prefs.Theme = theme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            prefs.Save();
            App.ApplyTheme(prefs);
            f.Close();
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
        var conns = f.Text("Connections per download (1-32)", p.MaxConnections?.ToString() ?? "");
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
                MaxConnections = Optional(conns.Text, 1, 32), SpeedLimitKBps = Optional(limit.Text, 0, 10_000_000),
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
        var (link, count) = ShareLinks.Link(urls);
        if (count == 0) return;
        var f = new Form(owner, "Download on Your Phone", 400);
        f.Body.Children.Add(new QrView(QrCode.Encode(link)) { Width = 300, Height = 300, Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Center });
        f.Note(count == 1 ? "Scan with your phone's camera to download this file with the Nexonic app on your phone."
                          : $"Scan with your phone's camera to download these {count} files with the Nexonic app on your phone.");
        if (count < urls.Count) f.Note($"Only the first {count} of {urls.Count} links fit in one code.");
        f.Note("Only the links are shared: no cookies or passwords. Downloads that need you to be signed in may not work on the phone.");
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

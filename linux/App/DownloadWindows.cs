using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>
/// The download window (shown when a link is clicked in the browser) and one progress window per download,
/// built on the same manager as the main window, like the Windows app's.
/// </summary>
public static class DownloadWindows
{
    static readonly Dictionary<string, Window> infoWindows = new();   // by link: one link never gets two windows
    static readonly Dictionary<Guid, Window> progressWindows = new();

    static TextBlock Caption(string text) => new() { Text = text, Opacity = 0.7, Margin = new Thickness(0, 4, 16, 4), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Opens each new window a little below and to the right of the last one, so none hides another.</summary>
    static void Place(Window w, IEnumerable<Window> others)
    {
        if (others.LastOrDefault(o => o.IsVisible) is { } last)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint(last.Position.X + 28, last.Position.Y + 28);
        }
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    // ---- Download File Info ----

    public static void ShowInfo(DownloadManager m, DownloadJob job)
    {
        job = job with { Url = Links.Unwrap(job.Url) };  // e.g. a Google Images result: the picture itself
        if (infoWindows.TryGetValue(job.Url, out var open)) { open.Activate(); return; }
        var uri = new Uri(job.Url);
        var site = job.Site == true || Nexonic.Core.SiteVideo.IsSupported(job.Url);
        var w = new Window
        {
            Title = "Download File Info", Width = 580, SizeToContent = SizeToContent.Height, CanResize = false, Icon = Ui.WindowIcon,
            Topmost = true, ShowActivated = true, ShowInTaskbar = true,
        };
        Place(w, infoWindows.Values);
        var root = new StackPanel { Margin = new Thickness(20) };

        var head = new DockPanel();
        var name = new TextBox
        {
            Text = job.FileName is { Length: > 0 } n ? FileNames.Sanitize(n) : site ? $"Video from {uri.Host}" : FileNames.FromUrl(job.Url),
            FontSize = 15, FontWeight = FontWeight.SemiBold,
        };
        var badgeHost = new Border { Margin = new Thickness(0, 0, 12, 0), Child = Ui.FileBadge(name.Text ?? "", 40), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(badgeHost, Dock.Left);
        head.Children.Add(badgeHost);
        var titles = new StackPanel();
        titles.Children.Add(name);
        var link = new TextBlock { Text = job.Url, Opacity = 0.7, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 4, 0, 0) };
        ToolTip.SetTip(link, job.Url);
        titles.Children.Add(link);
        head.Children.Add(titles);
        root.Children.Add(head);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 14, 0, 0) };
        var rowIndex = 0;
        void AddRow(string label, Control value)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Caption(label);
            Grid.SetRow(l, rowIndex);
            Grid.SetRow(value, rowIndex);
            Grid.SetColumn(value, 1);
            grid.Children.Add(l);
            grid.Children.Add(value);
            rowIndex++;
        }
        var size = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = job.Size is long s ? Row.Bytes(s) : site ? "Finding the qualities on offer…" : "Checking…" };
        AddRow("Size", size);
        var category = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = FileCategories.Of(name.Text ?? "").ToString() };
        AddRow("Category", category);
        name.TextChanged += (_, _) =>
        {
            category.Text = FileCategories.Of(name.Text ?? "").ToString();
            badgeHost.Child = Ui.FileBadge(name.Text ?? "", 40);
        };
        var (folderRow, folder) = Form.FolderRow(w, m.Settings.Folder);
        folderRow.Margin = new Thickness(0, 3);
        AddRow("Save to", folderRow);
        var queue = Form.QueueBox(m, job.Queue is { Length: > 0 } qn ? m.Queues.FirstOrDefault(q => string.Equals(q.Name, qn, StringComparison.OrdinalIgnoreCase))?.Id ?? DownloadQueue.MainId : DownloadQueue.MainId);
        queue.Margin = new Thickness(0, 3);
        queue.HorizontalAlignment = HorizontalAlignment.Left;
        AddRow("Queue", queue);
        // Quality: 0 = best, -1 = sound only, otherwise the highest picture height. Filled in when the video is looked up.
        var quality = new ComboBox { Margin = new Thickness(0, 3), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220 };
        var qualityTags = new List<int>();
        SiteVideo.Choices? offered = null;
        long? bestSize = null;
        var looked = false;
        int Chosen() => quality.SelectedIndex >= 0 && quality.SelectedIndex < qualityTags.Count ? qualityTags[quality.SelectedIndex] : 0;
        void ShowSize()
        {
            if (!looked) return;
            var q = Chosen();
            long? known = q == 0 ? bestSize : q == -1 ? offered?.SoundSize : offered?.Video.FirstOrDefault(v => v.Height == q)?.Size;
            size.Text = known is long bytes ? Row.Bytes(bytes) : "Size known when the download starts";
        }
        void FillQualities(int keep)
        {
            var first = offered?.Video.FirstOrDefault();
            if (keep > 0 && keep == first?.Height) keep = 0;  // that is the best on offer
            var lower = offered?.Video.Skip(1).Select(q => q.Height).ToList() ?? new List<int>();
            if (keep > 0 && !lower.Contains(keep)) lower.Add(keep);
            var labels = new List<string> { first != null ? $"Best ({SiteVideo.QualityName(first.Height)})" : "Best available" };
            qualityTags = new List<int> { 0 };
            foreach (var h in lower.OrderByDescending(x => x)) { labels.Add(SiteVideo.QualityName(h)); qualityTags.Add(h); }
            labels.Add("Sound only");
            qualityTags.Add(-1);
            quality.ItemsSource = labels;
            quality.SelectedIndex = Math.Max(0, qualityTags.IndexOf(keep));
        }
        FillQualities(job.AudioOnly == true ? -1 : job.MaxHeight ?? 0);
        quality.SelectionChanged += (_, _) => ShowSize();
        if (site) AddRow("Quality", quality);
        root.Children.Add(grid);

        var never = new CheckBox { Content = "Don't show this window; start browser downloads right away", Margin = new Thickness(0, 12, 0, 0), FontSize = 12 };
        root.Children.Add(never);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        string? infoPath = null;
        void Done(bool? start)
        {
            if (never.IsChecked == true)
            {
                var st = m.Settings;
                st.ShowDownloadDialog = false;
                m.ApplySettings(st);
            }
            if (start is bool go)
            {
                var o = new AddOptions
                {
                    Folder = (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : null,
                    QueueId = Form.QueueId(queue),
                    Start = go,
                    AudioOnly = Chosen() == -1,
                };
                var id = m.Add(job with
                {
                    FileName = (name.Text ?? "").Trim().Length > 0 ? name.Text!.Trim() : null, Start = go, InfoJson = infoPath, Queue = null,
                    AudioOnly = Chosen() == -1, MaxHeight = Chosen() > 0 ? Chosen() : null,
                }, o);
                if (go && id is Guid g && m.Settings.ShowProgressWindow && m.Rows.FirstOrDefault(r => r.Record.Id == g) is { } row)
                    ShowProgress(m, row);
            }
            w.Close();
        }
        buttons.Children.Add(Ui.Button("Cancel", () => Done(null), isCancel: true));
        buttons.Children.Add(Ui.Button("Download Later", () => Done(false)));
        buttons.Children.Add(Ui.Button("Start Download", () => Done(true), isDefault: true));
        root.Children.Add(buttons);
        w.Content = root;
        infoWindows[job.Url] = w;
        w.Closed += (_, _) => infoWindows.Remove(job.Url);
        w.Show();
        w.Activate();

        if (site) _ = LookUpVideoAsync(job, size, name, (path, found, total) =>
        {
            infoPath = path;
            offered = found;
            bestSize = total ?? found?.Video.FirstOrDefault()?.Size;  // estimated from the bitrate when not given
            looked = true;
            FillQualities(Chosen());
            ShowSize();
        }, m.Settings.Proxy);
        else if (job.Size is null && job.Stream != true) _ = ProbeAsync(m, job, size, name);
    }

    /// <summary>
    /// Looks the video up while the window is open (title, size, best quality). The details are saved and reused, so the
    /// download starts at once instead of looking the video up again.
    /// </summary>
    static async Task LookUpVideoAsync(DownloadJob job, TextBlock size, TextBox name, Action<string, SiteVideo.Choices?, long?> found,
        ProxySettings proxy)
    {
        size.Text = "Finding the qualities on offer…";
        var path = Path.Combine(AppPaths.Parts, $"info-{Guid.NewGuid():N}.json");
        var result = await Task.Run(() =>
        {
            try
            {
                SiteVideoTools.Prepare();
                if (!File.Exists(SiteVideoTools.YtDlp)) return ((SiteVideo.Info?)null, "");
                Directory.CreateDirectory(AppPaths.Parts);
                var psi = new ProcessStartInfo(SiteVideoTools.YtDlp)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                };
                psi.Environment["PYTHONIOENCODING"] = "utf-8";
                var (args, configFile) = SiteVideo.Prepare(
                    SiteVideo.InfoArguments(job.Url, SiteVideoTools.QuickJs, false, job.Referrer, job.Cookies, job.UserAgent, null, null), proxy);
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var cleanup = new ConfigCleanup(configFile);
                using var p = Process.Start(psi)!;
                p.ErrorDataReceived += (_, _) => { };
                p.BeginErrorReadLine();
                var json = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                var info = p.ExitCode == 0 ? SiteVideo.ParseInfo(json) : null;
                if (info != null) File.WriteAllText(path, json);
                return (info, json);
            }
            catch { return ((SiteVideo.Info?)null, ""); }
        });
        if (result.Item1 is not { } info)
        {
            size.Text = "Size known when the download starts";
            return;
        }
        found(path, SiteVideo.ParseChoices(result.Item2), info.Size);
        if (string.IsNullOrEmpty(job.FileName)) name.Text = FileNames.Sanitize($"{info.Title}.{info.Ext}");
    }

    /// <summary>Asks the server for the size (and the real file name) without downloading the file.</summary>
    static async Task ProbeAsync(DownloadManager m, DownloadJob job, TextBlock size, TextBox name)
    {
        try
        {
            var handler = Network.Handler(m.Settings.Proxy);
            handler.AllowAutoRedirect = true;
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var req = new HttpRequestMessage(HttpMethod.Head, job.Url);
            if (!string.IsNullOrEmpty(job.Cookies)) req.Headers.TryAddWithoutValidation("Cookie", job.Cookies);
            if (!string.IsNullOrEmpty(job.UserAgent)) req.Headers.TryAddWithoutValidation("User-Agent", job.UserAgent);
            if (!string.IsNullOrEmpty(job.Referrer)) req.Headers.TryAddWithoutValidation("Referer", job.Referrer);
            using var res = await http.SendAsync(req);
            var length = res.Content.Headers.ContentLength;
            size.Text = length is > 0 ? Row.Bytes(length.Value) : "Unknown";
            var suggested = res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName;
            if (string.IsNullOrEmpty(job.FileName) && !string.IsNullOrWhiteSpace(suggested))
                name.Text = FileNames.Sanitize(suggested.Trim('"'));
        }
        catch { size.Text = "Unknown"; }
    }

    // ---- Progress window ----

    public static void ShowProgress(DownloadManager m, Row row)
    {
        var id = row.Record.Id;
        if (progressWindows.TryGetValue(id, out var open))
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var w = new Window { Width = 600, SizeToContent = SizeToContent.Height, CanResize = false, ShowActivated = false, Icon = Ui.WindowIcon };
        Place(w, progressWindows.Values);
        var root = new StackPanel { Margin = new Thickness(18) };

        var head = new DockPanel();
        var badge = Ui.FileBadge(row.Record.FileName, 36);
        badge.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        var titles = new StackPanel();
        var fileName = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        titles.Children.Add(fileName);
        var link = new TextBlock { Text = row.Record.Url, Opacity = 0.7, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(link, row.Record.Url);
        titles.Children.Add(link);
        head.Children.Add(titles);
        root.Children.Add(head);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 12, 0, 0) };
        var values = new List<TextBlock>();
        foreach (var label in new[] { "Status", "File size", "Downloaded", "Transfer rate", "Time left", "Resume capability" })
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Caption(label);
            var v = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4) };
            Grid.SetRow(l, values.Count);
            Grid.SetRow(v, values.Count);
            Grid.SetColumn(v, 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
            values.Add(v);
        }
        root.Children.Add(grid);

        var bar = new ProgressBar { Height = 14, Margin = new Thickness(0, 12, 0, 0), Minimum = 0, Maximum = 100, Foreground = Ui.Good };
        root.Children.Add(bar);
        var graph = new SpeedGraph { Height = 46, Margin = new Thickness(0, 10, 0, 0) };
        root.Children.Add(graph);

        var details = new StackPanel();
        var segCaption = Ui.Muted("Start positions and progress by connection", 11);
        segCaption.Margin = new Thickness(0, 10, 0, 4);
        var segments = new SegmentBar { Height = 12 };
        var connHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("50,140,*"), Margin = new Thickness(0, 8, 0, 2) };
        foreach (var (t, i) in new[] { ("N.", 0), ("Downloaded", 1), ("Info", 2) })
        {
            var h = new TextBlock { Text = t, FontWeight = FontWeight.SemiBold, FontSize = 12, Opacity = 0.8 };
            Grid.SetColumn(h, i);
            connHeader.Children.Add(h);
        }
        var conns = new StackPanel();
        var connBox = new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2),
            Child = new ScrollViewer { Content = conns, MaxHeight = 150 },
        };
        details.Children.Add(segCaption);
        details.Children.Add(segments);
        details.Children.Add(connHeader);
        details.Children.Add(connBox);
        root.Children.Add(details);

        var error = new TextBlock { Foreground = Ui.Bad, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        root.Children.Add(error);

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        var toggle = new Button { Content = "<< Hide details" };
        toggle.Click += (_, _) =>
        {
            details.IsVisible = !details.IsVisible;
            toggle.Content = details.IsVisible ? "<< Hide details" : "Show details >>";
        };
        DockPanel.SetDock(toggle, Dock.Left);
        buttons.Children.Add(toggle);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(right, Dock.Right);
        var pause = Ui.Button("Pause", () =>
        {
            if (row.Record.IsActive || row.Record.Status == DownloadStatus.Queued) m.Pause(row); else m.Resume(row);
        });
        right.Children.Add(pause);
        right.Children.Add(Ui.Button("Cancel", async () =>
        {
            if (!await Ui.Confirm(w, "Cancel this download?", $"{row.Record.FileName} is removed from the list and the part already downloaded is deleted.", "Cancel Download", "Keep")) return;
            w.Close();
            m.Remove(row);
        }));
        right.Children.Add(Ui.Button("Close", w.Close, isCancel: true));
        buttons.Children.Add(right);
        root.Children.Add(buttons);
        w.Content = root;

        var lastSegments = "";
        void ConnRows(IEnumerable<(string N, string Downloaded, string Info)> items)
        {
            conns.Children.Clear();
            foreach (var (n, d, info) in items)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("50,140,*"), Margin = new Thickness(0, 2) };
                var a = new TextBlock { Text = n, FontSize = 12 };
                var b = new TextBlock { Text = d, FontSize = 12 };
                var c = new TextBlock { Text = info, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(b, 1);
                Grid.SetColumn(c, 2);
                g.Children.Add(a);
                g.Children.Add(b);
                g.Children.Add(c);
                conns.Children.Add(g);
            }
        }
        void Update()
        {
            var r = row.Record;
            if (!m.Rows.Contains(row)) { w.Close(); return; }
            if (r.Status == DownloadStatus.Finished) { w.Close(); return; }
            var pct = r.Progress * 100;
            w.Title = $"{(int)pct}% {r.FileName}";
            fileName.Text = r.FileName;
            values[0].Text = row.PreparingVideo ? "Preparing video… (finding the best quality)"
                : r.IsActive && r.VideoStep is { } videoStep ? videoStep : r.Status switch
            {
                DownloadStatus.Queued => "Waiting in queue",
                DownloadStatus.Probing => "Connecting…",
                DownloadStatus.Downloading => "Receiving data…",
                DownloadStatus.Paused => "Paused",
                DownloadStatus.Failed => "Failed",
                _ => "Complete",
            };
            values[1].Text = r.Size is long s ? Row.Bytes(s) : "Unknown";
            values[2].Text = Row.Bytes(r.Received) + (r.Size is > 0 ? $" ({pct:0.0} %)" : "");
            values[3].Text = r.IsActive && row.SpeedBytes > 0 ? row.Speed : "–";
            values[4].Text = row.TimeLeft.Length > 0 ? row.TimeLeft : "–";
            values[5].Text = r.AcceptsRanges || r.SiteVideo == true || r.IsStream == true ? "Yes" : "No";
            bar.IsIndeterminate = r.IsActive && r.Size is null && r.IsStream != true;
            bar.Value = pct;
            pause.Content = r.IsActive || r.Status == DownloadStatus.Queued ? "Pause" : "Resume";
            error.Text = r.Error ?? "";
            error.IsVisible = !string.IsNullOrEmpty(r.Error);
            graph.Add(r.IsActive ? row.SpeedBytes : 0);

            var plain = r.IsStream != true && r.SiteVideo != true && r.Segments.Count > 0;
            segments.IsVisible = plain;
            connHeader.IsVisible = connBox.IsVisible = plain || r.SiteVideo == true;
            if (r.SiteVideo == true)
            {
                // The picture and sound pieces, like the connection list of a normal download.
                var pieces = r.VideoParts?.ToList() ?? new List<VideoPart> { new() { Name = "Video", Done = r.Received } };
                var videoKey = string.Join(",", pieces.Select(x => x.Done)) + r.Status + r.VideoStep;
                if (videoKey != lastSegments)
                {
                    lastSegments = videoKey;
                    ConnRows(pieces.Select((pc, i) => (
                        (i + 1).ToString(),
                        Row.Bytes(pc.Done) + (pc.Total is long t ? " of " + Row.Bytes(t) : ""),
                        pc.Name + " · " + (pc.Total is long tt && pc.Done >= tt ? "Complete"
                            : r.Status == DownloadStatus.Downloading ? r.VideoStep ?? "Receiving data…"
                            : r.Status == DownloadStatus.Probing ? "Connecting…" : "Waiting"))));
                }
            }
            if (!plain)
            {
                segCaption.Text = r.SiteVideo == true
                    ? ("Video site · " + SiteVideo.ChoiceText(r.AudioOnly, r.MaxHeight) + ", picture and sound joined") + $"\nSave to: {r.Folder}"
                    : r.IsStream == true ? $"Streamed video · pieces: {r.StreamDone ?? 0} of {r.StreamTotal ?? 0}, joined into one .mp4" : "";
                segCaption.IsVisible = segCaption.Text.Length > 0;
                return;
            }
            segCaption.Text = "Start positions and progress by connection";
            segCaption.IsVisible = true;
            segments.Update(r);
            List<Segment> segs;
            lock (r.Segments) segs = r.Segments.Select(x => new Segment { Id = x.Id, Start = x.Start, End = x.End, Written = x.Written }).ToList();
            var key = string.Join(",", segs.Select(x => x.Written)) + r.Status;
            if (key == lastSegments) return;
            lastSegments = key;
            ConnRows(segs.Select(sg => (
                (sg.Id + 1).ToString(),
                Row.Bytes(sg.Written),
                sg.IsComplete ? "Complete" : r.Status == DownloadStatus.Downloading ? "Receiving data…" : r.Status == DownloadStatus.Probing ? "Connecting…" : "Waiting")));
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Update();
        progressWindows[id] = w;
        w.Closed += (_, _) => { timer.Stop(); progressWindows.Remove(id); };
        Update();
        if (!progressWindows.ContainsKey(id)) return;   // finished or removed already
        w.Show();
        timer.Start();
    }
}

/// <summary>The speed over the last minute, as a filled line.</summary>
public sealed class SpeedGraph : Control
{
    readonly List<double> history = new();

    public void Add(double bytesPerSecond)
    {
        history.Add(bytesPerSecond);
        if (history.Count > 120) history.RemoveAt(0);
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        dc.DrawRectangle(Ui.Shade, null, new Rect(0, 0, w, h), 3, 3);
        var top = history.Count > 0 ? history.Max() : 0;
        if (top <= 0 || history.Count < 2) return;
        var step = w / (history.Count - 1);
        var points = history.Select((v, i) => new Point(i * step, h - v / top * (h - 4))).ToList();
        var area = new StreamGeometry();
        using (var c = area.Open())
        {
            c.BeginFigure(new Point(0, h), true);
            foreach (var p in points) c.LineTo(p);
            c.LineTo(new Point(points[^1].X, h));
            c.EndFigure(true);
        }
        dc.DrawGeometry(Ui.Brush("#301F6FEB"), null, area);
        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(points[0], false);
            foreach (var p in points.Skip(1)) c.LineTo(p);
            c.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(Ui.Accent, 1.5), line);
    }
}

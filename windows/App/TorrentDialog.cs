using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nexonic.Core;

namespace Nexonic.App;

public static partial class Dialogs
{
    sealed class TorrentSelection : ScrollViewer
    {
        readonly StackPanel body = new();
        readonly List<ComboBox> priorities = new();
        public int[] Priorities => priorities.Select(p => new[] { 0, 1, 4, 7 }[p.SelectedIndex]).ToArray();
        public TorrentSelection() { Content = body; Height = 200; VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
        public void Load(TorrentMetadata? metadata)
        {
            body.Children.Clear(); priorities.Clear();
            if (metadata == null) return;
            var values = metadata.Priorities();
            for (int i = 0; i < metadata.Files.Count; i++)
            {
                var file = metadata.Files[i];
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var priority = new ComboBox { ItemsSource = new[] { "Skip", "Low", "Normal", "High" },
                    SelectedIndex = Array.IndexOf(new[] { 0, 1, 4, 7 }, values[i]), Width = 90, IsEnabled = !file.Pad };
                priorities.Add(priority); DockPanel.SetDock(priority, Dock.Right); row.Children.Add(priority);
                row.Children.Add(new TextBlock { Text = $"{file.Path} ({Row.Bytes(file.Size)})", TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = file.Path });
                body.Children.Add(row);
            }
        }
    }
    sealed class TorrentOptionsForm
    {
        readonly CheckBox seed, dht;
        readonly TextBox ratio, duration, download, upload, trackers;
        public TorrentOptionsForm(Form form, TorrentMetadata torrent, int limit)
        {
            download = form.Text("Download limit (KiB/s; 0 = unlimited)", limit.ToString());
            upload = form.Text("Upload limit (KiB/s; 0 = unlimited)", torrent.UploadLimitKBps.ToString());
            seed = form.Check("Seed verified files after downloading", torrent.Seed);
            ratio = form.Text("Stop seeding at ratio (0 = no ratio limit)", torrent.RatioLimit.ToString(CultureInfo.InvariantCulture));
            duration = form.Text("Stop seeding after seconds (0 = no time limit)", torrent.SeedSeconds.ToString());
            dht = form.Check("Use DHT (private torrents always disable DHT)", torrent.Dht);
            trackers = form.Text("Trackers — one HTTP(S) or UDP address per line", string.Join("\n", torrent.Trackers), true);
        }
        public int Save(TorrentMetadata torrent)
        {
            if (!double.TryParse(ratio.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 0)
                throw new ArgumentException("Enter a nonnegative seeding ratio.");
            int Limit(TextBox field) => int.TryParse(field.Text, out var n) && n is >= 0 and <= 2000000 ? n : throw new ArgumentException("Enter a limit between 0 and 2000000.");
            var addresses = trackers.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            if (addresses.Count > 128 || addresses.Any(a => a.Length > 4096 || !Uri.TryCreate(a, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https" or "udp")))
                throw new ArgumentException("Use HTTP, HTTPS or UDP tracker addresses (maximum 128).");
            var down = Limit(download); var up = Limit(upload); var seconds = Limit(duration);
            torrent.Seed = seed.IsChecked == true; torrent.Dht = dht.IsChecked == true; torrent.RatioLimit = value;
            torrent.UploadLimitKBps = up; torrent.SeedSeconds = seconds; torrent.Trackers = addresses;
            return down;
        }
    }
    public static void Torrent(Window owner, DownloadManager manager, string source, string queueId)
    {
        var f = new Form(owner, "Add Torrent", 700);
        f.Note("Torrent peers and trackers can see your IP address. Choose material you may download and share. The native engine uses your proxy setting (HTTP or SOCKS5) and the speed limit for all downloads; DNS over HTTPS must be off. Local-network peers and web seeds are disabled.");
        var enabled = f.Check("Enable experimental torrents", manager.Settings.TorrentsEnabled);
        var input = f.Text("Magnet, HTTP(S) torrent link or local .torrent file", source);
        f.AddButton("Choose .torrent…", () =>
        {
            var pick = new Microsoft.Win32.OpenFileDialog { Filter = "Torrent files|*.torrent" };
            if (pick.ShowDialog(f.Window) == true) input.Text = pick.FileName;
        });
        var folder = f.Folder(manager.Settings.Folder);
        var queue = f.Queue(manager, queueId);
        var preview = f.Note("Load metadata before choosing files.");
        var selection = f.Field("Files and priorities", new TorrentSelection());
        var draft = new TorrentMetadata();
        var options = new TorrentOptionsForm(f, draft, 0);
        var error = f.Note("");
        using var cancel = new CancellationTokenSource();
        f.Window.Closed += (_, _) => cancel.Cancel();
        TorrentMetadata? metadata = null; string? previewSource = null; bool busy = false;
        input.TextChanged += (_, _) => { metadata = null; selection.Load(null); };
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Load metadata", async () =>
        {
            if (busy) return;
            error.Text = "";
            manager.Settings.TorrentsEnabled = enabled.IsChecked == true; manager.ApplySettings(manager.Settings);
            if (TorrentRuntime.Unavailable(manager.Settings) is { } reason) { error.Text = reason; return; }
            busy = true; var requested = input.Text.Trim(); preview.Text = "Loading metadata…";
            try
            {
                var found = await TorrentRuntime.PreviewAsync(requested, cancel.Token, manager.Settings.Proxy);
                if (requested != input.Text.Trim()) return;
                metadata = found; previewSource = requested; selection.Load(found);
                preview.Text = $"{found.Name} · {Row.Bytes(found.Size)} · {found.Files.Count} files\n{found.InfoHash}";
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { error.Text = e.Message; }
            finally { busy = false; }
        });
        void Add(bool start)
        {
            try
            {
                if (busy || metadata == null || previewSource != input.Text.Trim()) throw new ArgumentException("Load metadata before adding this torrent.");
                if (enabled.IsChecked != true) throw new ArgumentException("Enable torrents first.");
                metadata.FilePriorities = selection.Priorities.ToList(); _ = metadata.SelectedSize;
                if (!System.IO.Path.IsPathFullyQualified(folder.Text)) throw new ArgumentException("Choose an absolute destination folder.");
                var limit = options.Save(draft);
                metadata.Seed = draft.Seed; metadata.Dht = draft.Dht; metadata.RatioLimit = draft.RatioLimit; metadata.SeedSeconds = draft.SeedSeconds;
                metadata.UploadLimitKBps = draft.UploadLimitKBps;
                if (draft.Trackers.Count > 0) metadata.Trackers = draft.Trackers;
                manager.AddTorrent(previewSource, metadata, new AddOptions { Folder = folder.Text, QueueId = ((DownloadQueue)queue.SelectedItem).Id, Start = start, SpeedLimitKBps = limit });
                f.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        }
        f.AddButton("Download Later", () => Add(false)); f.AddButton("Start Download", () => Add(true)); f.Show();
    }
    public static void TorrentFiles(Window owner, DownloadManager manager, Row row)
    {
        if (row.Record.Torrent is not { MetadataPending: false } torrent) return;
        var f = new Form(owner, "Torrent Files and Settings", 700);
        f.Note(torrent.Name + "\n" + TorrentText.Summary(torrent));
        var selection = f.Field("Files and priorities", new TorrentSelection()); selection.Load(torrent);
        var options = new TorrentOptionsForm(f, torrent, row.Record.SpeedLimitKBps ?? 0);
        var error = f.Note("");
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Save and Pause", () =>
        {
            try
            {
                var draft = new TorrentMetadata(); var limit = options.Save(draft);
                manager.SetTorrentPriorities(row, selection.Priorities);
                torrent.Seed = draft.Seed; torrent.Dht = draft.Dht; torrent.RatioLimit = draft.RatioLimit;
                torrent.UploadLimitKBps = draft.UploadLimitKBps; torrent.SeedSeconds = draft.SeedSeconds; torrent.Trackers = draft.Trackers;
                row.Record.SpeedLimitKBps = limit; manager.Save(); f.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        }); f.Show();
    }
    public static void TorrentManager(Window owner, DownloadManager manager, string queueId)
    {
        var f = new Form(owner, "Torrent Manager", 900);
        f.Note("Pause and resume use verified native checkpoints. Removing a task retains downloaded files. Select a torrent to manage its files, trackers, seeding and limits.");
        var list = f.Field("Torrents", new ListBox { Height = 280, DisplayMemberPath = "Label" });
        Guid? selected = null;
        void Update()
        {
            selected = (list.SelectedItem as TorrentManagerRow)?.Row.Record.Id ?? selected;
            var rows = manager.Rows.Where(r => r.Record.Torrent != null).Select(r => new TorrentManagerRow(r)).ToList();
            list.ItemsSource = rows; list.SelectedItem = rows.FirstOrDefault(r => r.Row.Record.Id == selected);
        }
        Row? Pick() => (list.SelectedItem as TorrentManagerRow)?.Row;
        f.AddButton("Add Torrent", () => Torrent(f.Window, manager, "", queueId));
        f.AddButton("Pause", () => { if (Pick() is { } r) manager.Pause(r); });
        f.AddButton("Resume", () => { if (Pick() is { } r) manager.Resume(r); });
        f.AddButton("Files and Settings", () => { if (Pick() is { } r) TorrentFiles(f.Window, manager, r); });
        f.AddButton("Remove", () => { if (Pick() is { } r) manager.Remove(r); });
        f.AddButton("Close", f.Close);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Update();
        f.Window.Closed += (_, _) => timer.Stop(); Update(); timer.Start(); f.Show();
    }
    sealed record TorrentManagerRow(Row Row)
    {
        public string Label => $"{Row.Record.FileName} · {Row.Status} · {Row.Percent:0}%\n{TorrentText.Summary(Row.Record.Torrent!)}";
    }
}

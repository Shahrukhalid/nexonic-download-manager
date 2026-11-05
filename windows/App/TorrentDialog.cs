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
        readonly CheckBox seed, dht, sequential;
        readonly TextBox ratio, duration, download, upload, trackers, connections, slots;
        public TorrentOptionsForm(Form form, TorrentMetadata torrent, int limit)
        {
            download = form.Text("Download limit (KiB/s; 0 = unlimited)", limit.ToString());
            upload = form.Text("Upload limit (KiB/s; 0 = unlimited)", torrent.UploadLimitKBps.ToString());
            connections = form.Text("Connections per torrent (0 = no limit)", torrent.MaxConnections.ToString());
            slots = form.Text("Upload slots: peers you upload to at once (0 = no limit)", torrent.MaxUploads.ToString());
            sequential = form.Check("Download pieces in order (for watching while it downloads)", torrent.Sequential);
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
            var peers = int.TryParse(connections.Text, out var c) && (c == 0 || c is >= 2 and <= 10000) ? c : throw new ArgumentException("Connections must be 0 (no limit) or between 2 and 10000.");
            var uploads = int.TryParse(slots.Text, out var u2) && u2 is >= 0 and <= 10000 ? u2 : throw new ArgumentException("Upload slots must be between 0 (no limit) and 10000.");
            torrent.MaxConnections = peers; torrent.MaxUploads = uploads; torrent.Sequential = sequential.IsChecked == true;
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
                metadata.UploadLimitKBps = draft.UploadLimitKBps; metadata.MaxConnections = draft.MaxConnections;
                metadata.MaxUploads = draft.MaxUploads; metadata.Sequential = draft.Sequential;
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
        f.AddButton("Save", () =>
        {
            try
            {
                var draft = new TorrentMetadata(); var limit = options.Save(draft);
                manager.ApplyTorrentSettings(row, draft, limit, selection.Priorities, draft.Trackers); f.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        }, isDefault: true); f.Show();
    }
    public static void TorrentManager(Window owner, DownloadManager manager, string queueId)
    {
        var f = new Form(owner, "Torrent Manager", 900);
        f.Note("Pause and resume use verified native checkpoints. Removing a task retains downloaded files. Select a torrent to manage its files, trackers, seeding and limits.");
        var list = f.Field("Torrents", new ListBox { Height = 220, DisplayMemberPath = "Label" });
        var details = f.Note("Select a torrent to see its details.");
        details.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        Guid? selected = null;
        void Update()
        {
            selected = (list.SelectedItem as TorrentManagerRow)?.Row.Record.Id ?? selected;
            var rows = manager.Rows.Where(r => r.Record.Torrent != null).Select(r => new TorrentManagerRow(r)).ToList();
            list.ItemsSource = rows; list.SelectedItem = rows.FirstOrDefault(r => r.Row.Record.Id == selected);
            details.Text = Pick() is { } row ? TorrentText.Details(row.Record.Torrent!, manager.IsRunning(row)) : "Select a torrent to see its details.";
        }
        Row? Pick() => (list.SelectedItem as TorrentManagerRow)?.Row;
        f.AddButton("Add Torrent", () => Torrent(f.Window, manager, "", queueId));
        f.AddButton("Pause", () => { if (Pick() is { } r) manager.Pause(r); });
        f.AddButton("Resume", () => { if (Pick() is { } r) manager.Resume(r); });
        f.AddButton("Files and Settings", () => { if (Pick() is { } r) TorrentFiles(f.Window, manager, r); });
        f.AddButton("Peers", () => { if (Pick() is { } r) TorrentPeers(f.Window, manager, r); });
        f.AddButton("Trackers", () => { if (Pick() is { } r) TorrentTrackers(f.Window, manager, r); });
        f.AddButton("Network", () => TorrentNetwork(f.Window, manager));
        f.AddButton("Remove", () => { if (Pick() is { } r) manager.Remove(r); });
        f.AddButton("Close", f.Close);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Update();
        f.Window.Closed += (_, _) => timer.Stop(); Update(); timer.Start(); f.Show();
    }
    /// <summary>Connected peers, refreshed every two seconds while open.</summary>
    public static void TorrentPeers(Window owner, DownloadManager manager, Row row)
    {
        var f = new Form(owner, "Peers · " + row.Record.FileName, 860);
        f.Note("Flags: E encrypted, I incoming, P µTP, S seed. Peers appear while the torrent is running.");
        var list = f.Field("Peers", new ListBox { Height = 360, FontFamily = new System.Windows.Media.FontFamily("Consolas") });
        void Update()
        {
            var peers = manager.TorrentPeers(row);
            list.ItemsSource = peers.Count == 0 ? new[] { manager.IsRunning(row) ? "Looking for peers…" : "Start the torrent to see its peers." }
                : peers.Select(p => $"{p.Address,-39} {p.Client,-24} {p.Flags,-4} {p.Progress * 100,5:0}%  ↓ {Row.Bytes(p.DownloadSpeed),9}/s  ↑ {Row.Bytes(p.UploadSpeed),9}/s").ToArray();
        }
        f.AddButton("Close", f.Close, isCancel: true);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; timer.Tick += (_, _) => Update();
        f.Window.Closed += (_, _) => timer.Stop(); Update(); timer.Start(); f.Show();
    }

    /// <summary>Each tracker's status, seeds and peers.</summary>
    public static void TorrentTrackers(Window owner, DownloadManager manager, Row row)
    {
        var f = new Form(owner, "Trackers · " + row.Record.FileName, 800);
        var list = f.Field("Trackers", new ListBox { Height = 260 });
        void Update()
        {
            var t = row.Record.Torrent;
            var status = t?.TrackerStatus ?? new();
            list.ItemsSource = status.Count > 0
                ? status.Select(s => $"{s.Url}\n   {s.Status} · Seeds {(s.Seeds >= 0 ? s.Seeds.ToString() : "–")} · Peers {(s.Peers >= 0 ? s.Peers.ToString() : "–")}").ToArray()
                : (t?.Trackers ?? new()).Select(u => u + "\n   Not contacted yet").DefaultIfEmpty("No trackers (peers come from DHT).").ToArray();
        }
        f.AddButton("Update Tracker", () => manager.Reannounce(row));
        f.AddButton("Close", f.Close, isCancel: true);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; timer.Tick += (_, _) => Update();
        f.Window.Closed += (_, _) => timer.Stop(); Update(); timer.Start(); f.Show();
    }

    /// <summary>Port forwarding and encryption for torrents started afterwards.</summary>
    public static void TorrentNetwork(Window owner, DownloadManager manager)
    {
        var f = new Form(owner, "Torrent Network", 560);
        var forwarding = f.Check("Forward the listening port on my router (UPnP and NAT-PMP), so more peers can connect", manager.Settings.TorrentPortForwarding);
        var modes = new[] { "prefer", "require", "off" };
        var encryption = f.Field("Encryption", new ComboBox { ItemsSource = new[] { "Prefer encrypted connections", "Only encrypted connections", "Off" },
            SelectedIndex = Math.Max(0, Array.IndexOf(modes, manager.Settings.TorrentEncryption)) });
        f.Note("Torrents use your proxy setting and the speed limit for all downloads. Changes apply to torrents started afterwards.");
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Save", () =>
        {
            manager.Settings.TorrentPortForwarding = forwarding.IsChecked == true;
            manager.Settings.TorrentEncryption = modes[Math.Max(0, encryption.SelectedIndex)];
            manager.ApplySettings(manager.Settings); f.Close();
        }, isDefault: true);
        f.Show();
    }

    sealed record TorrentManagerRow(Row Row)
    {
        public string Label => $"{Row.Record.FileName} · {Row.Status} · {Row.Percent:0}%\n{TorrentText.Summary(Row.Record.Torrent!)}";
    }
}

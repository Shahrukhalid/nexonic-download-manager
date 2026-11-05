using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

public static partial class Dialogs
{
    /// <summary>Connected peers, refreshed every two seconds while open.</summary>
    public static async Task TorrentPeers(Window owner, DownloadManager manager, Row row)
    {
        var f = new Form(owner, "Peers · " + row.Record.FileName, 860);
        f.Note("Flags: E encrypted, I incoming, P µTP, S seed. Peers appear while the torrent is running.");
        var list = f.Field("Peers", new ListBox { Height = 360, FontFamily = new Avalonia.Media.FontFamily("monospace") });
        void Update()
        {
            var peers = manager.TorrentPeers(row);
            list.ItemsSource = peers.Count == 0 ? new[] { manager.IsRunning(row) ? "Looking for peers…" : "Start the torrent to see its peers." }
                : peers.Select(p => $"{p.Address,-39} {p.Client,-24} {p.Flags,-4} {p.Progress * 100,5:0}%  ↓ {Row.Bytes(p.DownloadSpeed),9}/s  ↑ {Row.Bytes(p.UploadSpeed),9}/s").ToArray();
        }
        f.AddButton("Close", f.Close, isCancel: true);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; timer.Tick += (_, _) => Update();
        f.Window.Closed += (_, _) => timer.Stop(); Update(); timer.Start(); await f.Show();
    }

    /// <summary>Each tracker's status, seeds and peers.</summary>
    public static async Task TorrentTrackers(Window owner, DownloadManager manager, Row row)
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
        f.Window.Closed += (_, _) => timer.Stop(); Update(); timer.Start(); await f.Show();
    }

    /// <summary>Port forwarding and encryption for torrents started afterwards.</summary>
    public static async Task TorrentNetwork(Window owner, DownloadManager manager)
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
        await f.Show();
    }

    sealed record TorrentManagerRow(Row Row)
    {
        public string Label => $"{Row.Record.FileName} · {Row.Status} · {Row.Percent:0}%\n{TorrentText.Summary(Row.Record.Torrent!)}";
    }
    public static async Task TorrentManager(Window owner, DownloadManager manager, string queueId)
    {
        var f = new Form(owner, "Torrent Manager", 900);
        f.Note("Pause and resume use verified native checkpoints. Removing a task retains downloaded files. Select a torrent to manage its files, trackers, seeding and limits.");
        var list = f.Field("Torrents", new ListBox { Height = 220 });
        list.ItemTemplate = new FuncDataTemplate<TorrentManagerRow>((r, _) => new TextBlock { Text = r?.Label });
        var details = f.Note("Select a torrent to see its details.");
        details.FontFamily = new Avalonia.Media.FontFamily("monospace");
        Guid? selected = null;
        void Update()
        {
            selected = (list.SelectedItem as TorrentManagerRow)?.Row.Record.Id ?? selected;
            var rows = manager.Rows.Where(r => r.Record.Torrent != null).Select(r => new TorrentManagerRow(r)).ToList();
            list.ItemsSource = rows; list.SelectedItem = rows.FirstOrDefault(r => r.Row.Record.Id == selected);
            details.Text = Pick() is { } row ? TorrentText.Details(row.Record.Torrent!, manager.IsRunning(row)) : "Select a torrent to see its details.";
        }
        Row? Pick() => (list.SelectedItem as TorrentManagerRow)?.Row;
        f.AddButton("Add Torrent", async () => await Torrent(f.Window, manager, "", queueId));
        f.AddButton("Pause", () => { if (Pick() is { } r) manager.Pause(r); });
        f.AddButton("Resume", () => { if (Pick() is { } r) manager.Resume(r); });
        f.AddButton("Files and Settings", async () => { if (Pick() is { } r) await TorrentFiles(f.Window, manager, r); });
        f.AddButton("Peers", async () => { if (Pick() is { } r) await TorrentPeers(f.Window, manager, r); });
        f.AddButton("Trackers", async () => { if (Pick() is { } r) await TorrentTrackers(f.Window, manager, r); });
        f.AddButton("Network", async () => await TorrentNetwork(f.Window, manager));
        f.AddButton("Remove", () => { if (Pick() is { } r) manager.Remove(r); });
        f.AddButton("Close", f.Close);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Update();
        f.Window.Closed += (_, _) => timer.Stop(); Update(); timer.Start(); await f.Show();
    }
}

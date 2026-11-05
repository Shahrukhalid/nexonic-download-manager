using System.Globalization;
using Avalonia.Controls;
using Nexonic.Core;
using Nexonic.Linux;
namespace Nexonic.App;
public static partial class Dialogs
{
    sealed class NativeTorrentOptionsForm
    {
        readonly CheckBox seed, dht, sequential;
        readonly TextBox ratio, duration, download, upload, trackers, connections, slots;
        public NativeTorrentOptionsForm(Form form, TorrentMetadata torrent, int limit)
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
            var addresses = (trackers.Text ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
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
}

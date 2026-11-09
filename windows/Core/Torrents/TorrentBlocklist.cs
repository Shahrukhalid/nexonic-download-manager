using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

namespace Nexonic.Core;

/// <summary>
/// A list of address ranges torrents never connect to. Reads the common formats: P2P ("name:1.2.3.0-1.2.3.255"),
/// DAT ("001.002.003.000 - 001.002.003.255 , 000 , name"), CIDR ("1.2.3.0/24") and single addresses, plain or gzip.
/// </summary>
public static class TorrentBlocklist
{
    public const int MaxRanges = 2_000_000;
    const long MaxBytes = 256L * 1024 * 1024;
    static readonly TimeSpan Refresh = TimeSpan.FromDays(1);

    /// <summary>The ranges in a list's text; lines that aren't addresses (comments, blanks) are skipped.</summary>
    public static List<(IPAddress First, IPAddress Last)> Parse(TextReader text)
    {
        var ranges = new List<(IPAddress, IPAddress)>();
        while (text.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';' || line.StartsWith("//")) continue;
            if (TryLine(line, out var r)) ranges.Add(r);
            if (ranges.Count > MaxRanges) throw new InvalidDataException($"The blocklist has more than {MaxRanges:N0} ranges.");
        }
        return ranges;
    }

    static bool TryLine(string line, out (IPAddress, IPAddress) range)
    {
        range = default;
        // DAT: the range comes before the first comma.
        var comma = line.IndexOf(',');
        var body = comma > 0 && line[..comma].Contains('-') ? line[..comma] : line;
        // P2P: the range comes after the last colon that is followed by an IPv4 range.
        var dash = body.LastIndexOf('-');
        if (dash > 0)
        {
            var left = body[..dash].Trim();
            var colon = left.LastIndexOf(':');
            if (colon >= 0 && !IPAddress.TryParse(left, out _)) left = left[(colon + 1)..].Trim();
            return TryAddress(left, out var a) && TryAddress(body[(dash + 1)..].Trim(), out var b)
                   && a.AddressFamily == b.AddressFamily && Compare(a, b) <= 0 && Set(out range, a, b);
        }
        var slash = body.IndexOf('/');
        if (slash > 0 && TryAddress(body[..slash].Trim(), out var net) && int.TryParse(body[(slash + 1)..].Trim(), out var bits))
        {
            var bytes = net.GetAddressBytes();
            if (bits < 0 || bits > bytes.Length * 8) return false;
            var first = (byte[])bytes.Clone(); var last = (byte[])bytes.Clone();
            for (var i = 0; i < bytes.Length * 8; i++)
            {
                if (i < bits) continue;
                first[i / 8] &= (byte)~(0x80 >> (i % 8));
                last[i / 8] |= (byte)(0x80 >> (i % 8));
            }
            return Set(out range, new IPAddress(first), new IPAddress(last));
        }
        return TryAddress(body, out var one) && Set(out range, one, one);
    }

    static bool Set(out (IPAddress, IPAddress) range, IPAddress a, IPAddress b) { range = (a, b); return true; }

    /// <summary>Addresses with leading zeros ("001.002.003.004") as DAT lists write them.</summary>
    static bool TryAddress(string s, out IPAddress ip)
    {
        var parts = s.Split('.');
        if (parts.Length == 4 && parts.All(p => p.Length is > 0 and <= 3 && p.All(char.IsAsciiDigit) && int.Parse(p) <= 255))
        {
            ip = new IPAddress(parts.Select(byte.Parse).ToArray());
            return true;
        }
        if (s.Contains(':') && IPAddress.TryParse(s, out var v6) && v6.AddressFamily == AddressFamily.InterNetworkV6) { ip = v6; return true; }
        ip = IPAddress.None;
        return false;
    }

    static int Compare(IPAddress a, IPAddress b)
    {
        var x = a.GetAddressBytes(); var y = b.GetAddressBytes();
        for (var i = 0; i < x.Length; i++) if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        return 0;
    }

    /// <summary>Reads a list from bytes (gzip is unpacked).</summary>
    public static List<(IPAddress First, IPAddress Last)> Parse(Stream data)
    {
        var buffered = new BufferedStream(data);
        Span<byte> head = stackalloc byte[2];
        var gzip = false;
        if (buffered.CanSeek) { gzip = buffered.Read(head) == 2 && head[0] == 0x1f && head[1] == 0x8b; buffered.Position = 0; }
        Stream input = gzip ? new GZipStream(buffered, CompressionMode.Decompress) : buffered;
        using var reader = new StreamReader(new LimitedStream(input, MaxBytes));
        return Parse(reader);
    }

    /// <summary>
    /// The ranges for the setting: a local file, or an https:// list downloaded at most once a day (the last copy is
    /// used when the server can't be reached). Empty setting = no ranges.
    /// </summary>
    public static async Task<List<(IPAddress First, IPAddress Last)>> LoadAsync(string setting, ProxySettings? proxy, CancellationToken ct = default)
    {
        setting = (setting ?? "").Trim();
        if (setting.Length == 0) return new();
        if (Uri.TryCreate(setting, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
        {
            if (u.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("The blocklist address must start with https://");
            var cache = Path.Combine(AppPaths.Support, "Torrent blocklist", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(setting)))[..16] + ".list");
            if (!File.Exists(cache) || DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) > Refresh)
            {
                try { await DownloadAsync(u, cache, proxy, ct); }
                catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    if (!File.Exists(cache)) throw new InvalidOperationException($"The blocklist could not be downloaded: {e.Message}");
                }
            }
            setting = cache;
        }
        if (!File.Exists(setting)) throw new FileNotFoundException("The blocklist file was not found.", setting);
        await using var file = File.OpenRead(setting);
        var ranges = Parse(file);
        if (ranges.Count == 0) throw new InvalidDataException("The blocklist has no address ranges.");
        return ranges;
    }

    static async Task DownloadAsync(Uri url, string cache, ProxySettings? proxy, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        using var http = new HttpClient(Network.Handler(proxy ?? new ProxySettings { Mode = ProxyMode.System })) { Timeout = TimeSpan.FromMinutes(2) };
        using var res = await Network.SendAsync(http, url, u => new HttpRequestMessage(HttpMethod.Get, u), HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var temp = cache + ".tmp";
        await using (var output = File.Create(temp))
        await using (var body = new LimitedStream(await res.Content.ReadAsStreamAsync(ct), MaxBytes))
            await body.CopyToAsync(output, ct);
        await using (var check = File.OpenRead(temp))
            if (Parse(check).Count == 0) { File.Delete(temp); throw new IOException("The downloaded blocklist has no address ranges."); }
        File.Move(temp, cache, overwrite: true);
    }

    /// <summary>The ranges as the torrent engine takes them.</summary>
    public static string[][] ForEngine(IEnumerable<(IPAddress First, IPAddress Last)> ranges) =>
        ranges.Select(r => new[] { r.First.ToString(), r.Last.ToString() }).ToArray();

    sealed class LimitedStream(Stream inner, long max) : Stream
    {
        long read;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            if ((read += n) > max) throw new InvalidDataException("The blocklist is too large.");
            return n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await inner.ReadAsync(buffer, ct);
            if ((read += n) > max) throw new InvalidDataException("The blocklist is too large.");
            return n;
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;

namespace Nexonic.Core;

/// <summary>Advanced connection settings (Options → Connection). The defaults are the values the app always used.</summary>
public sealed class NetworkOptions
{
    /// <summary>Smallest part a file is split into, in KB.</summary>
    public int MinSegmentKB { get; set; } = 512;
    /// <summary>Tries per part before the download stops with an error (progress that keeps arriving resets the count).</summary>
    public int Retries { get; set; } = 6;
    public int ConnectTimeoutSeconds { get; set; } = 30;
    /// <summary>A part that receives nothing for this long is reconnected.</summary>
    public int ReadTimeoutSeconds { get; set; } = 60;
    /// <summary>DNS-over-HTTPS server (JSON API, e.g. https://cloudflare-dns.com/dns-query); empty = the system's DNS.</summary>
    public string DnsOverHttps { get; set; } = "";

    public static readonly string[] DohServers = { "https://cloudflare-dns.com/dns-query", "https://dns.google/resolve" };

    public NetworkOptions Clamped() => new()
    {
        MinSegmentKB = Math.Clamp(MinSegmentKB, 64, 1024 * 1024),
        Retries = Math.Clamp(Retries, 1, 50),
        ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, 3, 300),
        ReadTimeoutSeconds = Math.Clamp(ReadTimeoutSeconds, 5, 3600),
        DnsOverHttps = DnsOverHttpsUrlOk(DnsOverHttps) ? DnsOverHttps.Trim() : "",
    };

    public static bool DnsOverHttpsUrlOk(string? url) =>
        Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
}

/// <summary>Settings used for every download from one website (Options → Sites). Empty fields keep the general setting.</summary>
public sealed class HostProfile
{
    /// <summary>example.com (also matches www.example.com and other subdomains).</summary>
    public string Host { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int? MaxConnections { get; set; }
    public int? SpeedLimitKBps { get; set; }
    public int? Retries { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? UserAgent { get; set; }
    public string? Folder { get; set; }
    public string? QueueName { get; set; }
    /// <summary>False: downloads from this site are added stopped (start them yourself or with a queue).</summary>
    public bool? StartAtOnce { get; set; }
    /// <summary>Login sent only to this website (never to a site it redirects to). Saved encrypted.</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>Null = the proxy from Options.</summary>
    public ProxySettings? Proxy { get; set; }
}

public static class HostProfiles
{
    /// <summary>"https://www.Example.com/x" → "www.example.com"; "*.example.com" → "example.com".</summary>
    public static string Normalize(string? host)
    {
        var h = (host ?? "").Trim().ToLowerInvariant();
        if (h.Contains("://") && Uri.TryCreate(h, UriKind.Absolute, out var u)) h = u.Host;
        h = h.Split('/')[0];
        if (h.StartsWith("*.")) h = h[2..];
        var colon = h.LastIndexOf(':');
        if (colon > 0 && !h.Contains(']') && int.TryParse(h[(colon + 1)..], out _)) h = h[..colon];
        return h.Trim('.');
    }

    /// <summary>The most specific enabled profile whose host is the link's host or a parent domain of it.</summary>
    public static HostProfile? Match(IEnumerable<HostProfile>? profiles, string url)
    {
        if (profiles == null || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        var host = u.Host.ToLowerInvariant().Trim('.');
        HostProfile? best = null;
        var bestLength = -1;
        foreach (var p in profiles)
        {
            if (!p.Enabled) continue;
            var h = Normalize(p.Host);
            if (h.Length == 0) continue;
            if ((host == h || host.EndsWith("." + h, StringComparison.Ordinal)) && h.Length > bestLength) { best = p; bestLength = h.Length; }
        }
        return best;
    }
}

/// <summary>Per-download engine settings, from Options and the site's profile.</summary>
public sealed record TransferOptions(long MinSegment = SegmentPlanner.MinSegment, int Retries = 6, int ReadTimeoutSeconds = 60)
{
    public static TransferOptions From(NetworkOptions n, HostProfile? p) => new(
        Math.Clamp(n.MinSegmentKB, 64, 1024 * 1024) * 1024L,
        Math.Clamp(p?.Retries ?? n.Retries, 1, 50),
        Math.Clamp(p?.TimeoutSeconds ?? n.ReadTimeoutSeconds, 5, 3600));
}

/// <summary>Looks names up with DNS over HTTPS (JSON API), cached for the answer's lifetime.</summary>
public static class DnsOverHttps
{
    static readonly ConcurrentDictionary<string, (IPAddress[] Addresses, DateTime Until)> cache = new();
    static readonly HttpClient client = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) }) { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>The A/AAAA addresses in a DNS JSON answer and the shortest TTL.</summary>
    public static (List<IPAddress> Addresses, int Ttl) ParseAnswer(string json)
    {
        var list = new List<IPAddress>();
        var ttl = 300;
        var node = JsonNode.Parse(json);
        if (node?["Answer"] is JsonArray answers)
            foreach (var a in answers)
            {
                var type = a?["type"]?.GetValue<int>() ?? 0;
                if (type is not (1 or 28)) continue;
                if (IPAddress.TryParse(a?["data"]?.GetValue<string>(), out var ip)) list.Add(ip);
                if (a?["TTL"] is JsonValue t && t.TryGetValue<int>(out var v)) ttl = Math.Min(ttl, Math.Max(30, v));
            }
        return (list, ttl);
    }

    public static async Task<IPAddress[]> ResolveAsync(string server, string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal)) return new[] { literal };
        var key = server + "|" + host;
        if (cache.TryGetValue(key, out var hit) && hit.Until > DateTime.UtcNow) return hit.Addresses;
        var all = new List<IPAddress>();
        var ttl = 300;
        foreach (var type in new[] { "A", "AAAA" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{server}?name={Uri.EscapeDataString(host)}&type={type}");
            req.Headers.Accept.ParseAdd("application/dns-json");
            try
            {
                using var res = await client.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) continue;
                var (found, t) = ParseAnswer(await res.Content.ReadAsStringAsync(ct));
                all.AddRange(found);
                ttl = Math.Min(ttl, t);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException) { }
        }
        if (all.Count == 0) throw new HttpRequestException($"DNS over HTTPS could not find {host}");
        var result = all.ToArray();
        cache[key] = (result, DateTime.UtcNow.AddSeconds(ttl));
        return result;
    }

    /// <summary>Connects to the first address that answers (IPv4 first).</summary>
    public static async ValueTask<Stream> ConnectAsync(string server, DnsEndPoint target, TimeSpan timeout, CancellationToken ct)
    {
        var addresses = (await ResolveAsync(server, target.Host, ct)).OrderBy(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToList();
        Exception? last = null;
        foreach (var ip in addresses)
        {
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(timeout);
                await socket.ConnectAsync(new IPEndPoint(ip, target.Port), limit.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (!ct.IsCancellationRequested) { socket.Dispose(); last = e; }
        }
        throw new HttpRequestException($"Could not connect to {target.Host}", last);
    }
}

/// <summary>
/// Backup of settings, queues and the downloads list (versioned JSON). Passwords, proxy passwords and cookies are never
/// written into a backup; they stay encrypted on this computer.
/// </summary>
public static class Backup
{
    public const string Format = "nexonic-backup";
    public const int Version = 1;

    public sealed class Data
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public string App { get; set; } = "";
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public AppSettings? Settings { get; set; }
        public List<DownloadQueue>? Queues { get; set; }
        public List<DownloadRecord>? Downloads { get; set; }
    }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string Export(AppSettings settings, IEnumerable<DownloadQueue> queues, IEnumerable<DownloadRecord> downloads, string appVersion)
    {
        var file = new Data
        {
            Format = Format,
            Version = Version,
            App = appVersion,
            Settings = Clone(settings),
            Queues = Clone(queues.ToList()),
            // Torrent backups need tracker-secret redaction and portable resume data (Milestone 8).
            Downloads = downloads.Where(r => r.Torrent == null).Select(r => Clone(r)!).ToList(),
        };
        var s = file.Settings!;
        s.Proxy.Password = "";
        s.TorrentsEnabled = false;
        s.Automation.Key = "";
        foreach (var p in s.HostProfiles) { p.Password = null; if (p.Proxy != null) p.Proxy.Password = ""; }
        foreach (var r in file.Downloads)
        {
            r.Cookies = null;
            r.Password = null;
            r.InfoJson = null;
            r.VideoParts = null;
            r.VideoStep = null;
        }
        return JsonSerializer.Serialize(file, Pretty);
    }

    /// <summary>Reads a backup; throws InvalidDataException for other files or newer formats.</summary>
    public static Data Parse(string json)
    {
        Data? file;
        try { file = JsonSerializer.Deserialize<Data>(json); }
        catch (JsonException) { throw new InvalidDataException("This is not a Nexonic Download Manager backup."); }
        if (file == null || file.Format != Format) throw new InvalidDataException("This is not a Nexonic Download Manager backup.");
        if (file.Version > Version) throw new InvalidDataException("This backup was made by a newer version of the app. Update the app first.");
        file.Downloads = (file.Downloads ?? new()).Where(r => r != null).ToList();
        foreach (var r in file.Downloads)
        {
            // Unfinished downloads start again from the beginning (their parts are not in the backup).
            if (r.Status != DownloadStatus.Finished)
            {
                r.Status = DownloadStatus.Paused;
                r.Segments = new();
                r.Validator = null;
                r.Error = null;
            }
            r.Cookies = null;
            r.Password = null;
            // A backup is a file from somewhere else: keep only what a backup made by this app can hold.
            r.InfoJson = null;
            r.VideoParts = null;
            r.VideoStep = null;
            r.FileName = FileNames.Sanitize(r.FileName ?? "");
            if (!string.IsNullOrEmpty(r.Folder) && !Path.IsPathFullyQualified(r.Folder)) r.Folder = "";
        }
        file.Downloads = (file.Downloads ?? new()).Where(r => r.Torrent == null && new DownloadJob(r.Url ?? "").IsValid).ToList();
        if (file.Settings is { } s)
        {
            // Never take secrets or the automation switch from a file: the API stays as it is on this computer.
            s.Proxy ??= new();
            s.Network ??= new();
            s.Proxy.Password = "";
            s.Automation = new AutomationSettings();
            s.TorrentsEnabled = false;
            s.HostProfiles = (s.HostProfiles ?? new()).Where(p => p != null).ToList();
            foreach (var p in s.HostProfiles) { p.Password = null; if (p.Proxy != null) p.Proxy.Password = ""; }
        }
        file.Queues = (file.Queues ?? new()).Where(q => q != null).ToList();
        return file;
    }

    /// <summary>Downloads from the backup that are not in the list yet (same id, or same link and file name).</summary>
    public static List<DownloadRecord> NewDownloads(Data file, IEnumerable<DownloadRecord> existing)
    {
        var have = existing.ToList();
        var ids = have.Select(r => r.Id).ToHashSet();
        var keys = have.Select(r => (r.Url, r.FileName)).ToHashSet();
        return (file.Downloads ?? new()).Where(r => !string.IsNullOrEmpty(r.Url) && !ids.Contains(r.Id) && !keys.Contains((r.Url, r.FileName))).ToList();
    }

    static T? Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
}

/// <summary>Network check for Options: name lookup, connection and answer of a test address with the current settings.</summary>
public static class NetworkCheck
{
    public static async Task<List<string>> RunAsync(ProxySettings proxy, string url = "https://github.com/", CancellationToken ct = default)
    {
        var lines = new List<string>();
        var u = new Uri(url);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var doh = Network.Options.DnsOverHttps;
        try
        {
            var ips = NetworkOptions.DnsOverHttpsUrlOk(doh)
                ? await DnsOverHttps.ResolveAsync(doh, u.Host, ct)
                : await Dns.GetHostAddressesAsync(u.Host, ct);
            lines.Add($"Name lookup ({(NetworkOptions.DnsOverHttpsUrlOk(doh) ? "DNS over HTTPS" : "system DNS")}): {u.Host} → {string.Join(", ", ips.Take(3).Select(i => i.ToString()))} in {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e) { lines.Add($"Name lookup failed: {e.Message}"); }
        sw.Restart();
        try
        {
            using var http = new HttpClient(Network.Handler(proxy)) { Timeout = TimeSpan.FromSeconds(30) };
            using var req = new HttpRequestMessage(HttpMethod.Head, u);
            using var res = await http.SendAsync(req, ct);
            lines.Add($"Connection{(proxy.Mode is ProxyMode.Http or ProxyMode.Socks ? " through the proxy" : "")}: HTTP {(int)res.StatusCode} in {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e) { lines.Add($"Connection failed: {(e.InnerException ?? e).Message}"); }
        return lines;
    }
}

/// <summary>Speed limits offered in the tray menu.</summary>
public static class SpeedLimits
{
    public static readonly int[] Presets = { 0, 128, 256, 512, 1024, 2048, 5120, 10240 };

    public static string Title(int kb) => kb <= 0 ? "No Limit" : kb >= 1024 && kb % 1024 == 0 ? $"{kb / 1024} MB/s" : $"{kb} KB/s";
}

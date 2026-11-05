using System.Text.Json;

namespace Nexonic.Core;

public sealed record TorrentFile(string Path, long Size, bool Pad = false);
public sealed class TorrentMetadata
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string InfoHash { get; set; } = "";
    public string Metainfo { get; set; } = "";
    public List<TorrentFile> Files { get; set; } = new();
    public List<int>? FilePriorities { get; set; }
    public bool MetadataPending { get; set; }
    public bool Seed { get; set; }
    public double RatioLimit { get; set; } = 1;
    public int SeedSeconds { get; set; }
    /// <summary>0 = no limit. Uploading is what earns fast downloads from other peers, so new torrents don't cap it.</summary>
    public int UploadLimitKBps { get; set; }
    /// <summary>Peers this torrent may connect to and upload to at once (0 = no limit), and pieces in order.</summary>
    public int MaxConnections { get; set; } = 50;
    public int MaxUploads { get; set; } = 4;
    public bool Sequential { get; set; }
    public bool Dht { get; set; } = true;
    public List<string> Trackers { get; set; } = new();
    public long Uploaded { get; set; }
    public long DownloadRate { get; set; }
    public long Eta { get; set; } = -1;
    public double Ratio { get; set; }
    public bool PayloadComplete { get; set; }
    public bool CompletionNotified { get; set; }
    public Dictionary<string, string> Sha256 { get; set; } = new();
    public string ResumeStatus { get; set; } = "";
    public int[] Priorities()
    {
        var values = FilePriorities?.ToArray() ?? Enumerable.Repeat(4, Files.Count).ToArray();
        if (values.Length != Files.Count || values.Any(p => p is not (0 or 1 or 4 or 7)) || !values.Any(p => p > 0))
            throw new ArgumentException("Select at least one file and choose Skip, Low, Normal or High priorities.");
        return values;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public long SelectedSize => Files.Zip(Priorities()).Where(pair => pair.Second > 0).Sum(pair => pair.First.Size);
    public long Received { get; set; }
    public int Peers { get; set; }
    public int Seeds { get; set; }
    public int UploadRate { get; set; }
    // What a BitTorrent client shows for a running torrent (refreshed by the engine).
    public int Connections { get; set; }
    public int SeedsTotal { get; set; } = -1;
    public int PeersTotal { get; set; } = -1;
    public double Availability { get; set; } = -1;
    public long Downloaded { get; set; }
    public int PiecesDone { get; set; }
    public int PieceCount { get; set; }
    public long PieceSize { get; set; }
    public long ActiveSeconds { get; set; }
    public List<TorrentTrackerStatus> TrackerStatus { get; set; } = new();
    /// <summary>Engine settings for this torrent's limits, connections, seeding and order (-1 = no limit).</summary>
    public Dictionary<string, object> EngineSettings() => new()
    {
        ["seed"] = Seed, ["ratio_limit"] = RatioLimit, ["seed_seconds"] = SeedSeconds,
        ["upload_limit"] = (long)Math.Clamp(UploadLimitKBps, 0, 2000000) * 1024,
        ["max_connections"] = MaxConnections <= 0 ? -1 : Math.Clamp(MaxConnections, 2, 10000),
        ["max_uploads"] = MaxUploads <= 0 ? -1 : Math.Clamp(MaxUploads, 1, 10000), ["sequential"] = Sequential,
    };
}

public sealed record TorrentTrackerStatus(string Url, bool Working, bool Updating, string Message, int Seeds, int Peers)
{
    public string Status => Updating ? "Updating…" : Message.Length > 0 ? Message : Working ? "Working" : "Not contacted yet";
}

public sealed record TorrentPeer(string Address, int Port, string Client, double Progress, long DownloadSpeed, long UploadSpeed,
    long Downloaded, long Uploaded, bool Seed, bool Encrypted, bool Incoming, bool Utp)
{
    /// <summary>Flags as BitTorrent clients show them: E encrypted, I incoming, P µTP, S seed.</summary>
    public string Flags => (Encrypted ? "E" : "") + (Incoming ? "I" : "") + (Utp ? "P" : "") + (Seed ? "S" : "");
}

/// <summary>Bundled native metadata previews; descriptor bytes never indicate content completion.</summary>
public static class TorrentRuntime
{
    public static string? Unavailable(AppSettings settings) => !settings.TorrentsEnabled ? "Turn on torrents first."
        : settings.Network.DnsOverHttps.Length > 0 ? "Torrents do not yet support the configured DNS over HTTPS resolver."
        : !File.Exists(NativeTorrentSession.BundledLibrary) ? "The bundled native torrent engine is missing. Reinstall the complete application." : null;

    /// <summary>The proxy torrents use: none, the HTTP or SOCKS5 proxy from settings, or the system's web proxy.</summary>
    public static object? Proxy(ProxySettings? proxy)
    {
        if (proxy is null || proxy.Mode == ProxyMode.None) return null;
        if (proxy.Mode is ProxyMode.Http or ProxyMode.Socks)
            return string.IsNullOrWhiteSpace(proxy.Host) || proxy.Port is < 1 or > 65535 ? null
                : new { type = proxy.Mode == ProxyMode.Socks ? "socks5" : "http", host = proxy.Host.Trim(), port = proxy.Port, username = proxy.Username, password = proxy.Password };
        try
        {
            var target = new Uri("https://tracker.example/");
            var system = System.Net.Http.HttpClient.DefaultProxy;
            if (system.IsBypassed(target) || system.GetProxy(target) is not { } found || found == target || found.Port is < 1 or > 65535) return null;
            return new { type = "http", host = found.Host, port = found.Port, username = "", password = "" };
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException or UriFormatException) { return null; }
    }

    /// <summary>A torrent's own download limit, capped by the limit for all downloads (KB/s, 0 = none).</summary>
    public static int DownloadLimit(int? own, int all)
    {
        int mine = Math.Clamp(own ?? 0, 0, 2000000), shared = Math.Clamp(all, 0, 2000000);
        return mine == 0 ? shared : shared == 0 ? mine : Math.Min(mine, shared);
    }

    public static Task<TorrentMetadata> PreviewAsync(string source, CancellationToken cancellation = default, ProxySettings? proxy = null) => Task.Run(async () =>
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        var token = timeout.Token;
        var state = Path.Combine(AppPaths.Parts, "Torrent previews", Guid.NewGuid().ToString("N"));
        try
        {
            source = source.Trim();
            if (source.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) source = "magnet:?" + source[8..];
            string metainfo = source.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) ? ""
                : Convert.ToBase64String(await TorrentSource.ReadMetadataAsync(source, token));
            using var engine = new NativeTorrentSession(state, proxy: Proxy(proxy));
            var status = engine.Invoke(new { action = "add", id = "preview", source, metainfo, preview = true });
            while (status.GetProperty("state").GetString() != "preview")
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(250, token);
                status = engine.Invoke(new { action = "status", id = "preview" });
                if (status.GetProperty("state").GetString() == "failed") throw new InvalidOperationException(status.GetProperty("error").GetString());
            }
            return ReadMetadata(status);
        }
        finally { try { Directory.Delete(state, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }, cancellation);

    /// <summary>Swarm, connection, piece and tracker details from an engine snapshot (missing fields are skipped).</summary>
    internal static void ReadDetails(JsonElement e, TorrentMetadata t)
    {
        int Int(string name, int fallback) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : fallback;
        long Long(string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
        t.Connections = Int("connections", t.Peers); t.SeedsTotal = Int("seeds_total", -1); t.PeersTotal = Int("peers_total", -1);
        t.Availability = e.TryGetProperty("availability", out var a) && a.TryGetDouble(out var d) ? d : -1;
        t.Downloaded = Long("downloaded"); t.PiecesDone = Int("pieces_done", 0); t.PieceCount = Int("piece_count", 0);
        t.PieceSize = Long("piece_size"); t.ActiveSeconds = Long("active_seconds");
        if (e.TryGetProperty("tracker_status", out var list) && list.ValueKind == JsonValueKind.Array)
            t.TrackerStatus = list.EnumerateArray().Select(x => new TorrentTrackerStatus(x.GetProperty("url").GetString() ?? "",
                x.GetProperty("working").GetBoolean(), x.GetProperty("updating").GetBoolean(), x.GetProperty("message").GetString() ?? "",
                x.GetProperty("seeds").GetInt32(), x.GetProperty("peers").GetInt32())).ToList();
    }

    internal static TorrentMetadata ReadMetadata(JsonElement e)
    {
        var files = e.GetProperty("files").EnumerateArray().Select(f => new TorrentFile(
            f.GetProperty("path").GetString()!, f.GetProperty("size").GetInt64(), f.GetProperty("pad").GetBoolean())).ToList();
        var v1 = e.GetProperty("info_hash_v1").GetString()!;
        return new TorrentMetadata { Name = e.GetProperty("name").GetString()!, Size = e.GetProperty("size").GetInt64(),
            InfoHash = v1.Length > 0 ? v1 : e.GetProperty("info_hash_v2").GetString()!, Metainfo = e.GetProperty("metainfo").GetString()!,
            Files = files, FilePriorities = files.Select(f => f.Pad ? 0 : 4).ToList(),
            Trackers = e.GetProperty("trackers").EnumerateArray().Select(t => t.GetString()!).ToList() };
    }
}

public sealed class TorrentTransfer : IDownloader
{
    public DownloadRecord Record { get; }
    public SpeedLimiter OwnLimiter { get; } = new();
    public event Action<DownloadRecord>? Changed;
    readonly object gate = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DownloadRecord, SemaphoreSlim> workers = new();
    readonly DownloadContext context;
    bool discarded;
    NativeTorrentSession? active;
    NativeTorrentSession? Running { get { lock (gate) return active; } }
    /// <summary>Applies new limits, connections, seeding, file priorities and trackers to the running torrent.
    /// False when it isn't running (the settings are then used when it starts).</summary>
    public bool Apply(TorrentMetadata settings, int downloadLimitKBps, IReadOnlyList<int>? priorities = null, IReadOnlyList<string>? trackers = null)
    {
        if (Running is not { } engine) return false;
        var request = settings.EngineSettings();
        request["action"] = "configure"; request["id"] = "payload";
        request["download_limit"] = (long)TorrentRuntime.DownloadLimit(downloadLimitKBps, context.GlobalSpeedLimitKBps) * 1024;
        if (priorities != null) request["priorities"] = priorities;
        if (trackers != null) request["trackers"] = trackers;
        try { engine.Invoke(request); return true; }
        catch (ObjectDisposedException) { return false; }
    }
    /// <summary>Connected peers of the running torrent.</summary>
    public IReadOnlyList<TorrentPeer> Peers()
    {
        if (Running is not { } engine) return Array.Empty<TorrentPeer>();
        try
        {
            return engine.Invoke(new { action = "peers", id = "payload" }).GetProperty("peers").EnumerateArray().Select(p => new TorrentPeer(
                p.GetProperty("address").GetString() ?? "", p.GetProperty("port").GetInt32(), p.GetProperty("client").GetString() ?? "",
                p.GetProperty("progress").GetDouble(), p.GetProperty("download_speed").GetInt64(), p.GetProperty("upload_speed").GetInt64(),
                p.GetProperty("downloaded").GetInt64(), p.GetProperty("uploaded").GetInt64(), p.GetProperty("seed").GetBoolean(),
                p.GetProperty("encrypted").GetBoolean(), p.GetProperty("incoming").GetBoolean(), p.GetProperty("utp").GetBoolean())).ToList();
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or KeyNotFoundException) { return Array.Empty<TorrentPeer>(); }
    }
    /// <summary>Asks the trackers and DHT for more peers now.</summary>
    public void Reannounce()
    {
        try { Running?.Invoke(new { action = "reannounce", id = "payload" }); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }
    string ResumePath => Path.Combine(AppPaths.Parts, Record.Id.ToString("N"), "torrent.resume");
    readonly CancellationTokenSource cancellation = new();
    public TorrentTransfer(DownloadRecord record, DownloadContext context) { Record = record; this.context = context; }
    public Task StartAsync() => Task.Run(async () =>
    {
        var worker = workers.GetValue(Record, _ => new SemaphoreSlim(1, 1));
        try
        {
            await worker.WaitAsync(cancellation.Token);
            try { await RunAsync(); } finally { worker.Release(); }
        }
        catch (OperationCanceledException) { }
    });
    string? LoadResume()
    {
        try { return new FileInfo(ResumePath).Length <= 3 * 1024 * 1024 ? File.ReadAllText(ResumePath) : null; }
        catch { return null; }
    }
    async Task RunAsync()
    {
        NativeTorrentSession? engine = null;
        try
        {
            var settings = new AppSettings { TorrentsEnabled = context.TorrentsEnabled, Proxy = context.Proxy,
                SpeedLimitKBps = context.GlobalSpeedLimitKBps, Network = Network.Options };
            if (TorrentRuntime.Unavailable(settings) is { } reason) throw new InvalidOperationException(reason);
            var torrent = Record.Torrent ?? throw new InvalidOperationException("Torrent metadata is missing.");
            if (torrent.MetadataPending)
            {
                Record.Status = DownloadStatus.Probing; Changed?.Invoke(Record);
                torrent = await TorrentRuntime.PreviewAsync(Record.Url, cancellation.Token, context.Proxy);
                Record.Torrent = torrent; Record.FileName = torrent.Name; Record.Size = torrent.SelectedSize;
            }
            var priorities = torrent.Priorities();
            var folder = Path.Combine(Record.Folder, "Nexonic torrents", Record.Id.ToString("N"));
            var state = Path.Combine(AppPaths.Parts, Record.Id.ToString("N"), "native");
            Directory.CreateDirectory(state);
            // Import earlier Linux checkpoints once; native hash/path validation remains authoritative.
            var checkpoint = Path.Combine(state, "torrent-payload.resume");
            if (!File.Exists(checkpoint) && LoadResume() is { } legacy)
            {
                try
                {
                    var bytes = Convert.FromBase64String(legacy);
                    var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                    if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    using var output = new FileStream(checkpoint, options);
                    output.Write(bytes);
                }
                catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException) { }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            engine = new NativeTorrentSession(state, dht: torrent.Dht, proxy: TorrentRuntime.Proxy(context.Proxy),
                portForwarding: context.TorrentPortForwarding, encryption: context.TorrentEncryption);
            var add = torrent.EngineSettings();
            add["action"] = "add"; add["id"] = "payload"; add["source"] = Record.Url; add["metainfo"] = torrent.Metainfo; add["preview"] = false;
            add["destination"] = folder; add["priorities"] = priorities; add["trackers"] = torrent.Trackers;
            add["download_limit"] = (long)TorrentRuntime.DownloadLimit(Record.SpeedLimitKBps, context.GlobalSpeedLimitKBps) * 1024;
            engine.Invoke(add);
            lock (gate) active = engine;
            var snapshot = engine.Invoke(new { action = "resume", id = "payload" });
            torrent.ResumeStatus = File.Exists(checkpoint) ? "Resume checkpoint loaded; native pieces verified" : "Checking existing pieces";
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    if (cancellation.IsCancellationRequested) break;
                    var status = snapshot.GetProperty("state").GetString();
                    if (status == "failed") throw new InvalidOperationException(snapshot.GetProperty("error").GetString());
                    var metadata = TorrentRuntime.ReadMetadata(snapshot);
                    torrent.Name = metadata.Name; torrent.Files = metadata.Files; torrent.Size = metadata.Size;
                    Record.FileName = torrent.Name; Record.Size = torrent.SelectedSize;
                    torrent.Received = snapshot.GetProperty("received").GetInt64();
                    torrent.Peers = snapshot.GetProperty("peers").GetInt32(); torrent.Seeds = snapshot.GetProperty("seeds").GetInt32();
                    torrent.UploadRate = snapshot.GetProperty("upload_speed").GetInt32(); torrent.DownloadRate = snapshot.GetProperty("download_speed").GetInt64();
                    torrent.Uploaded = snapshot.GetProperty("uploaded").GetInt64(); torrent.Eta = snapshot.GetProperty("eta").GetInt64(); torrent.Ratio = snapshot.GetProperty("ratio").GetDouble();
                    torrent.PayloadComplete = snapshot.GetProperty("payload_complete").GetBoolean();
                    TorrentRuntime.ReadDetails(snapshot, torrent);
                    Record.Status = status == "seeding" ? DownloadStatus.Seeding : status == "complete" ? DownloadStatus.Finished
                        : status is "checking" or "verifying" ? DownloadStatus.Probing : DownloadStatus.Downloading;
                    if (torrent.PayloadComplete)
                    {
                        torrent.Sha256 = snapshot.GetProperty("sha256").Deserialize<Dictionary<string, string>>()!;
                        var selected = torrent.Files.Where((file, i) => priorities[i] > 0 && !file.Pad).ToList();
                        Record.SavedPath = selected.Count == 1 ? Path.Combine(folder, selected[0].Path) : Path.Combine(folder, torrent.Name);
                        Record.Finished ??= DateTime.Now;
                    }
                    Changed?.Invoke(Record);
                    if (status == "complete") break;
                }
                await Task.Delay(500, cancellation.Token);
                snapshot = engine.Invoke(new { action = "status", id = "payload" });
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e)
        {
            lock (gate)
            {
                if (!cancellation.IsCancellationRequested)
                { Record.Status = DownloadStatus.Failed; Record.Error = e.Message; Changed?.Invoke(Record); }
            }
        }
        finally
        {
            lock (gate) active = null;
            if (engine != null)
            {
                try
                {
                    engine.Invoke(new { action = "pause", id = "payload" });
                    engine.Invoke(new { action = "flush" });
                    if (discarded) engine.Invoke(new { action = "remove", id = "payload" });
                }
                catch (Exception e) when (e is InvalidOperationException or IOException) { }
                engine.Dispose();
            }
        }
    }
    public void Pause() { lock (gate) { cancellation.Cancel(); Record.Status = DownloadStatus.Paused; Changed?.Invoke(Record); } }
    public void Discard() { lock (gate) { discarded = true; cancellation.Cancel(); } } // User files are never deleted by native-engine cleanup.
}

public static class TorrentText
{
    static string Count(int connected, int total) => total >= 0 ? $"{connected} ({total})" : connected.ToString();
    /// <summary>A BitTorrent client's General tab as text.</summary>
    public static string Details(TorrentMetadata t, bool running) => string.Join("\n", new[]
    {
        $"Downloaded {Bytes(t.Downloaded > 0 ? t.Downloaded : t.Received)} · Uploaded {Bytes(t.Uploaded)} · Ratio {t.Ratio:0.000}",
        running ? $"Download {Bytes(t.DownloadRate)}/s · Upload {Bytes(t.UploadRate)}/s" + (t.Eta > 0 ? $" · Time left {TimeSpan.FromSeconds(Math.Min(t.Eta, 8553600)):g}" : "") : "Stopped",
        $"Seeds {Count(t.Seeds, t.SeedsTotal)} · Peers {Count(Math.Max(0, t.Peers - t.Seeds), t.PeersTotal)} · Connections {t.Connections}" + (t.MaxConnections > 0 ? $" of {t.MaxConnections}" : ""),
        (t.PieceCount > 0 ? $"Pieces {t.PiecesDone} of {t.PieceCount} × {Bytes(t.PieceSize)}" : "Pieces –") + (t.Availability >= 0 ? $" · Availability {t.Availability:0.000}" : "")
            + (t.ActiveSeconds > 0 ? $" · Active {TimeSpan.FromSeconds(t.ActiveSeconds):g}" : ""),
        $"Upload limit {(t.UploadLimitKBps > 0 ? t.UploadLimitKBps + " KB/s" : "none")} · Upload slots {(t.MaxUploads > 0 ? t.MaxUploads.ToString() : "no limit")}" + (t.Sequential ? " · In order" : ""),
        "Hash " + t.InfoHash,
    });
    static string Bytes(long n) { string[] u = { "B", "KB", "MB", "GB", "TB" }; double v = n; int i = 0; while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; } return i == 0 ? $"{n} B" : $"{v:0.0} {u[i]}"; }
    public static string Summary(TorrentMetadata t) => $"{t.Peers} peers · {t.Seeds} seeds · ↓ {t.DownloadRate / 1024d:0.0} KiB/s · ↑ {t.UploadRate / 1024d:0.0} KiB/s · Ratio {t.Ratio:0.00}"
        + (t.Eta >= 0 ? $" · ETA {TimeSpan.FromSeconds(Math.Min(t.Eta, 8553600)):g}" : "")
        + (t.PayloadComplete ? " · Pieces and SHA-256 verified" : "");
}

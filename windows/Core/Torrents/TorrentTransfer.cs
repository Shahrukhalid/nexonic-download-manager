using System.Diagnostics;
using System.Text.Json;

namespace Nexonic.Core;

public sealed record TorrentFile(string Path, long Size);
public sealed class TorrentMetadata
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string InfoHash { get; set; } = "";
    public string Metainfo { get; set; } = "";
    public List<TorrentFile> Files { get; set; } = new();
    public List<int>? FilePriorities { get; set; }
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
}

/// <summary>Native engine isolation; trusted embedded script, fixed interpreter, JSON data on stdin.</summary>
public static class TorrentRuntime
{
    static readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };
    static string Script
    {
        get
        {
            using var stream = typeof(TorrentRuntime).Assembly.GetManifestResourceStream("torrent.bridge.py")!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
    public static string? Unavailable(AppSettings settings) => !settings.TorrentsEnabled ? "Enable experimental torrents first."
        : settings.Proxy.Mode != ProxyMode.None ? "Torrents currently require an explicit No proxy setting; system and custom proxies are not supported."
        : settings.SpeedLimitKBps > 0 ? "Torrents do not yet support the shared global speed limit. Use a per-torrent limit instead."
        : settings.Network.DnsOverHttps.Length > 0 ? "Torrents do not yet support the configured DNS over HTTPS resolver."
        : !OperatingSystem.IsLinux() ? "The optional torrent runtime is currently supported on Linux only." : null;

    public static async Task<TorrentMetadata> PreviewAsync(string source, CancellationToken cancellation = default)
    {
        TorrentMetadata? metadata = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        await RunAsync(new { action = "metadata", source }, e =>
        {
            if (e.GetProperty("event").GetString() == "metadata") metadata = ReadMetadata(e);
        }, timeout.Token);
        return metadata ?? throw new InvalidOperationException("Torrent metadata was not returned.");
    }
    internal static TorrentMetadata ReadMetadata(JsonElement e) => e.Deserialize<TorrentMetadata>(json)!;
    internal static async Task RunAsync(object request, Action<JsonElement> onEvent, CancellationToken cancellation)
    {
        var psi = new ProcessStartInfo("/usr/bin/python3")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.ArgumentList.Add("-I"); psi.ArgumentList.Add("-u"); psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(Script);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the torrent runtime.");
        using var killAfterGrace = new Timer(_ => { try { process.Kill(entireProcessTree: true); } catch { } });
        try
        {
            var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            // Allow a bounded graceful stop so the native engine can flush a final resume checkpoint.
            using var stop = cancellation.Register(() =>
            {
                try { process.StandardInput.Close(); } catch { }
                killAfterGrace.Change(TimeSpan.FromSeconds(8), Timeout.InfiniteTimeSpan);
            });
            var sawEvent = false;
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.Length > 4 * 1024 * 1024) throw new InvalidDataException("Torrent response exceeds the size limit.");
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                sawEvent = true;
                if (e.GetProperty("event").GetString() == "error") throw new InvalidOperationException(e.GetProperty("message").GetString());
                onEvent(e);
            }
            await process.WaitForExitAsync();
            cancellation.ThrowIfCancellationRequested();
            var errors = await stderr;
            if (process.ExitCode != 0 || !sawEvent) throw new InvalidOperationException("Torrent runtime failed. Install python3-libtorrent (libtorrent 2.x). " + errors[..Math.Min(errors.Length, 300)]);
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        }
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
        try
        {
            var settings = new AppSettings { TorrentsEnabled = context.TorrentsEnabled, Proxy = context.Proxy,
                SpeedLimitKBps = context.GlobalSpeedLimitKBps, Network = Network.Options };
            if (TorrentRuntime.Unavailable(settings) is { } reason) throw new InvalidOperationException(reason);
            var torrent = Record.Torrent ?? throw new InvalidOperationException("Torrent metadata is missing.");
            var priorities = torrent.Priorities();
            lock (gate)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Record.Status = DownloadStatus.Probing;
                Changed?.Invoke(Record);
            }
            // Isolated per-task directory avoids overwriting unrelated files or another torrent's data.
            var folder = Path.Combine(Record.Folder, "Nexonic torrents", Record.Id.ToString("N"));
            await TorrentRuntime.RunAsync(new { action = "download", metainfo = torrent.Metainfo, folder, priorities, resume = LoadResume(),
                limit = (long)Math.Clamp(Record.SpeedLimitKBps ?? 0, 0, 2000000) * 1024 }, e =>
            {
                lock (gate)
                {
                    if (e.GetProperty("event").GetString() == "checkpoint")
                    {
                        if (!discarded)
                        {
                            var saved = e.GetProperty("resume").GetString()!;
                            try { TorrentCheckpoint.Write(ResumePath, saved); }
                            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                            { torrent.ResumeStatus = "Checkpoint could not be saved; resume will recheck pieces"; }
                        }
                        return;
                    }
                    if (cancellation.IsCancellationRequested) return;
                    switch (e.GetProperty("event").GetString())
                    {
                        case "metadata":
                            // Native validation is authoritative even if a saved history/backup was edited.
                            var checkedMetadata = TorrentRuntime.ReadMetadata(e);
                            checkedMetadata.ResumeStatus = torrent.ResumeStatus;
                            Record.Torrent = torrent = checkedMetadata;
                            Record.FileName = torrent.Name;
                            torrent.FilePriorities = priorities.ToList();
                            Record.Size = torrent.SelectedSize;
                            break;
                        case "resume":
                            torrent.ResumeStatus = e.GetProperty("used").GetBoolean() ? "Resume checkpoint loaded" : "Checkpoint unavailable; rechecking pieces";
                            break;
                        case "progress":
                            torrent.Received = e.GetProperty("received").GetInt64();
                            torrent.Peers = e.GetProperty("peers").GetInt32();
                            torrent.Seeds = e.GetProperty("seeds").GetInt32();
                            torrent.UploadRate = e.GetProperty("upload_rate").GetInt32();
                            Record.Status = DownloadStatus.Downloading;
                            break;
                        case "finished":
                            torrent.Received = torrent.SelectedSize;
                            var selected = torrent.Files.Where((_, i) => priorities[i] > 0).ToList();
                            Record.SavedPath = selected.Count == 1 ? Path.Combine(folder, selected[0].Path) : Path.Combine(folder, torrent.Name);
                            Record.Status = DownloadStatus.Finished;
                            Record.Finished = DateTime.Now;
                            break;
                    }
                    Changed?.Invoke(Record);
                }
            }, cancellation.Token);
        }
        catch (Exception e)
        {
            lock (gate)
            {
                if (cancellation.IsCancellationRequested) return;
                Record.Status = DownloadStatus.Failed;
                Record.Error = e.Message;
                Changed?.Invoke(Record);
            }
        }
    }
    public void Pause() { lock (gate) { cancellation.Cancel(); Record.Status = DownloadStatus.Paused; Changed?.Invoke(Record); } }
    public void Discard() { lock (gate) { discarded = true; cancellation.Cancel(); } } // User files are never deleted by native-engine cleanup.
}

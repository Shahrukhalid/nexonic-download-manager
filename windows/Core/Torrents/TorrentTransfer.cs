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
        // The script also stops on stdin EOF. Kill on cancellation so a native worker cannot outlive pause/quit.
        using var stop = cancellation.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });
        try
        {
            var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            var sawEvent = false;
            while (await process.StandardOutput.ReadLineAsync(cancellation) is { } line)
            {
                if (line.Length > 4 * 1024 * 1024) throw new InvalidDataException("Torrent response exceeds the size limit.");
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                sawEvent = true;
                if (e.GetProperty("event").GetString() == "error") throw new InvalidOperationException(e.GetProperty("message").GetString());
                onEvent(e);
            }
            await process.WaitForExitAsync(cancellation);
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
    readonly DownloadContext context;
    readonly CancellationTokenSource cancellation = new();
    public TorrentTransfer(DownloadRecord record, DownloadContext context) { Record = record; this.context = context; }
    public Task StartAsync() => Task.Run(RunAsync);
    async Task RunAsync()
    {
        try
        {
            var settings = new AppSettings { TorrentsEnabled = context.TorrentsEnabled, Proxy = context.Proxy,
                SpeedLimitKBps = context.GlobalSpeedLimitKBps, Network = Network.Options };
            if (TorrentRuntime.Unavailable(settings) is { } reason) throw new InvalidOperationException(reason);
            var torrent = Record.Torrent ?? throw new InvalidOperationException("Torrent metadata is missing.");
            lock (gate)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Record.Status = DownloadStatus.Probing;
                Changed?.Invoke(Record);
            }
            // Isolated per-task directory avoids overwriting unrelated files or another torrent's data.
            var folder = Path.Combine(Record.Folder, "Nexonic torrents", Record.Id.ToString("N"));
            await TorrentRuntime.RunAsync(new { action = "download", metainfo = torrent.Metainfo, folder,
                limit = (long)Math.Clamp(Record.SpeedLimitKBps ?? 0, 0, 2000000) * 1024 }, e =>
            {
                lock (gate)
                {
                    if (cancellation.IsCancellationRequested) return;
                    switch (e.GetProperty("event").GetString())
                    {
                        case "metadata":
                            // Native validation is authoritative even if a saved history/backup was edited.
                            var checkedMetadata = TorrentRuntime.ReadMetadata(e);
                            Record.Torrent = torrent = checkedMetadata;
                            Record.FileName = torrent.Name;
                            Record.Size = torrent.Size;
                            break;
                        case "progress":
                            torrent.Received = e.GetProperty("received").GetInt64();
                            torrent.Peers = e.GetProperty("peers").GetInt32();
                            torrent.Seeds = e.GetProperty("seeds").GetInt32();
                            torrent.UploadRate = e.GetProperty("upload_rate").GetInt32();
                            Record.Status = DownloadStatus.Downloading;
                            break;
                        case "finished":
                            torrent.Received = torrent.Size;
                            Record.SavedPath = torrent.Files.Count == 1 ? Path.Combine(folder, torrent.Files[0].Path) : Path.Combine(folder, torrent.Name);
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
    public void Discard() { lock (gate) cancellation.Cancel(); } // User files are never deleted by native-engine cleanup.
}

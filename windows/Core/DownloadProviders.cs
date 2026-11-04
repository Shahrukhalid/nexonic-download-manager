namespace Nexonic.Core;

public enum DownloadEngine { Auto, File, Hls, Video, Torrent, Unsupported }

/// <summary>Network-free routing. Unsupported protocols remain explicit rather than falling back to HTTP.</summary>
public static class SmartDownload
{
    public static DownloadEngine Detect(string source)
    {
        source = source.Trim();
        if (source.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathFullyQualified(source) && source.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) return DownloadEngine.Torrent;
        if (!new DownloadJob(source).IsValid) return DownloadEngine.Unsupported;
        if (new Uri(source).AbsolutePath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) return DownloadEngine.Torrent;
        if (SiteVideo.IsSupported(source)) return DownloadEngine.Video;
        return HLS.IsPlaylistUrl(source) ? DownloadEngine.Hls : DownloadEngine.File;
    }
}

public sealed record ProviderCapabilities(bool PauseResume, bool PieceVerification, bool PeerStatistics, bool SharedSpeedLimit);
public sealed record DownloadContext(int Connections, SpeedLimiter Limiter, bool CategoryFolders,
    ProxySettings Proxy, TransferOptions Options, bool TorrentsEnabled = false, int GlobalSpeedLimitKBps = 0);

/// <summary>Every provider returns the existing IDownloader task used by queues and progress windows.</summary>
public interface IDownloadProvider
{
    DownloadEngine Engine { get; }
    ProviderCapabilities Capabilities { get; }
    IDownloader Create(DownloadRecord record, DownloadContext context);
}

public sealed class DownloadProviders
{
    readonly Dictionary<DownloadEngine, IDownloadProvider> providers;
    public DownloadProviders(IEnumerable<IDownloadProvider>? additional = null)
    {
        providers = new IDownloadProvider[] { new FileProvider(), new HlsProvider(), new VideoProvider(), new TorrentProvider() }
            .Concat(additional ?? []).ToDictionary(p => p.Engine);
    }
    public IDownloadProvider For(DownloadEngine engine) => providers.TryGetValue(engine, out var p)
        ? p : throw new NotSupportedException($"No provider is installed for {engine}.");
    public IDownloader Create(DownloadRecord record, DownloadContext context) => For(EngineOf(record)).Create(record, context);
    public static DownloadEngine EngineOf(DownloadRecord r) => r.Torrent != null ? DownloadEngine.Torrent
        : r.SiteVideo == true ? DownloadEngine.Video : r.IsStream == true ? DownloadEngine.Hls : DownloadEngine.File;

    sealed class FileProvider : IDownloadProvider
    {
        public DownloadEngine Engine => DownloadEngine.File;
        public ProviderCapabilities Capabilities => new(true, false, false, true);
        public IDownloader Create(DownloadRecord r, DownloadContext c) => new Transfer(r, c.Connections, c.Limiter, c.CategoryFolders, c.Proxy, c.Options);
    }
    sealed class HlsProvider : IDownloadProvider
    {
        public DownloadEngine Engine => DownloadEngine.Hls;
        public ProviderCapabilities Capabilities => new(true, false, false, true);
        public IDownloader Create(DownloadRecord r, DownloadContext c) => new StreamTransfer(r, c.Connections, c.Limiter, c.CategoryFolders, c.Proxy);
    }
    sealed class VideoProvider : IDownloadProvider
    {
        public DownloadEngine Engine => DownloadEngine.Video;
        public ProviderCapabilities Capabilities => new(true, false, false, false);
        public IDownloader Create(DownloadRecord r, DownloadContext c) => new SiteVideoTransfer(r, c.CategoryFolders, c.GlobalSpeedLimitKBps, c.Proxy);
    }
    sealed class TorrentProvider : IDownloadProvider
    {
        public DownloadEngine Engine => DownloadEngine.Torrent;
        public ProviderCapabilities Capabilities => new(true, true, true, false);
        public IDownloader Create(DownloadRecord r, DownloadContext c) => new TorrentTransfer(r, c);
    }
}

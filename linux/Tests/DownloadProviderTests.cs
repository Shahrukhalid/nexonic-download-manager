using System.Text.Json;
using Nexonic.Core;
using Xunit;

namespace Nexonic.Linux.Tests;

public sealed class DownloadProviderTests
{
    [Theory]
    [InlineData("https://example.org/file.zip", DownloadEngine.File)]
    [InlineData("https://example.org/a.m3u8?token=x", DownloadEngine.Hls)]
    [InlineData("https://www.youtube.com/watch?v=test", DownloadEngine.Video)]
    [InlineData("magnet:?xt=urn:btih:0123456789012345678901234567890123456789", DownloadEngine.Torrent)]
    [InlineData("/home/user/file.torrent", DownloadEngine.Torrent)]
    [InlineData("https://example.org/file.torrent", DownloadEngine.Torrent)]
    [InlineData("sftp://example.org/file", DownloadEngine.Unsupported)]
    public void DetectsWithoutNetwork(string source, DownloadEngine expected) => Assert.Equal(expected, SmartDownload.Detect(source));

    [Fact]
    public void ExistingRecordsSelectExistingEngines()
    {
        var providers = new DownloadProviders();
        var context = new DownloadContext(1, new(), false, new() { Mode = ProxyMode.None }, new());
        Assert.IsType<Transfer>(providers.Create(new() { Url = "https://example.org/file" }, context));
        Assert.IsType<StreamTransfer>(providers.Create(new() { Url = "https://example.org/a.m3u8", IsStream = true }, context));
        Assert.IsType<SiteVideoTransfer>(providers.Create(new() { Url = "https://youtube.com/watch?v=x", SiteVideo = true }, context));
        Assert.True(providers.For(DownloadEngine.Torrent).Capabilities.PieceVerification);
        Assert.False(providers.For(DownloadEngine.Torrent).Capabilities.SharedSpeedLimit);
        Assert.Throws<NotSupportedException>(() => providers.For(DownloadEngine.Unsupported));
    }

    [Fact]
    public async Task TorrentRequiresOptInAndDoesNotBroadenWebEntryPoints()
    {
        Assert.False(new AppSettings().TorrentsEnabled);
        Assert.False(new DownloadJob("magnet:?xt=urn:btih:test").IsValid);
        Assert.False(new DownloadJob("/tmp/test.torrent").IsValid);
        var record = new DownloadRecord { Torrent = new(), Cookies = "secret", Username = "private" };
        var context = new DownloadContext(1, new(), false, new(), new());
        var task = new DownloadProviders().Create(record, context);
        await task.StartAsync();
        Assert.Equal(DownloadStatus.Failed, record.Status);
        Assert.Contains("torrents first", record.Error);
        Assert.Null(record.SavedPath);
    }

    [Fact]
    public void BackupCannotSmuggleTorrentTasksOrLeakTheirTrackerMetadata()
    {
        var forged = new DownloadRecord { Url = "https://example.org/file", Torrent = new() { Metainfo = "tracker-secret" } };
        var exported = Backup.Export(new(), Array.Empty<DownloadQueue>(), new[] { forged }, "test");
        Assert.DoesNotContain("tracker-secret", exported);
        var incoming = JsonSerializer.Serialize(new Backup.Data { Format = Backup.Format, Version = Backup.Version, Downloads = new() { forged } });
        Assert.Empty(Backup.Parse(incoming).Downloads!);
    }

    [Fact]
    public void FileSelectionPersistsAndRejectsInvalidPriorities()
    {
        var torrent = new TorrentMetadata { Files = new() { new("first.bin", 100), new("second.bin", 200) }, FilePriorities = new() { 0, 7 } };
        var restored = JsonSerializer.Deserialize<TorrentMetadata>(JsonSerializer.Serialize(torrent))!;
        Assert.Equal(200, restored.SelectedSize);
        Assert.Equal(new[] { 0, 7 }, restored.Priorities());
        restored.FilePriorities = new() { 0, 0 };
        Assert.Throws<ArgumentException>(() => restored.Priorities());
        restored.FilePriorities = new() { 4 };
        Assert.Throws<ArgumentException>(() => restored.Priorities());
        restored.FilePriorities = new() { 2, 4 };
        Assert.Throws<ArgumentException>(() => restored.Priorities());
    }

    [Fact]
    public void CheckpointsReplaceAtomicallyWithPrivatePermissionsAndRejectOversizedData()
    {
        using var temp = new TempHome();
        var path = Path.Combine(temp.Path, "resume", "torrent.resume");
        TorrentCheckpoint.Write(path, Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        TorrentCheckpoint.Write(path, Convert.ToBase64String(new byte[] { 4, 5 }));
        Assert.Equal(new byte[] { 4, 5 }, Convert.FromBase64String(File.ReadAllText(path)));
        Assert.False(File.Exists(path + ".tmp"));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Throws<InvalidDataException>(() => TorrentCheckpoint.Write(path, new string('a', 3 * 1024 * 1024 + 1)));
        Assert.Equal(new byte[] { 4, 5 }, Convert.FromBase64String(File.ReadAllText(path)));
    }

    [Fact]
    public void TorrentProgressSurvivesHistoryRoundTrip()
    {
        var original = new DownloadRecord { Size = 100, Torrent = new() { Name = "test", Received = 25, Files = new() { new("test", 100) } } };
        var restored = JsonSerializer.Deserialize<DownloadRecord>(JsonSerializer.Serialize(original))!;
        Assert.Equal(.25, restored.Progress);
        Assert.Equal(25, restored.Received);
        Assert.Equal("test", restored.Torrent!.Files[0].Path);
        Assert.Equal(DownloadEngine.Torrent, DownloadProviders.EngineOf(restored));
    }

    [Fact]
    public void TorrentsFollowTheProxyAndSpeedLimitAndRefuseDnsOverHttps()
    {
        var s = new AppSettings { TorrentsEnabled = true, SpeedLimitKBps = 5 };
        foreach (var mode in Enum.GetValues<ProxyMode>()) { s.Proxy.Mode = mode; Assert.Null(TorrentRuntime.Unavailable(s)); }
        Assert.Null(TorrentRuntime.Proxy(new ProxySettings { Mode = ProxyMode.None }));
        Assert.Null(TorrentRuntime.Proxy(new ProxySettings { Mode = ProxyMode.Socks, Host = "" }));
        var socks = System.Text.Json.JsonSerializer.Serialize(TorrentRuntime.Proxy(new ProxySettings { Mode = ProxyMode.Socks, Host = "proxy.example", Port = 1080 }));
        Assert.Contains("\"socks5\"", socks); Assert.Contains("1080", socks);
        Assert.Equal(200, TorrentRuntime.DownloadLimit(300, 200));
        Assert.Equal(100, TorrentRuntime.DownloadLimit(100, 200));
        Assert.Equal(200, TorrentRuntime.DownloadLimit(null, 200));
        Assert.Equal(0, TorrentRuntime.DownloadLimit(0, 0));
        s.Network.DnsOverHttps = "https://cloudflare-dns.com/dns-query";
        Assert.Contains("DNS", TorrentRuntime.Unavailable(s));
    }
}

public sealed class LinuxTorrentFactAttribute : FactAttribute
{
    public LinuxTorrentFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires a bundled native Linux engine; exercised by both Linux CI architectures."; }
}

public sealed class NativeTorrentRuntimeTests
{
    [LinuxTorrentFact]
    public async Task EmbeddedRuntimeReadsMetadataAndVerifiesExistingPieces()
    {
        using var temp = new TempHome();
        const string encoded = "ZDQ6aW5mb2Q2Omxlbmd0aGkxM2U0Om5hbWUxMTpmaXh0dXJlLmJpbjEyOnBpZWNlIGxlbmd0aGkxNjM4NGU2OnBpZWNlczIwOqYWPxfthLnM+mxpSHvcE8OkwCtINzpwcml2YXRlaTFlZWU=";
        var source = Path.Combine(temp.Path, "fixture.torrent");
        await File.WriteAllBytesAsync(source, Convert.FromBase64String(encoded));
        var metadata = await TorrentRuntime.PreviewAsync(source);
        Assert.Equal("fixture.bin", metadata.Name);
        Assert.Equal(13, metadata.Size);
        Assert.Single(metadata.Files);
        Assert.NotEmpty(metadata.InfoHash);
        var record = new DownloadRecord { Url = source, Folder = temp.Path, FileName = metadata.Name, Size = metadata.Size, Torrent = metadata };
        var destination = Path.Combine(temp.Path, "Nexonic torrents", record.Id.ToString("N"));
        Directory.CreateDirectory(destination);
        // Resume must verify existing pieces through the actual native engine.
        await File.WriteAllTextAsync(Path.Combine(destination, "fixture.bin"), "Nexonic test\n");
        var context = new DownloadContext(1, new(), false, new() { Mode = ProxyMode.None }, new(), TorrentsEnabled: true);
        var task = new DownloadProviders().Create(record, context);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var stop = timeout.Token.Register(task.Pause);
        await task.StartAsync().WaitAsync(TimeSpan.FromSeconds(35));
        Assert.Equal(DownloadStatus.Finished, record.Status);
        Assert.Equal(13, record.Received);
        Assert.Equal("Nexonic test\n", await File.ReadAllTextAsync(record.SavedPath!));
        Assert.Equal(Path.Combine(destination, "fixture.bin"), record.SavedPath);
        var checkpointPath = Path.Combine(AppPaths.Parts, record.Id.ToString("N"), "native", "torrent-payload.resume");
        Assert.True(File.Exists(checkpointPath));
        Assert.NotEmpty(await File.ReadAllBytesAsync(checkpointPath));
        Directory.Delete(Path.GetDirectoryName(checkpointPath)!, true);
    }
}

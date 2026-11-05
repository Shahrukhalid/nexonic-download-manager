using System.Security.Cryptography;
using System.Text.Json;
using Nexonic.Core;

var library = Path.GetFullPath(args.Single());
// Resolve the macOS /var -> /private/var alias; production rejects symlink destinations.
var temporary = Path.GetTempPath();
if (OperatingSystem.IsMacOS() && temporary.StartsWith("/var/")) temporary = "/private" + temporary;
var root = Path.Combine(temporary, "nexonic-managed-torrent-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task<JsonElement> Wait(NativeTorrentSession engine, string id, Func<JsonElement, bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    while (true)
    {
        var state = engine.Invoke(new { action = "status", id });
        Require(state.GetProperty("state").GetString() != "failed", state.GetProperty("error").GetString() ?? "Torrent failed");
        if (condition(state)) return state;
        try { await Task.Delay(100, timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            var diagnostic = state.Deserialize<Dictionary<string, JsonElement>>()!;
            diagnostic.Remove("metainfo");
            throw new TimeoutException($"Waiting for {id}: {JsonSerializer.Serialize(diagnostic)}");
        }
    }
}
try
{
    var source = Path.Combine(root, "original", "Legal-試験");
    Directory.CreateDirectory(source);
    var bytes = new byte[4 * 1024 * 1024];
    new Random(42).NextBytes(bytes);
    await File.WriteAllBytesAsync(Path.Combine(source, "selected.bin"), bytes);
    await File.WriteAllTextAsync(Path.Combine(source, "skipped.txt"), "Legal Nexonic test data\n");
    using var seed = new NativeTorrentSession(Path.Combine(root, "seed-state"), true, false, library);
    var metadata = seed.Invoke(new { action = "create_torrent", path = source }).GetProperty("metainfo").GetString()!;
    seed.Invoke(new { action = "add", id = "seed", source = "fixture", metainfo = metadata, preview = false,
        destination = Path.GetDirectoryName(source), seed = true, ratio_limit = 0, upload_limit = 0 });
    seed.Invoke(new { action = "resume", id = "seed" });
    var seedStatus = await Wait(seed, "seed", s => s.GetProperty("state").GetString() == "seeding");
    var port = seed.Invoke(new { action = "list" }).GetProperty("listen_port").GetInt32();
    // connect_peer is a one-shot hint (no DHT or trackers here): repeat it each second until a peer is attached.
    var ticks = 0;
    bool Hint(NativeTorrentSession engine, string id, JsonElement s)
    {
        if (s.GetProperty("peers").GetInt32() == 0 && ++ticks % 10 == 0) engine.Invoke(new { action = "connect_peer", id, port });
        return true;
    }
    var files = seedStatus.GetProperty("files").EnumerateArray().ToArray();
    var priorities = files.Select(f => f.GetProperty("path").GetString()!.EndsWith("selected.bin") ? 7 : 0).ToArray();
    var stateDirectory = Path.Combine(root, "downloader-state");
    var destination = Path.Combine(root, "downloaded");
    using (var client = new NativeTorrentSession(stateDirectory, true, false, library))
    {
        client.Invoke(new { action = "add", id = "preview", source = seedStatus.GetProperty("magnet").GetString() });
        client.Invoke(new { action = "connect_peer", id = "preview", port });
        var preview = await Wait(client, "preview", s => Hint(client, "preview", s) && s.GetProperty("state").GetString() == "preview");
        Require(!preview.GetProperty("payload_complete").GetBoolean(), "Metadata falsely completed content");
        bool refused = false;
        try { client.Invoke(new { action = "resume", id = "preview" }); } catch (InvalidOperationException) { refused = true; }
        Require(refused, "Metadata preview accepted payload download");
        client.Invoke(new { action = "remove", id = "preview" });
        client.Invoke(new { action = "add", id = "download", source = "fixture", metainfo = metadata,
            preview = false, destination, priorities, download_limit = 262144 });
        client.Invoke(new { action = "resume", id = "download" });
        client.Invoke(new { action = "connect_peer", id = "download", port });
        await Wait(client, "download", s => Hint(client, "download", s) && s.GetProperty("received").GetInt64() > 65536);
        client.Invoke(new { action = "pause", id = "download" });
        client.Invoke(new { action = "flush" });
    }
    using (var client = new NativeTorrentSession(stateDirectory, true, false, library))
    {
        client.Invoke(new { action = "restore", id = "download" });
        client.Invoke(new { action = "configure", id = "download", download_limit = 0 });
        client.Invoke(new { action = "resume", id = "download" });
        client.Invoke(new { action = "connect_peer", id = "download", port });
        var complete = await Wait(client, "download", s => Hint(client, "download", s) && s.GetProperty("state").GetString() == "complete");
        Require(complete.GetProperty("payload_complete").GetBoolean(), "Final verification missing");
        var selectedPath = files.Single(f => f.GetProperty("path").GetString()!.EndsWith("selected.bin")).GetProperty("path").GetString()!;
        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var actual = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(destination, selectedPath)));
        Require(Convert.ToHexString(actual).ToLowerInvariant() == expected, "Downloaded bytes differ");
        Require(complete.GetProperty("sha256").GetProperty(selectedPath).GetString() == expected, "Native SHA-256 differs");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => client.Invoke(new { action = "status", id = "download" }))));
        client.Invoke(new { action = "remove", id = "download" });
        Require(File.Exists(Path.Combine(destination, selectedPath)), "Removing a torrent deleted user data");
        client.Dispose();
        bool disposed = false;
        try { client.Invoke(new { action = "list" }); } catch (ObjectDisposedException) { disposed = true; }
        Require(disposed, "Disposed session accepted calls");
    }
    // Exercise the actual queue provider as well as its C ABI, using pre-existing legal pieces.
    const string fixture = "ZDQ6aW5mb2Q2Omxlbmd0aGkxM2U0Om5hbWUxMTpmaXh0dXJlLmJpbjEyOnBpZWNlIGxlbmd0aGkxNjM4NGU2OnBpZWNlczIwOqYWPxfthLnM+mxpSHvcE8OkwCtINzpwcml2YXRlaTFlZWU=";
    var fixturePath = Path.Combine(root, "provider.torrent");
    await File.WriteAllBytesAsync(fixturePath, Convert.FromBase64String(fixture));
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var httpUrl = $"http://127.0.0.1:{((System.Net.IPEndPoint)listener.LocalEndpoint).Port}/fixture.torrent";
    using var httpTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
    var serve = Task.Run(async () =>
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(httpTimeout.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (await reader.ReadLineAsync(httpTimeout.Token) is { Length: > 0 }) { }
            var data = Convert.FromBase64String(fixture);
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/x-bittorrent\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n"), httpTimeout.Token);
            await stream.WriteAsync(data, httpTimeout.Token);
        }
        finally { listener.Stop(); }
    });
    Require(SmartDownload.Detect(httpUrl) == DownloadEngine.Torrent, "HTTP torrent routed as an ordinary file");
    var record = new DownloadRecord { Url = httpUrl, Folder = root, Torrent = new TorrentMetadata { MetadataPending = true } };
    var recordFolder = Path.Combine(root, "Nexonic torrents", record.Id.ToString("N"));
    Directory.CreateDirectory(recordFolder);
    await File.WriteAllTextAsync(Path.Combine(recordFolder, "fixture.bin"), "Nexonic test\n");
    var provider = new DownloadProviders().Create(record, new DownloadContext(1, new(), false, new() { Mode = ProxyMode.None }, new(), TorrentsEnabled: true));
    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35)))
    using (timeout.Token.Register(provider.Pause)) await provider.StartAsync().WaitAsync(TimeSpan.FromSeconds(40));
    await serve;
    Require(record.Status == DownloadStatus.Finished && record.Torrent!.PayloadComplete && record.Received == 13, "Queue provider failed native completion: " + record.Error);
    Require(record.SavedPath == Path.Combine(recordFolder, "fixture.bin"), "Provider saved path differs");
    Require(record.Torrent.Sha256.Count == 1, "Provider omitted verified hashes");
    Directory.Delete(Path.Combine(AppPaths.Parts, record.Id.ToString("N")), true);
    var descriptor = Path.Combine(root, "fixture.torrent");
    await File.WriteAllBytesAsync(descriptor, Convert.FromBase64String(metadata));
    Require((await TorrentSource.ReadMetadataAsync(descriptor)).SequenceEqual(Convert.FromBase64String(metadata)), "Descriptor bytes changed");
    await using (var oversized = File.Create(Path.Combine(root, "oversized.torrent"))) oversized.SetLength(TorrentSource.MaximumMetadataBytes + 1);
    bool limited = false;
    try { await TorrentSource.ReadMetadataAsync(Path.Combine(root, "oversized.torrent")); } catch (InvalidDataException) { limited = true; }
    Require(limited, "Oversized metadata accepted");
    Console.WriteLine("PASS: managed native real transfer, UTF-8 paths, magnet preview, file selection, restart, SHA-256, concurrent calls, safe removal, HTTP descriptor-to-payload completion and metadata bounds");
}
finally { Directory.Delete(root, true); }

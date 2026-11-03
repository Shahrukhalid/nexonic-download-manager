using System.Net.Sockets;
using System.Text;
using Nexonic.Core;
using Xunit;

namespace Nexonic.Tests;

public class LocalApiTests
{
    sealed class Fake : IDownloadControl
    {
        public readonly List<DownloadRecord> Items = new();
        public readonly List<DownloadQueue> Queues = new() { DownloadQueue.Main() };
        public IReadOnlyList<DownloadRecord> AllDownloads() => Items;
        public IReadOnlyList<DownloadQueue> AllQueues() => Queues;
        public string QueueName(string id) => Queues.FirstOrDefault(q => q.Id == id)?.Name ?? "Main queue";
        public List<Guid> AddFromApi(IReadOnlyList<string> urls, string? fileName, string? folder, string? queue, bool start)
        {
            var added = urls.Select(u => new DownloadRecord { Url = u, FileName = fileName ?? FileNames.FromUrl(u), Folder = folder ?? "/tmp",
                Status = start ? DownloadStatus.Queued : DownloadStatus.Paused, Cookies = "secret-cookie", Password = "secret-pw" }).ToList();
            Items.AddRange(added);
            return added.Select(r => r.Id).ToList();
        }
        public bool PauseById(Guid id) => Set(id, DownloadStatus.Paused);
        public bool ResumeById(Guid id) => Set(id, DownloadStatus.Queued);
        public bool RemoveById(Guid id) => Items.RemoveAll(r => r.Id == id) > 0;
        public bool StartQueueById(string id) => Queues.Any(q => q.Id == id);
        public bool StopQueueById(string id) => Queues.Any(q => q.Id == id);
        bool Set(Guid id, DownloadStatus s) { if (Items.FirstOrDefault(r => r.Id == id) is not { } r) return false; r.Status = s; return true; }
    }

    const string Key = "0123456789abcdef0123456789abcdef";
    static Dictionary<string, string> Headers(string? auth = "Bearer " + Key, string host = "127.0.0.1:47812", string? origin = null)
    {
        var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Host"] = host };
        if (auth != null) h["Authorization"] = auth;
        if (origin != null) h["Origin"] = origin;
        return h;
    }

    [Fact]
    public void OnlyTheKeyHolderOnThisComputerGetsIn()
    {
        Assert.Null(LocalApi.Check(Headers(), Key, 47812));
        Assert.Null(LocalApi.Check(Headers(host: "localhost:47812"), Key, 47812));
        Assert.Equal(401, LocalApi.Check(Headers(auth: null), Key, 47812)!.Value.Status);
        Assert.Equal(401, LocalApi.Check(Headers(auth: "Bearer wrong"), Key, 47812)!.Value.Status);
        Assert.Equal(403, LocalApi.Check(Headers(origin: "https://evil.example"), Key, 47812)!.Value.Status);
        Assert.Equal(403, LocalApi.Check(Headers(host: "evil.example:47812"), Key, 47812)!.Value.Status);
        Assert.Equal(401, LocalApi.Check(Headers(auth: "Bearer "), "", 47812)!.Value.Status);
    }

    [Fact]
    public void AddsListsPausesAndRemoves()
    {
        var c = new Fake();
        var (status, body) = LocalApi.Route("POST", "/v1/downloads", "{\"urls\":[\"https://example.com/a.zip\",\"https://example.com/b.zip\"],\"start\":false}", c);
        Assert.Equal(201, status);
        Assert.Equal(2, c.Items.Count);
        Assert.All(c.Items, r => Assert.Equal(DownloadStatus.Paused, r.Status));
        var list = System.Text.Json.JsonSerializer.Serialize(LocalApi.Route("GET", "/v1/downloads", "", c).Body);
        Assert.Contains("a.zip", list);
        Assert.DoesNotContain("secret", list);
        var id = c.Items[0].Id;
        Assert.Equal(200, LocalApi.Route("POST", $"/v1/downloads/{id}/resume", "", c).Status);
        Assert.Equal(DownloadStatus.Queued, c.Items[0].Status);
        Assert.Equal(200, LocalApi.Route("DELETE", $"/v1/downloads/{id}", "", c).Status);
        Assert.Single(c.Items);
        Assert.Equal(404, LocalApi.Route("GET", $"/v1/downloads/{Guid.NewGuid()}", "", c).Status);
        Assert.Equal(200, LocalApi.Route("POST", "/v1/queues/main/stop", "", c).Status);
        Assert.Equal(404, LocalApi.Route("POST", "/v1/queues/nope/start", "", c).Status);
        Assert.Equal(405, LocalApi.Route("PUT", "/v1/downloads", "", c).Status);
        Assert.Equal(404, LocalApi.Route("GET", "/v2/anything", "", c).Status);
    }

    [Fact]
    public void RejectsBadInput()
    {
        var c = new Fake();
        Assert.Equal(400, LocalApi.Route("POST", "/v1/downloads", "not json", c).Status);
        Assert.Equal(400, LocalApi.Route("POST", "/v1/downloads", "{\"url\":\"file:///etc/passwd\"}", c).Status);
        Assert.Equal(400, LocalApi.Route("POST", "/v1/downloads", "{\"url\":\"https://e.com/a\",\"folder\":\"relative/path\"}", c).Status);
        Assert.Equal(400, LocalApi.Route("POST", "/v1/downloads", "{\"url\":\"https://e.com/a\",\"fileName\":\"../x\"}", c).Status);
        var many = "{\"urls\":[" + string.Join(",", Enumerable.Range(0, 201).Select(i => $"\"https://e.com/{i}\"")) + "]}";
        Assert.Equal(413, LocalApi.Route("POST", "/v1/downloads", many, c).Status);
        Assert.Empty(c.Items);
    }

    [Fact]
    public async Task ServesOverLoopbackWithTheKey()
    {
        var c = new Fake();
        var port = 47000 + Random.Shared.Next(2000);
        using var api = new LocalApi(port, Key, c, f => Task.FromResult(f()));
        api.Start();
        async Task<string> Send(string request)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", port);
            var s = tcp.GetStream();
            await s.WriteAsync(Encoding.ASCII.GetBytes(request));
            using var reader = new StreamReader(s);
            return await reader.ReadToEndAsync();
        }
        var body = "{\"url\":\"https://example.com/file.iso\"}";
        var ok = await Send($"POST /v1/downloads HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nAuthorization: Bearer {Key}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n{body}");
        Assert.StartsWith("HTTP/1.1 201", ok);
        Assert.Single(c.Items);
        var denied = await Send($"GET /v1/downloads HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 401", denied);
        var page = await Send($"GET /v1/downloads HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nOrigin: https://evil.example\r\nAuthorization: Bearer {Key}\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 403", page);
    }
}

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexonic.Core;

/// <summary>Options → Automation. Off until the user turns it on; the key is saved encrypted like other secrets.</summary>
public sealed class AutomationSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 47812;
    string key = "";
    public string Key { get => key; set => key = value ?? ""; }

    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>What the local API may do with the app's downloads. Every call runs on the app's UI thread.</summary>
public interface IDownloadControl
{
    IReadOnlyList<DownloadRecord> AllDownloads();
    IReadOnlyList<DownloadQueue> AllQueues();
    string QueueName(string id);
    /// <summary>Adds web links; returns the new downloads' ids.</summary>
    List<Guid> AddFromApi(IReadOnlyList<string> urls, string? fileName, string? folder, string? queue, bool start);
    bool PauseById(Guid id);
    bool ResumeById(Guid id);
    bool RemoveById(Guid id);
    bool StartQueueById(string id);
    bool StopQueueById(string id);
}

/// <summary>
/// Optional automation API for scripts and other programs on this computer (Options → Automation). Listens only on
/// 127.0.0.1, needs the API key in an Authorization: Bearer header, refuses requests that come from web pages (Origin
/// header, foreign Host), and never returns cookies, passwords or the key. See docs/API.md.
/// </summary>
public sealed class LocalApi : IDisposable
{
    public const int MaxBody = 64 * 1024;
    public const int MaxLinks = 200;
    readonly TcpListener listener;
    readonly string key;
    readonly int port;
    readonly Func<Func<(int, object)>, Task<(int, object)>> onUi;
    readonly IDownloadControl control;
    readonly CancellationTokenSource stop = new();

    /// <param name="onUi">Runs a call on the app's UI thread.</param>
    public LocalApi(int port, string key, IDownloadControl control, Func<Func<(int, object)>, Task<(int, object)>> onUi)
    {
        this.port = port;
        this.key = key;
        this.control = control;
        this.onUi = onUi;
        listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    async Task AcceptLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); }
            catch { return; }
            _ = Task.Run(() => Serve(client));
        }
    }

    async Task Serve(TcpClient client)
    {
        using var connection = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var stream = client.GetStream();
        int status;
        object body;
        try
        {
            var req = await ReadRequest(stream, timeout.Token);
            if (req == null) { status = 400; body = Error("Bad request"); }
            else if (Check(req.Headers, key, port) is { } refused) { status = refused.Status; body = Error(refused.Message); }
            else (status, body) = await onUi(() => Route(req.Method, req.Path, req.Body, control));
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { return; }
        catch { status = 500; body = Error("Internal error"); }
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(body, Json);
            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {Reason(status)}\r\nContent-Type: application/json; charset=utf-8\r\n" +
                                              $"Content-Length: {json.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, timeout.Token);
            await stream.WriteAsync(json, timeout.Token);
        }
        catch { }
    }

    public sealed record Request(string Method, string Path, Dictionary<string, string> Headers, string Body);

    /// <summary>Reads one HTTP/1.1 request (headers up to 16 KB, a body up to 64 KB).</summary>
    public static async Task<Request?> ReadRequest(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024 + MaxBody];
        var filled = 0;
        int headerEnd;
        while (true)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(filled, Math.Min(buffer.Length - filled, 16 * 1024)), ct);
            if (n == 0) return null;
            filled += n;
            headerEnd = IndexOf(buffer, filled, "\r\n\r\n"u8);
            if (headerEnd >= 0) break;
            if (filled >= 16 * 1024) return null;
        }
        var lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length != 3 || !first[2].StartsWith("HTTP/1.")) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) return null;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        var length = headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var l) ? l : 0;
        if (length < 0 || length > MaxBody || headers.ContainsKey("Transfer-Encoding")) return null;
        var start = headerEnd + 4;
        while (filled - start < length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(filled, start + length - filled), ct);
            if (n == 0) return null;
            filled += n;
        }
        return new Request(first[0].ToUpperInvariant(), first[1], headers, Encoding.UTF8.GetString(buffer, start, length));
    }

    static int IndexOf(byte[] data, int length, ReadOnlySpan<byte> pattern) => data.AsSpan(0, length).IndexOf(pattern);

    /// <summary>Null when the request may go on; otherwise why not.</summary>
    public static (int Status, string Message)? Check(IReadOnlyDictionary<string, string> headers, string key, int port)
    {
        // Web pages can reach 127.0.0.1 too: they always send Origin, and DNS-rebinding pages send a foreign Host.
        if (headers.ContainsKey("Origin")) return (403, "Requests from web pages are not allowed");
        var host = headers.TryGetValue("Host", out var h) ? h.ToLowerInvariant() : "";
        if (host != $"127.0.0.1:{port}" && host != $"localhost:{port}") return (403, "Use http://127.0.0.1:" + port);
        var given = headers.TryGetValue("Authorization", out var a) && a.StartsWith("Bearer ", StringComparison.Ordinal) ? a[7..].Trim() : "";
        if (key.Length < 16 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(key)))
            return (401, "Missing or wrong API key (Authorization: Bearer <key>)");
        return null;
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    static object Error(string message) => new { error = message };

    static string Reason(int status) => status switch
    {
        200 => "OK", 201 => "Created", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found",
        405 => "Method Not Allowed", 413 => "Payload Too Large", _ => "Error",
    };

    /// <summary>A download as the API shows it (never cookies, passwords or the user agent).</summary>
    public static object Describe(DownloadRecord r, Func<string, string> queueName) => new
    {
        id = r.Id,
        url = r.Url,
        fileName = r.FileName,
        folder = r.Folder,
        status = r.Status.ToString().ToLowerInvariant(),
        size = r.Size,
        received = r.Received,
        progress = Math.Round(r.Progress * 100, 1),
        queue = queueName(r.Queue),
        queueId = r.Queue,
        added = r.Added,
        finished = r.Finished,
        savedPath = r.SavedPath,
        error = r.Error,
    };

    /// <summary>The API's endpoints. Runs on the UI thread.</summary>
    public static (int Status, object Body) Route(string method, string rawPath, string body, IDownloadControl c)
    {
        var path = rawPath.Split('?')[0].TrimEnd('/');
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "v1") return (404, Error("Unknown address. See docs/API.md"));
        Guid id = Guid.Empty;
        var hasId = parts.Length >= 3 && Guid.TryParse(parts[2], out id);
        switch (parts[1], parts.Length, method)
        {
            case ("status", 2, "GET"):
            {
                var all = c.AllDownloads();
                return (200, new
                {
                    app = "Nexonic Download Manager",
                    active = all.Count(r => r.IsActive),
                    queued = all.Count(r => r.Status == DownloadStatus.Queued),
                    total = all.Count,
                });
            }
            case ("downloads", 2, "GET"):
                return (200, c.AllDownloads().Select(r => Describe(r, c.QueueName)).ToList());
            case ("downloads", 2, "POST"):
                return Add(body, c);
            case ("downloads", 3, "GET"):
                return hasId && c.AllDownloads().FirstOrDefault(r => r.Id == id) is { } one ? (200, Describe(one, c.QueueName)) : (404, Error("No such download"));
            case ("downloads", 3, "DELETE"):
                return hasId && c.RemoveById(id) ? (200, new { removed = id }) : (404, Error("No such download"));
            case ("downloads", 4, "POST") when parts[3] is "pause" or "resume":
                if (!hasId) return (404, Error("No such download"));
                var ok = parts[3] == "pause" ? c.PauseById(id) : c.ResumeById(id);
                return ok ? (200, Describe(c.AllDownloads().First(r => r.Id == id), c.QueueName)) : (404, Error("No such download"));
            case ("queues", 2, "GET"):
                return (200, c.AllQueues().Select(q => new { id = q.Id, name = q.Name, running = q.Running, maxActive = q.MaxActive }).ToList());
            case ("queues", 4, "POST") when parts[3] is "start" or "stop":
                var done = parts[3] == "start" ? c.StartQueueById(parts[2]) : c.StopQueueById(parts[2]);
                return done ? (200, new { id = parts[2], running = parts[3] == "start" }) : (404, Error("No such queue"));
            case ("status" or "downloads" or "queues", _, _):
                return (405, Error("This method is not allowed here"));
            default:
                return (404, Error("Unknown address. See docs/API.md"));
        }
    }

    static (int, object) Add(string body, IDownloadControl c)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(body); }
        catch (JsonException) { return (400, Error("The body must be JSON")); }
        if (node is not JsonObject o) return (400, Error("The body must be a JSON object"));
        string? Text(string name) => o[name] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length <= 4096 ? s : null;
        var urls = new List<string>();
        if (Text("url") is { } single) urls.Add(single);
        if (o["urls"] is JsonArray arr) urls.AddRange(arr.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : ""));
        if (urls.Count == 0) return (400, Error("Give url or urls"));
        if (urls.Count > MaxLinks) return (413, Error($"At most {MaxLinks} links per request"));
        if (urls.Any(u => !new DownloadJob(u.Trim()).IsValid)) return (400, Error("Only http and https links can be added"));
        var folder = Text("folder");
        if (folder != null && (!Path.IsPathFullyQualified(folder) || folder.Split('/', '\\').Contains(".."))) return (400, Error("folder must be a full path"));
        var fileName = Text("fileName");
        if (fileName != null && (urls.Count != 1 || FileNames.Sanitize(fileName) != fileName)) return (400, Error("fileName works with one link and must be a plain file name"));
        var start = o["start"] is not JsonValue sv || !sv.TryGetValue<bool>(out var b) || b;
        var ids = c.AddFromApi(urls.Select(u => u.Trim()).ToList(), fileName, folder, Text("queue"), start);
        return (201, new { ids });
    }

    public void Dispose()
    {
        stop.Cancel();
        try { listener.Stop(); } catch { }
    }
}

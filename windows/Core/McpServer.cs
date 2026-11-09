using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexonic.Core;

/// <summary>
/// Optional MCP server (`ndm --mcp`, standard input and output) so an assistant app can list, add, pause, resume and
/// remove downloads. Adding works always (through the inbox); the other tools use the local automation API, so they
/// need Options → Browsers → Automation turned on and its key in NDM_API_KEY (port in NDM_API_PORT, default 47812).
/// </summary>
public sealed class McpServer(Func<DownloadJob, bool> add, HttpClient? api = null)
{
    public const string Protocol = "2024-11-05";
    readonly HttpClient? api = api ?? DefaultApi();

    static HttpClient? DefaultApi()
    {
        var key = Environment.GetEnvironmentVariable("NDM_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return null;
        var port = int.TryParse(Environment.GetEnvironmentVariable("NDM_API_PORT"), out var p) && p is >= 1024 and <= 65535 ? p : 47812;
        var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}/v1/"), Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        return http;
    }

    static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => new()
    {
        ["name"] = name, ["description"] = description,
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()) },
    };

    static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

    public static JsonArray Tools() => new(
        Tool("add_download", "Download a file, video page, playlist item or magnet link with Nexonic Download Manager.",
            new JsonObject { ["url"] = Str("http(s) or magnet link"), ["folder"] = Str("Optional save folder"), ["file_name"] = Str("Optional file name") }, "url"),
        Tool("list_downloads", "List downloads with their status, progress and id.", new JsonObject()),
        Tool("pause_download", "Pause a download by id.", new JsonObject { ["id"] = Str("Download id from list_downloads") }, "id"),
        Tool("resume_download", "Resume a download by id.", new JsonObject { ["id"] = Str("Download id from list_downloads") }, "id"),
        Tool("remove_download", "Remove a download from the list by id (finished files stay on disk).", new JsonObject { ["id"] = Str("Download id from list_downloads") }, "id"));

    /// <summary>Answers one JSON-RPC message; null for notifications.</summary>
    public async Task<JsonObject?> HandleAsync(JsonObject message, CancellationToken ct = default)
    {
        var id = message["id"]?.DeepClone();
        var method = message["method"]?.GetValue<string>() ?? "";
        if (id == null) return null;
        JsonObject Result(JsonNode result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        JsonObject Fail(int code, string text) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = text } };
        switch (method)
        {
            case "initialize":
                return Result(new JsonObject
                {
                    ["protocolVersion"] = Protocol, ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "nexonic-download-manager", ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "" },
                });
            case "ping": return Result(new JsonObject());
            case "tools/list": return Result(new JsonObject { ["tools"] = Tools() });
            case "tools/call":
                var name = message["params"]?["name"]?.GetValue<string>() ?? "";
                var args = message["params"]?["arguments"] as JsonObject ?? new JsonObject();
                try { return Result(Text(await CallAsync(name, args, ct), false)); }
                catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
                { return Result(Text(e.Message, true)); }
            default: return Fail(-32601, "Unknown method");
        }
    }

    static JsonObject Text(string text, bool error) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = error,
    };

    async Task<string> CallAsync(string name, JsonObject a, CancellationToken ct)
    {
        string Arg(string key) => a[key]?.GetValue<string>()?.Trim() ?? "";
        if (name == "add_download")
        {
            var url = Arg("url");
            var magnet = url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase);
            if (!magnet && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"))
                throw new InvalidOperationException("Give an http(s) or magnet link.");
            var job = new DownloadJob(url, Arg("file_name") is { Length: > 0 } f ? f : null) { Folder = Arg("folder") is { Length: > 0 } d ? d : null };
            if (!add(job)) throw new InvalidOperationException("The download could not be handed to the app.");
            return $"Added {url}. It appears in Nexonic Download Manager.";
        }
        if (api == null) throw new InvalidOperationException("Turn on Automation in Options → Browsers and set NDM_API_KEY to its key to use this tool.");
        if (name == "list_downloads") return await Send(HttpMethod.Get, "downloads", ct);
        var id = Arg("id");
        if (!Guid.TryParse(id, out _)) throw new InvalidOperationException("Give a download id from list_downloads.");
        return name switch
        {
            "pause_download" => await Send(HttpMethod.Post, $"downloads/{id}/pause", ct),
            "resume_download" => await Send(HttpMethod.Post, $"downloads/{id}/resume", ct),
            "remove_download" => await Send(HttpMethod.Delete, $"downloads/{id}", ct),
            _ => throw new InvalidOperationException("Unknown tool"),
        };
    }

    async Task<string> Send(HttpMethod method, string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post) req.Content = new StringContent("", Encoding.UTF8, "application/json");
        using var res = await api!.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The app answered {(int)res.StatusCode}: {body}");
        return body;
    }

    /// <summary>Serves newline-delimited JSON-RPC until input ends.</summary>
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct = default)
    {
        while (await input.ReadLineAsync(ct) is { } line)
        {
            if (line.Trim().Length == 0) continue;
            JsonObject? reply;
            try { reply = JsonNode.Parse(line) is JsonObject m ? await HandleAsync(m, ct) : null; }
            catch (JsonException) { reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = null, ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "Parse error" } }; }
            if (reply == null) continue;
            await output.WriteLineAsync(reply.ToJsonString());
            await output.FlushAsync(ct);
        }
    }
}

using System.Text.Json.Nodes;

namespace Nexonic.Core;

public enum DebridService { None, RealDebrid, AllDebrid, Premiumize }

/// <summary>
/// An optional debrid account (your own API key; off by default). Links from the sites listed are turned into a direct
/// link by the service before downloading. Nothing is sent to the service for other sites.
/// </summary>
public sealed class DebridSettings
{
    public DebridService Service { get; set; } = DebridService.None;
    /// <summary>The account's API key (saved encrypted, never in backups).</summary>
    public string Key { get; set; } = "";
    /// <summary>Sites (comma or space separated) whose links go through the service; subdomains included.</summary>
    public string Hosts { get; set; } = "";

    public static string Title(DebridService s) => s switch
    {
        DebridService.RealDebrid => "Real-Debrid", DebridService.AllDebrid => "AllDebrid", DebridService.Premiumize => "Premiumize",
        _ => "Off",
    };
}

public static class Debrid
{
    /// <summary>Settings from Options (the app sets them when it starts and when they change).</summary>
    public static DebridSettings Settings { get; set; } = new();

    /// <summary>True when this link goes through the debrid service.</summary>
    public static bool Applies(string url, DebridSettings? s = null)
    {
        s ??= Settings;
        if (s.Service == DebridService.None || s.Key.Trim().Length == 0) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return false;
        var host = u.Host.ToLowerInvariant();
        return s.Hosts.Split(new[] { ',', ' ', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.Trim().ToLowerInvariant().TrimStart('.'))
            .Any(h => h.Length > 0 && (host == h || host.EndsWith("." + h)));
    }

    /// <summary>The service's request for one link.</summary>
    public static HttpRequestMessage Request(DebridSettings s, string link)
    {
        var key = s.Key.Trim();
        switch (s.Service)
        {
            case DebridService.RealDebrid:
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.real-debrid.com/rest/1.0/unrestrict/link")
                    { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["link"] = link }) };
                req.Headers.Authorization = new("Bearer", key);
                return req;
            }
            case DebridService.AllDebrid:
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.alldebrid.com/v4/link/unlock?agent=nexonic")
                    { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["link"] = link }) };
                req.Headers.Authorization = new("Bearer", key);
                return req;
            }
            case DebridService.Premiumize:
                return new HttpRequestMessage(HttpMethod.Post, "https://www.premiumize.me/api/transfer/directdl")
                    { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["apikey"] = key, ["src"] = link }) };
            default:
                throw new InvalidOperationException("No debrid service is chosen.");
        }
    }

    /// <summary>The direct link in the service's answer, or an exception with its message.</summary>
    public static string ParseAnswer(DebridService service, string json)
    {
        var node = JsonNode.Parse(json);
        string? link = service switch
        {
            DebridService.RealDebrid => node?["download"]?.GetValue<string>(),
            DebridService.AllDebrid => node?["data"]?["link"]?.GetValue<string>(),
            DebridService.Premiumize => node?["content"]?[0]?["link"]?.GetValue<string>() ?? node?["location"]?.GetValue<string>(),
            _ => null,
        };
        if (link != null && Uri.TryCreate(link, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") return link;
        var message = node?["error"]?.ToString() is { Length: > 0 } e ? e : node?["message"]?.GetValue<string>();
        if (node?["error"] is JsonObject o) message = o["message"]?.GetValue<string>() ?? message;
        throw new InvalidOperationException($"{DebridSettings.Title(service)} could not unlock the link{(message is { Length: > 0 } ? ": " + message : ".")}");
    }

    /// <summary>The direct link for `url` when the service applies to it, else `url` unchanged.</summary>
    public static async Task<string> ResolveAsync(string url, ProxySettings proxy, CancellationToken ct = default)
    {
        var s = Settings;
        if (!Applies(url, s)) return url;
        using var http = new HttpClient(Network.Handler(proxy)) { Timeout = TimeSpan.FromSeconds(60) };
        using var req = Request(s, url);
        using var res = await http.SendAsync(req, ct);
        return ParseAnswer(s.Service, await res.Content.ReadAsStringAsync(ct));
    }
}

/// <summary>
/// Torrents through the debrid account (Add Torrent → "Download with debrid"): the service fetches the torrent on its
/// servers and the files are then downloaded over HTTP with all connections, so the computer itself joins no swarm.
/// Real-Debrid: the magnet is added, all files selected, and the app waits (up to 10 minutes) until it is ready.
/// Premiumize: torrents its servers already have (cached) are available at once.
/// AllDebrid: the magnet is uploaded and the app waits (up to 10 minutes) until it is ready, then unlocks its files.
/// </summary>
public static class DebridTorrents
{
    public sealed record File(string Name, string Url, long? Size);

    public static string? Unsupported(DebridSettings s) => s.Service switch
    {
        DebridService.None => "Choose a debrid service in Options → Debrid first.",
        _ when s.Key.Trim().Length == 0 => "Enter your debrid service's API key in Options → Debrid first.",
        _ => null,
    };

    public static async Task<List<File>> ResolveAsync(DebridSettings s, string magnet, HttpClient http, Action<string>? status,
        CancellationToken ct, string? apiBase = null, TimeSpan? wait = null)
    {
        if (Unsupported(s) is { } why) throw new InvalidOperationException(why);
        if (!magnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Debrid downloads need a magnet link.");
        var key = s.Key.Trim();
        return s.Service switch
        {
            DebridService.RealDebrid => await RealDebridAsync(key, magnet, http, status, ct, apiBase ?? "https://api.real-debrid.com/rest/1.0", wait ?? TimeSpan.FromMinutes(10)),
            DebridService.AllDebrid => await AllDebridAsync(key, magnet, http, status, ct, apiBase ?? "https://api.alldebrid.com/v4", wait ?? TimeSpan.FromMinutes(10)),
            _ => await PremiumizeAsync(key, magnet, http, ct, apiBase ?? "https://www.premiumize.me/api"),
        };
    }

    static async Task<JsonNode?> Call(HttpClient http, HttpMethod method, string url, string? bearer, Dictionary<string, string>? form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        if (form != null) req.Content = new FormUrlEncodedContent(form);
        if (bearer != null) req.Headers.Authorization = new("Bearer", bearer);
        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        JsonNode? node = null;
        try { if (text.Length > 0) node = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { }
        if (!res.IsSuccessStatusCode)
        {
            var message = node?["error"]?.ToString() ?? node?["message"]?.ToString() ?? $"HTTP {(int)res.StatusCode}";
            throw new InvalidOperationException("The debrid service refused: " + message);
        }
        return node;
    }

    static bool Web(string? u) => u != null && Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Scheme is "https" or "http";

    static async Task<List<File>> RealDebridAsync(string key, string magnet, HttpClient http, Action<string>? status, CancellationToken ct, string api, TimeSpan wait)
    {
        var added = await Call(http, HttpMethod.Post, $"{api}/torrents/addMagnet", key, new() { ["magnet"] = magnet }, ct);
        var id = added?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Real-Debrid did not accept the magnet link.");
        var until = DateTime.UtcNow + wait;
        var selected = false;
        while (true)
        {
            var info = await Call(http, HttpMethod.Get, $"{api}/torrents/info/{Uri.EscapeDataString(id)}", key, null, ct);
            var state = info?["status"]?.GetValue<string>() ?? "";
            if (state == "waiting_files_selection" && !selected)
            {
                await Call(http, HttpMethod.Post, $"{api}/torrents/selectFiles/{Uri.EscapeDataString(id)}", key, new() { ["files"] = "all" }, ct);
                selected = true;
                continue;
            }
            if (state == "downloaded")
            {
                var files = new List<File>();
                foreach (var link in (info?["links"] as JsonArray ?? new()).Select(l => l?.GetValue<string>()).Where(Web))
                {
                    var u = await Call(http, HttpMethod.Post, $"{api}/unrestrict/link", key, new() { ["link"] = link! }, ct);
                    var direct = u?["download"]?.GetValue<string>();
                    if (!Web(direct)) continue;
                    files.Add(new File(FileNames.Sanitize(u?["filename"]?.GetValue<string>() ?? FileNames.FromUrl(direct!)), direct!,
                        u?["filesize"]?.GetValue<long>()));
                }
                return files.Count > 0 ? files : throw new InvalidOperationException("Real-Debrid has the torrent but gave no files.");
            }
            if (state is "error" or "magnet_error" or "virus" or "dead")
                throw new InvalidOperationException($"Real-Debrid could not get the torrent ({state}).");
            if (DateTime.UtcNow > until)
                throw new InvalidOperationException("Real-Debrid is still fetching the torrent; it stays in your account. Try again later.");
            status?.Invoke($"Real-Debrid: {state.Replace('_', ' ')} {info?["progress"]?.ToString() ?? "0"}%");
            await Task.Delay(TimeSpan.FromSeconds(wait < TimeSpan.FromMinutes(1) ? 0.2 : 5), ct);
        }
    }

    /// <summary>AllDebrid answers {"status":"error","error":{"message":…}} with HTTP 200, so the status is checked too.</summary>
    static async Task<JsonNode?> AllDebridCall(HttpClient http, HttpMethod method, string url, string key, Dictionary<string, string>? form, CancellationToken ct)
    {
        var node = await Call(http, method, url + (url.Contains('?') ? "&" : "?") + "agent=nexonic", key, form, ct);
        if (node?["status"]?.GetValue<string>() != "success")
            throw new InvalidOperationException("AllDebrid refused: " + (node?["error"]?["message"]?.GetValue<string>() ?? node?["error"]?["code"]?.GetValue<string>() ?? "no answer"));
        return node?["data"];
    }

    /// <summary>The download links of a ready magnet: AllDebrid lists them flat ("links") or as a folder tree ("files").</summary>
    static IEnumerable<string> AllDebridLinks(JsonNode? magnet)
    {
        foreach (var l in magnet?["links"] as JsonArray ?? new()) if (l?["link"]?.GetValue<string>() is { } u && Web(u)) yield return u;
        IEnumerable<string> Walk(JsonArray? entries)
        {
            foreach (var e in entries ?? new())
            {
                if (e?["l"]?.GetValue<string>() is { } u && Web(u)) yield return u;
                foreach (var inner in Walk(e?["e"] as JsonArray)) yield return inner;
            }
        }
        foreach (var u in Walk(magnet?["files"] as JsonArray)) yield return u;
    }

    static async Task<List<File>> AllDebridAsync(string key, string magnet, HttpClient http, Action<string>? status, CancellationToken ct, string api, TimeSpan wait)
    {
        var added = await AllDebridCall(http, HttpMethod.Post, $"{api}/magnet/upload", key, new() { ["magnets[]"] = magnet }, ct);
        var first = added?["magnets"]?[0];
        if (first?["error"]?["message"]?.GetValue<string>() is { } refused) throw new InvalidOperationException("AllDebrid refused: " + refused);
        var id = first?["id"]?.ToString() ?? throw new InvalidOperationException("AllDebrid did not accept the magnet link.");
        var until = DateTime.UtcNow + wait;
        while (true)
        {
            var info = (await AllDebridCall(http, HttpMethod.Get, $"{api}/magnet/status?id={Uri.EscapeDataString(id)}", key, null, ct))?["magnets"];
            if (info is JsonArray list) info = list.FirstOrDefault();
            var code = info?["statusCode"]?.GetValue<int>() ?? -1;
            if (code == 4)
            {
                var links = AllDebridLinks(info).ToList();
                if (links.Count == 0)
                {
                    var files = await AllDebridCall(http, HttpMethod.Post, $"{api}/magnet/files", key, new() { ["id[]"] = id }, ct);
                    links = AllDebridLinks(files?["magnets"]?[0]).ToList();
                }
                var result = new List<File>();
                foreach (var link in links.Distinct())
                {
                    var u = await AllDebridCall(http, HttpMethod.Post, $"{api}/link/unlock", key, new() { ["link"] = link }, ct);
                    var direct = u?["link"]?.GetValue<string>();
                    if (!Web(direct)) continue;
                    result.Add(new File(FileNames.Sanitize(u?["filename"]?.GetValue<string>() ?? FileNames.FromUrl(direct!)), direct!, u?["filesize"]?.GetValue<long>()));
                }
                return result.Count > 0 ? result : throw new InvalidOperationException("AllDebrid has the torrent but gave no files.");
            }
            if (code >= 5) throw new InvalidOperationException($"AllDebrid could not get the torrent ({info?["status"]?.GetValue<string>() ?? "error " + code}).");
            if (DateTime.UtcNow > until)
                throw new InvalidOperationException("AllDebrid is still fetching the torrent; it stays in your account. Try again later.");
            status?.Invoke($"AllDebrid: {info?["status"]?.GetValue<string>() ?? "waiting"}");
            await Task.Delay(TimeSpan.FromSeconds(wait < TimeSpan.FromMinutes(1) ? 0.2 : 5), ct);
        }
    }

    static async Task<List<File>> PremiumizeAsync(string key, string magnet, HttpClient http, CancellationToken ct, string api)
    {
        var node = await Call(http, HttpMethod.Post, $"{api}/transfer/directdl", null, new() { ["apikey"] = key, ["src"] = magnet }, ct);
        if (node?["status"]?.GetValue<string>() != "success")
            throw new InvalidOperationException("Premiumize does not have this torrent ready" + (node?["message"]?.GetValue<string>() is { Length: > 0 } m ? ": " + m : "."));
        var files = (node?["content"] as JsonArray ?? new())
            .Select(c => (Path: c?["path"]?.GetValue<string>() ?? "", Link: c?["link"]?.GetValue<string>(), Size: c?["size"]?.GetValue<long>()))
            .Where(c => Web(c.Link))
            .Select(c => new File(FileNames.Sanitize(Path.GetFileName(c.Path.Replace('\\', '/'))), c.Link!, c.Size)).ToList();
        return files.Count > 0 ? files : throw new InvalidOperationException("Premiumize gave no files for this torrent.");
    }
}

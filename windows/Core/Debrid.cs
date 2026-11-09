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

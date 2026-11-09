using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Nexonic.Core;

/// <summary>
/// Help → Network Diagnostics: checks, one step at a time, why a link may not download (name lookup, connection,
/// secure connection, the server's answer and whether it allows resuming). Uses the same settings as downloads
/// (DNS over HTTPS, IP version, network adapter, proxy) and never sends cookies or passwords.
/// </summary>
public static class NetworkDiagnostics
{
    public sealed record Step(string Name, bool Ok, string Detail, long Milliseconds);

    public static async Task<List<Step>> RunAsync(string link, ProxySettings proxy, CancellationToken ct = default)
    {
        var steps = new List<Step>();
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            steps.Add(new Step("Link", false, "Enter a web address that starts with http:// or https://", 0));
            return steps;
        }
        var o = Network.Options;
        var doh = NetworkOptions.DnsOverHttpsUrlOk(o.DnsOverHttps) ? o.DnsOverHttps.Trim() : null;
        var direct = proxy.Mode == ProxyMode.None;
        steps.Add(new Step("Settings", true, Describe(o, proxy, doh), 0));

        // 1. Name lookup (with a proxy the proxy looks the name up, but a failure here still says a lot).
        IPAddress[] addresses = Array.Empty<IPAddress>();
        var watch = Stopwatch.StartNew();
        try
        {
            addresses = IPAddress.TryParse(uri.Host, out var literal) ? new[] { literal }
                : await Network.Resolve(uri.Host, doh, o.IpVersion, ct);
            steps.Add(new Step("Name lookup", addresses.Length > 0,
                addresses.Length > 0 ? $"{uri.Host} → {string.Join(", ", addresses.Take(4))}{(doh != null ? $" (via {new Uri(doh).Host})" : "")}"
                    : $"{uri.Host} has no address", watch.ElapsedMilliseconds));
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            steps.Add(new Step("Name lookup", false, $"{uri.Host}: {Message(e)}", watch.ElapsedMilliseconds));
            if (direct) return steps;
        }

        // 2. Connection to the server (direct connections only; through a proxy the HTTP step shows the result).
        if (direct && addresses.Length > 0)
        {
            watch.Restart();
            var bind = o.BindAdapter.Trim();
            try
            {
                await using var stream = await Adapters.ConnectAsync(bind.Length > 0 ? Adapters.AddressesOf(bind) : null,
                    new DnsEndPoint(uri.Host, uri.Port), (_, _) => Task.FromResult(addresses), TimeSpan.FromSeconds(Math.Clamp(o.ConnectTimeoutSeconds, 3, 30)), ct);
                var remote = (stream as NetworkStream)?.Socket.RemoteEndPoint?.ToString() ?? uri.Host;
                steps.Add(new Step("Connection", true, $"Connected to {remote}{(bind.Length > 0 ? $" through {bind}" : "")}", watch.ElapsedMilliseconds));
                if (uri.Scheme == "https")
                {
                    watch.Restart();
                    try
                    {
                        using var tls = new SslStream(stream, leaveInnerStreamOpen: true);
                        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = uri.Host }, ct);
                        var cert = tls.RemoteCertificate as System.Security.Cryptography.X509Certificates.X509Certificate2;
                        steps.Add(new Step("Secure connection", true,
                            $"{Protocol(tls.SslProtocol)}, certificate for {cert?.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.DnsName, false) ?? uri.Host}" +
                            (cert != null ? $", valid until {cert.NotAfter:yyyy-MM-dd}" : ""), watch.ElapsedMilliseconds));
                    }
                    catch (Exception e) when (e is AuthenticationException or IOException)
                    {
                        steps.Add(new Step("Secure connection", false, Message(e), watch.ElapsedMilliseconds));
                        return steps;
                    }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                steps.Add(new Step("Connection", false, Message(e), watch.ElapsedMilliseconds));
                return steps;
            }
        }

        // 3. The server's answer to a request for the first byte, like the first request of a download.
        watch.Restart();
        try
        {
            using var http = new HttpClient(Network.Handler(proxy)) { Timeout = TimeSpan.FromSeconds(30) };
            using var res = await Network.SendAsync(http, uri, u =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, u);
                req.Headers.Range = new RangeHeaderValue(0, 0);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; NexonicDownloadManager)");
                return req;
            }, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)res.StatusCode;
            var ok = code is >= 200 and < 300;
            var size = res.Content.Headers.ContentRange?.Length ?? (code == 200 ? res.Content.Headers.ContentLength : null);
            var detail = $"{code} {res.ReasonPhrase}" +
                (res.RequestMessage?.RequestUri is { } final && final != uri ? $", redirected to {final.Host}" : "") +
                (res.Content.Headers.ContentType?.MediaType is { } type ? $", {type}" : "") +
                (size is long s ? $", {FormatSize(s)}" : ", size not given") +
                (res.Headers.Server.Count > 0 ? $", server {res.Headers.Server}" : "");
            steps.Add(new Step("Server answer", ok, detail, watch.ElapsedMilliseconds));
            if (ok)
                steps.Add(code == 206
                    ? new Step("Resuming and parts", true, "The server sends parts of the file: downloads can pause, resume and use several connections.", 0)
                    : new Step("Resuming and parts", false, "The server sends only the whole file: downloads cannot resume and use one connection.", 0));
            else if (code is 401 or 403)
                steps.Add(new Step("Hint", false, "The site needs a login or cookies; start the download from the browser so the extension can pass them on.", 0));
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            steps.Add(new Step("Server answer", false, Message(e), watch.ElapsedMilliseconds));
        }
        return steps;
    }

    /// <summary>The results as plain text (for Copy).</summary>
    public static string Report(IEnumerable<Step> steps) =>
        string.Join(Environment.NewLine, steps.Select(s => $"{(s.Ok ? "OK  " : "FAIL")}  {s.Name}: {s.Detail}{(s.Milliseconds > 0 ? $" ({s.Milliseconds} ms)" : "")}"));

    static string Describe(NetworkOptions o, ProxySettings p, string? doh) => string.Join(", ", new[]
    {
        p.Mode switch { ProxyMode.None => "no proxy", ProxyMode.System => "system proxy", ProxyMode.Socks => $"SOCKS5 proxy {p.Host}:{p.Port}", _ => $"HTTP proxy {p.Host}:{p.Port}" },
        doh != null ? $"DNS over HTTPS ({new Uri(doh).Host})" : "system DNS",
        o.IpVersion switch { "ipv4" => "IPv4 only", "ipv6" => "IPv6 only", _ => "IPv4 and IPv6" },
        o.BindAdapter.Length > 0 ? $"adapter {o.BindAdapter}" : "any adapter",
    });

    static string Protocol(SslProtocols p) => p switch { SslProtocols.Tls13 => "TLS 1.3", SslProtocols.Tls12 => "TLS 1.2", _ => p.ToString() };

    static string Message(Exception e)
    {
        var inner = e;
        while (inner.InnerException != null && inner is HttpRequestException or AggregateException) inner = inner.InnerException;
        return inner is OperationCanceledException or TimeoutException ? "No answer in time" : inner.Message;
    }

    static string FormatSize(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.##} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0.#} MB" : b >= 1024 ? $"{b / 1024.0:0} KB" : $"{b} bytes";
}

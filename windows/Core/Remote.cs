using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nexonic.Core;

/// <summary>Options → Remote: manage downloads from a phone or another device on this network. Off by default.</summary>
public sealed class RemoteSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 47813;
    string key = "";
    /// <summary>The pairing key (saved encrypted like other secrets); a new key unpairs every device.</summary>
    public string Key { get => key; set => key = value ?? ""; }
}

/// <summary>
/// Remote management: a small web page for phones and the same /v1 API as the local automation API, on the local
/// network. Every API call needs the pairing key (Authorization: Bearer), which the phone gets from the QR code shown in
/// the app (the key travels in the address's #fragment, so it is never sent in a request line or logged). The page
/// itself holds no data. Traffic is plain HTTP inside the network: the Options page says to use it only on trusted
/// networks (home or office), never on public Wi-Fi.
/// </summary>
public sealed class RemoteAccess : IDisposable
{
    public const int MaxConnections = 16;
    readonly SemaphoreSlim slots = new(MaxConnections);
    readonly TcpListener listener;
    readonly string key;
    readonly IDownloadControl control;
    readonly Func<Func<(int, object)>, Task<(int, object)>> onUi;
    readonly CancellationTokenSource stop = new();
    // Wrong keys slow down: after 10 failures from one address, that address waits a minute.
    readonly Dictionary<IPAddress, (int Count, DateTime Until)> failures = new();

    public int Port { get; }

    public RemoteAccess(int port, string key, IDownloadControl control, Func<Func<(int, object)>, Task<(int, object)>> onUi, IPAddress? bind = null)
    {
        if (key.Length < 16) throw new ArgumentException("The pairing key is too short.");
        Port = port;
        this.key = key;
        this.control = control;
        this.onUi = onUi;
        listener = new TcpListener(bind ?? IPAddress.Any, port);
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
            if (!slots.Wait(0)) { client.Dispose(); continue; }
            _ = Task.Run(async () => { try { await Serve(client); } finally { slots.Release(); } });
        }
    }

    async Task Serve(TcpClient client)
    {
        using var connection = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var stream = client.GetStream();
        var from = (client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
        if (from.IsIPv4MappedToIPv6) from = from.MapToIPv4();
        byte[] payload;
        int status;
        string type = "application/json; charset=utf-8";
        try
        {
            var req = await LocalApi.ReadRequest(stream, timeout.Token);
            if (req == null) (status, payload) = (400, Json(new { error = "Bad request" }));
            else if (req.Method == "GET" && req.Path.Split('?')[0] is "/" or "/index.html")
            {
                (status, payload, type) = (200, Page.Value, "text/html; charset=utf-8");
            }
            else if (!req.Path.StartsWith("/v1/", StringComparison.Ordinal)) (status, payload) = (404, Json(new { error = "Not found" }));
            else if (Blocked(from)) (status, payload) = (429, Json(new { error = "Too many wrong keys; try again in a minute" }));
            else if (Check(req.Headers, key) is { } refused)
            {
                if (refused.Status == 401) Failed(from);
                (status, payload) = (refused.Status, Json(new { error = refused.Message }));
            }
            else
            {
                var (s, body) = await onUi(() => LocalApi.Route(req.Method, req.Path, req.Body, control));
                (status, payload) = (s, Json(body));
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { return; }
        catch (Exception e) { status = 500; payload = Json(new { error = "Internal error" }); Console.Error.WriteLine("Remote: " + e); }
        try
        {
            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status < 300 ? "OK" : "Error")}\r\nContent-Type: {type}\r\n" +
                $"Content-Length: {payload.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\n" +
                "X-Frame-Options: DENY\r\nContent-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(head, timeout.Token);
            await stream.WriteAsync(payload, timeout.Token);
        }
        catch { }
    }

    /// <summary>Null when the API call may go on. Pages of other sites are refused (their Origin differs from the Host).</summary>
    public static (int Status, string Message)? Check(IReadOnlyDictionary<string, string> headers, string key)
    {
        var host = headers.TryGetValue("Host", out var h) ? h : "";
        if (headers.TryGetValue("Origin", out var origin) && !string.Equals(origin, "http://" + host, StringComparison.OrdinalIgnoreCase))
            return (403, "Requests from other web pages are not allowed");
        var given = headers.TryGetValue("Authorization", out var a) && a.StartsWith("Bearer ", StringComparison.Ordinal) ? a[7..].Trim() : "";
        if (key.Length < 16 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(key)))
            return (401, "This device is not paired (scan the QR code in the app again)");
        return null;
    }

    bool Blocked(IPAddress from)
    {
        lock (failures) return failures.TryGetValue(from, out var f) && f.Count >= 10 && f.Until > DateTime.UtcNow;
    }

    void Failed(IPAddress from)
    {
        lock (failures)
        {
            var f = failures.TryGetValue(from, out var old) && old.Until > DateTime.UtcNow ? old : (0, DateTime.UtcNow);
            failures[from] = (f.Item1 + 1, DateTime.UtcNow.AddMinutes(1));
            if (failures.Count > 1000) failures.Clear();
        }
    }

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static byte[] Json(object body) => JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);

    static readonly Lazy<byte[]> Page = new(() =>
    {
        using var s = typeof(RemoteAccess).Assembly.GetManifestResourceStream("remote.html");
        if (s == null) return Encoding.UTF8.GetBytes("<!doctype html><title>Nexonic</title><p>The remote page is missing.");
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    });

    /// <summary>This computer's addresses on the local network (private IPv4 first), for the pairing address.</summary>
    public static List<IPAddress> LanAddresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var a in nic.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)) list.Add(a.Address);
            }
        }
        catch (NetworkInformationException) { }
        return list.Distinct().OrderBy(a => IsPrivate(a) ? 0 : 1).ToList();
    }

    public static bool IsPrivate(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127));
    }

    /// <summary>The address a phone opens (scanned from the QR code): the key is in the #fragment, which browsers never send.</summary>
    public static string PairingUrl(string host, int port, string key) => $"http://{host}:{port}/#k={Uri.EscapeDataString(key)}";

    /// <summary>A QR code drawn with text blocks, for terminals (two rows of modules per line).</summary>
    public static string TerminalQr(string text)
    {
        var qr = QrCode.Encode(text, QrCode.Level.L);
        var sb = new StringBuilder();
        for (var y = -2; y < qr.Size + 2; y += 2)
        {
            for (var x = -2; x < qr.Size + 2; x++)
            {
                bool top = qr[x, y], bottom = qr[x, y + 1];
                // Light text on a dark terminal: draw the light modules, leave the dark ones as spaces.
                sb.Append((top, bottom) switch { (false, false) => '█', (false, true) => '▀', (true, false) => '▄', _ => ' ' });
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        stop.Cancel();
        try { listener.Stop(); } catch { }
    }
}

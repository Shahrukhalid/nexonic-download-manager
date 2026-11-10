using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Nexonic.Core;
using Xunit;

/// <summary>DNS over TLS, the DNS wire format and strict encrypted DNS (no lookups with the system's DNS).</summary>
public class EncryptedDnsTests
{
    /// <summary>A reply to <paramref name="query"/> with the given addresses, using a compression pointer for the name.</summary>
    static byte[] Reply(byte[] query, params IPAddress[] answers)
    {
        var qlen = query.Length - 12;
        var ms = new MemoryStream();
        ms.Write(query, 0, 12);
        ms.Write(query, 12, qlen);
        foreach (var a in answers)
        {
            var b = a.GetAddressBytes();
            ms.Write(new byte[] { 0xC0, 12 });  // pointer to the question's name
            var rr = new byte[10];
            BinaryPrimitives.WriteUInt16BigEndian(rr, (ushort)(b.Length == 4 ? 1 : 28));
            BinaryPrimitives.WriteUInt16BigEndian(rr.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt32BigEndian(rr.AsSpan(4), 120);
            BinaryPrimitives.WriteUInt16BigEndian(rr.AsSpan(8), (ushort)b.Length);
            ms.Write(rr);
            ms.Write(b);
        }
        var m = ms.ToArray();
        m[2] |= 0x80;  // response
        BinaryPrimitives.WriteUInt16BigEndian(m.AsSpan(6), (ushort)answers.Length);
        return m;
    }

    [Fact]
    public void WireFormatRoundTripsAndRejectsBadReplies()
    {
        var q = DnsWire.Query(0x1234, "files.example.com", DnsWire.A);
        var r = DnsWire.Parse(Reply(q, IPAddress.Parse("192.0.2.7"), IPAddress.Parse("2001:db8::1")), 0x1234);
        Assert.NotNull(r);
        Assert.Equal(2, r!.Value.Addresses.Count);
        Assert.Equal(IPAddress.Parse("192.0.2.7"), r.Value.Addresses[0]);
        Assert.Equal(120, r.Value.Ttl);
        Assert.Null(DnsWire.Parse(Reply(q, IPAddress.Loopback), 0x9999));             // another question's answer
        Assert.Null(DnsWire.Parse(Reply(q, IPAddress.Loopback)[..20], 0x1234));      // cut short
        Assert.Null(DnsWire.Parse(q, 0x1234));                                        // a question, not an answer
        Assert.Throws<ArgumentException>(() => DnsWire.Query(1, new string('a', 64) + ".com", DnsWire.A));
    }

    [Fact]
    public void ReadsServerSettings()
    {
        Assert.Equal(new DnsOverTls.Server(IPAddress.Parse("1.1.1.1"), 853, "cloudflare-dns.com"), DnsOverTls.ParseServer("1.1.1.1#cloudflare-dns.com"));
        Assert.Equal(8853, DnsOverTls.ParseServer("9.9.9.9:8853#dns.quad9.net")!.Port);
        Assert.Equal(IPAddress.Parse("2606:4700::1111"), DnsOverTls.ParseServer("[2606:4700::1111]:853#cloudflare-dns.com")!.Address);
        Assert.Null(DnsOverTls.ParseServer("dns.google#dns.google"));  // a name would need a lookup to find the resolver
        Assert.Null(DnsOverTls.ParseServer("1.1.1.1"));
        Assert.Null(DnsOverTls.ParseServer("1.1.1.1#"));
    }

    [Fact]
    public async Task ResolvesOverTlsWithARealTlsServer()
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest("CN=dns.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("dns.test");
        req.CertificateExtensions.Add(san.Build());
        using var made = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var cert = X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx), null);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync();
            await using var tls = new SslStream(c.GetStream());
            await tls.AuthenticateAsServerAsync(cert);
            for (var i = 0; i < 2; i++)
            {
                var head = new byte[2];
                await tls.ReadExactlyAsync(head);
                var q = new byte[BinaryPrimitives.ReadUInt16BigEndian(head)];
                await tls.ReadExactlyAsync(q);
                var type = BinaryPrimitives.ReadUInt16BigEndian(q.AsSpan(q.Length - 4));
                var reply = type == DnsWire.A ? Reply(q, IPAddress.Parse("192.0.2.10")) : Reply(q, IPAddress.Parse("2001:db8::10"));
                var framed = new byte[reply.Length + 2];
                BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)reply.Length);
                reply.CopyTo(framed, 2);
                await tls.WriteAsync(framed);
            }
        });
        var trusted = cert.GetCertHashString();
        var ips = await DnsOverTls.ResolveAsync(new DnsOverTls.Server(IPAddress.Loopback, port, "dns.test"), "files.example.com", CancellationToken.None,
            (_, c, _, errors) => c?.GetCertHashString() == trusted);  // only this exact certificate is accepted
        await server;
        listener.Stop();
        Assert.Equal("192.0.2.10, 2001:db8::10", string.Join(", ", ips.Select(i => i.ToString())));
    }

    [Fact]
    public async Task AnUntrustedCertificateIsRefused()
    {
        using var key = RSA.Create(2048);
        using var made = new CertificateRequest("CN=evil.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var cert = X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx), null);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync();
            await using var tls = new SslStream(c.GetStream());
            try { await tls.AuthenticateAsServerAsync(cert); } catch { }
        });
        var failed = false;
        try { await DnsOverTls.ResolveAsync(new DnsOverTls.Server(IPAddress.Loopback, port, "dns.test"), "a.example.com", CancellationToken.None); }
        catch (System.Security.Authentication.AuthenticationException) { failed = true; }
        listener.Stop();
        Assert.True(failed);  // the system's normal certificate check applies
    }

    [Fact]
    public async Task StrictModeNeverFallsBackToTheSystemsDns()
    {
        Assert.Null(NetworkOptions.DnsProblem("", "", false));
        Assert.Contains("needs a DNS", NetworkOptions.DnsProblem("", "", true));
        Assert.Contains("by its address", NetworkOptions.DnsProblem("https://cloudflare-dns.com/dns-query", "", true));
        Assert.Null(NetworkOptions.DnsProblem("https://1.1.1.1/dns-query", "", true));
        Assert.Null(NetworkOptions.DnsProblem("https://cloudflare-dns.com/dns-query", "1.1.1.1#cloudflare-dns.com", true));
        Assert.Contains("address#certificate", NetworkOptions.DnsProblem("", "dns.google", false));
        // The app's shared settings are never changed here: other tests download at the same time.
        // A resolver that is not there: the lookup fails instead of quietly using the system's DNS.
        var strict = new NetworkOptions { DnsOverTls = "127.0.0.1:1#dns.test", StrictDns = true }.Clamped();
        Assert.True(strict.StrictDns && strict.EncryptedDns);
        var failed = false;
        try { await Network.Resolve("localhost", "", "any", strict, CancellationToken.None); }
        catch (Exception e) when (e is SocketException or HttpRequestException or IOException or OperationCanceledException) { failed = true; }
        Assert.True(failed);
        Assert.Contains("Strict encrypted DNS", Network.StrictDnsBlocks("the video engine", new ProxySettings(), strict));
        Assert.Null(Network.StrictDnsBlocks("the video engine", new ProxySettings { Mode = ProxyMode.Socks, Host = "127.0.0.1", Port = 9050 }, strict));
        Assert.False(new NetworkOptions { StrictDns = true }.Clamped().StrictDns);  // strict without a resolver is not kept
    }
}

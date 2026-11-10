using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Globalization;
using System.Text;

namespace Nexonic.Core;

/// <summary>DNS messages in their wire format (RFC 1035): one question, and the A/AAAA answers of a reply.</summary>
public static class DnsWire
{
    public const ushort A = 1, AAAA = 28;

    public static byte[] Query(ushort id, string host, ushort type)
    {
        var name = Name(host);
        var q = new byte[12 + name.Length + 4];
        BinaryPrimitives.WriteUInt16BigEndian(q, id);
        q[2] = 0x01;                                  // recursion desired
        BinaryPrimitives.WriteUInt16BigEndian(q.AsSpan(4), 1);  // one question
        name.CopyTo(q, 12);
        BinaryPrimitives.WriteUInt16BigEndian(q.AsSpan(12 + name.Length), type);
        BinaryPrimitives.WriteUInt16BigEndian(q.AsSpan(14 + name.Length), 1);  // class IN
        return q;
    }

    static byte[] Name(string host)
    {
        var ascii = new IdnMapping().GetAscii(host.TrimEnd('.'));
        var ms = new MemoryStream();
        foreach (var label in ascii.Split('.'))
        {
            if (label.Length is 0 or > 63) throw new ArgumentException("Not a valid host name.");
            ms.WriteByte((byte)label.Length);
            ms.Write(Encoding.ASCII.GetBytes(label));
        }
        ms.WriteByte(0);
        if (ms.Length > 255) throw new ArgumentException("Not a valid host name.");
        return ms.ToArray();
    }

    /// <summary>The addresses and the shortest TTL (at least 30 s) in a reply to <paramref name="id"/>; null when the reply
    /// is not one (wrong id, not a response, truncated) or reports an error.</summary>
    public static (List<IPAddress> Addresses, int Ttl)? Parse(ReadOnlySpan<byte> m, ushort id)
    {
        if (m.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(m) != id || (m[2] & 0x80) == 0 || (m[2] & 0x02) != 0) return null;
        var rcode = m[3] & 0x0F;
        if (rcode is not (0 or 3)) return null;      // 3 = no such name: a valid answer with nothing in it
        int qd = BinaryPrimitives.ReadUInt16BigEndian(m[4..]), an = BinaryPrimitives.ReadUInt16BigEndian(m[6..]);
        var at = 12;
        for (var i = 0; i < qd; i++) { if (!Skip(m, ref at) || at + 4 > m.Length) return null; at += 4; }
        var list = new List<IPAddress>();
        var ttl = 3600;
        for (var i = 0; i < an; i++)
        {
            if (!Skip(m, ref at) || at + 10 > m.Length) return null;
            var type = BinaryPrimitives.ReadUInt16BigEndian(m[at..]);
            var t = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(m[(at + 4)..]));
            var len = BinaryPrimitives.ReadUInt16BigEndian(m[(at + 8)..]);
            at += 10;
            if (at + len > m.Length) return null;
            if ((type == A && len == 4) || (type == AAAA && len == 16))
            {
                list.Add(new IPAddress(m.Slice(at, len)));
                ttl = Math.Min(ttl, Math.Max(30, t));
            }
            at += len;
        }
        return (list, ttl);
    }

    /// <summary>Steps over a name (labels or a compression pointer).</summary>
    static bool Skip(ReadOnlySpan<byte> m, ref int at)
    {
        for (var guard = 0; guard < 128 && at < m.Length; guard++)
        {
            var b = m[at];
            if (b == 0) { at++; return true; }
            if ((b & 0xC0) == 0xC0) { at += 2; return at <= m.Length; }
            at += b + 1;
        }
        return false;
    }
}

/// <summary>
/// DNS over TLS (RFC 7858): questions go to a resolver on port 853 inside TLS, checked against the resolver's certificate
/// name. The resolver is given as an address plus the name on its certificate ("1.1.1.1#cloudflare-dns.com"), so finding
/// the resolver itself never needs an unencrypted lookup.
/// </summary>
public static class DnsOverTls
{
    static readonly ConcurrentDictionary<string, (IPAddress[] Addresses, DateTime Until)> cache = new();

    public sealed record Server(IPAddress Address, int Port, string TlsName);

    /// <summary>"1.1.1.1#cloudflare-dns.com", "[2606:4700::1111]:853#cloudflare-dns.com"; null when it cannot be used.</summary>
    public static Server? ParseServer(string? text)
    {
        var t = (text ?? "").Trim();
        var hash = t.IndexOf('#');
        if (hash <= 0 || hash == t.Length - 1) return null;
        var name = t[(hash + 1)..].Trim().ToLowerInvariant();
        var addr = t[..hash].Trim();
        var port = 853;
        if (addr.StartsWith('[') && addr.IndexOf(']') is var close and > 0)
        {
            if (addr.Length > close + 1 && (addr[close + 1] != ':' || !int.TryParse(addr[(close + 2)..], out port))) return null;
            addr = addr[1..close];
        }
        else if (addr.Count(c => c == ':') == 1)
        {
            if (!int.TryParse(addr[(addr.IndexOf(':') + 1)..], out port)) return null;
            addr = addr[..addr.IndexOf(':')];
        }
        if (!IPAddress.TryParse(addr, out var ip) || port is < 1 or > 65535) return null;
        if (Uri.CheckHostName(name) != UriHostNameType.Dns) return null;
        return new Server(ip, port, name);
    }

    public static async Task<IPAddress[]> ResolveAsync(Server server, string host, CancellationToken ct,
        RemoteCertificateValidationCallback? validate = null, Func<IPAddress, CancellationToken, Task<Stream>>? connect = null)
    {
        if (IPAddress.TryParse(host, out var literal)) return new[] { literal };
        var key = $"{server.Address}:{server.Port}#{server.TlsName}|{host}";
        if (validate == null && cache.TryGetValue(key, out var hit) && hit.Until > DateTime.UtcNow) return hit.Addresses;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Stream raw;
        if (connect != null) raw = await connect(server.Address, timeout.Token);
        else
        {
            var socket = new Socket(server.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(new IPEndPoint(server.Address, server.Port), timeout.Token); }
            catch { socket.Dispose(); throw; }
            raw = new NetworkStream(socket, ownsSocket: true);
        }
        await using var tls = new SslStream(raw, false, validate);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = server.TlsName,
            CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
        }, timeout.Token);
        var all = new List<IPAddress>();
        var ttl = 3600;
        foreach (var type in new[] { DnsWire.A, DnsWire.AAAA })
        {
            var id = (ushort)Random.Shared.Next(1, 65535);
            var q = DnsWire.Query(id, host, type);
            var framed = new byte[q.Length + 2];
            BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)q.Length);
            q.CopyTo(framed, 2);
            await tls.WriteAsync(framed, timeout.Token);
            var head = new byte[2];
            await tls.ReadExactlyAsync(head, timeout.Token);
            var reply = new byte[BinaryPrimitives.ReadUInt16BigEndian(head)];
            await tls.ReadExactlyAsync(reply, timeout.Token);
            if (DnsWire.Parse(reply, id) is not { } r) throw new HttpRequestException("The DNS-over-TLS server sent an answer that could not be read.");
            all.AddRange(r.Addresses);
            ttl = Math.Min(ttl, r.Ttl);
        }
        if (all.Count == 0) throw new HttpRequestException($"DNS over TLS could not find {host}");
        var result = all.ToArray();
        if (validate == null) cache[key] = (result, DateTime.UtcNow.AddSeconds(ttl));
        return result;
    }
}

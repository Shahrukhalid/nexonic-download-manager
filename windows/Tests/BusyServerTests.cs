using System.Net;
using System.Net.Sockets;
using System.Text;
using Nexonic.Core;
using Xunit;

/// <summary>Real downloads from a local server that refuses connections (429/503): nothing is lost or corrupted.</summary>
public class BusyServerTests
{
    /// <summary>A small HTTP/1.1 server with ranges; <c>refuse</c> decides per request whether to answer 429/503.</summary>
    sealed class Server : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly byte[] content;
        readonly Func<bool, int> refuse;
        public int Port { get; }
        public int Requests;
        public int Refused;

        public Server(byte[] content, Func<bool, int> refuse)
        {
            this.content = content;
            this.refuse = refuse;
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    TcpClient c;
                    try { c = await listener.AcceptTcpClientAsync(); } catch { return; }
                    _ = Task.Run(() => Serve(c));
                }
            });
        }

        async Task Serve(TcpClient c)
        {
            using var _ = c;
            var s = c.GetStream();
            var reader = new StreamReader(s, Encoding.ASCII);
            while (true)
            {
                var first = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(first)) return;
                string? range = null, line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = line[6..].Trim();
                var n = Interlocked.Increment(ref Requests);
                var code = refuse(range != null);
                byte[] body;
                var head = new StringBuilder();
                if (code != 0)
                {
                    Interlocked.Increment(ref Refused);
                    body = Array.Empty<byte>();
                    head.Append($"HTTP/1.1 {code} Busy\r\nRetry-After: 1\r\n");
                }
                else if (range == null)
                {
                    body = content;
                    head.Append("HTTP/1.1 200 OK\r\nAccept-Ranges: bytes\r\nETag: \"v1\"\r\n");
                }
                else
                {
                    var p = range["bytes=".Length..].Split('-');
                    var from = long.Parse(p[0]);
                    var to = p[1].Length > 0 ? long.Parse(p[1]) : content.Length - 1;
                    body = content[(int)from..((int)to + 1)];
                    head.Append($"HTTP/1.1 206 Partial\r\nAccept-Ranges: bytes\r\nETag: \"v1\"\r\nContent-Range: bytes {from}-{to}/{content.Length}\r\n");
                }
                head.Append($"Content-Length: {body.Length}\r\n\r\n");
                await s.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
                await s.WriteAsync(body);
            }
        }

        public void Dispose() => listener.Stop();
    }

    static byte[] Pattern(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++) data[i] = (byte)(i * 31 + i / 997);
        return data;
    }

    static async Task<DownloadRecord> Run(IDownloader t)
    {
        var done = new TaskCompletionSource();
        t.Changed += r => { if (r.Status is DownloadStatus.Finished or DownloadStatus.Failed) done.TrySetResult(); };
        await t.StartAsync();
        await Task.WhenAny(done.Task, Task.Delay(60000));
        return t.Record;
    }

    [Fact]
    public async Task RefusedPartsWaitAndTheFileStaysExact()
    {
        var content = Pattern(5 * 1024 * 1024 + 77);
        var ranged = 0;
        using var server = new Server(content, isRanged => isRanged && Interlocked.Increment(ref ranged) % 3 == 2 ? 429 : 0);
        var rec = new DownloadRecord { Url = $"http://127.0.0.1:{server.Port}/busy.bin", Folder = Directory.CreateTempSubdirectory().FullName, FileName = "busy.bin" };
        var r = await Run(new Transfer(rec, 16, new SpeedLimiter(), autoConnections: true));
        Assert.Equal(DownloadStatus.Finished, r.Status);
        Assert.True(content.AsSpan().SequenceEqual(File.ReadAllBytes(r.SavedPath!)));
        Assert.True(server.Refused >= 1);
    }

    [Fact]
    public async Task TheOnlyConnectionWaitsWithoutUsingUpRetries()
    {
        var content = Pattern(300 * 1024);
        var calls = 0;
        using var server = new Server(content, _ => Interlocked.Increment(ref calls) is 2 or 3 ? 503 : 0);
        var rec = new DownloadRecord { Url = $"http://127.0.0.1:{server.Port}/one.bin", Folder = Directory.CreateTempSubdirectory().FullName, FileName = "one.bin" };
        var r = await Run(new Transfer(rec, 1, new SpeedLimiter(), options: new TransferOptions(Retries: 1, ReadTimeoutSeconds: 20)));
        Assert.Equal(DownloadStatus.Finished, r.Status);
        Assert.True(content.AsSpan().SequenceEqual(File.ReadAllBytes(r.SavedPath!)));
        Assert.Equal(2, server.Refused);
    }

    [Fact]
    public async Task MirrorsShareTheWorkAndAWrongMirrorIsNeverUsed()
    {
        var content = Pattern(6 * 1024 * 1024 + 5);
        var other = Pattern(6 * 1024 * 1024 + 5);
        other[content.Length / 2] ^= 0xFF;  // same size, one byte different in the middle
        using var main = new Server(content, _ => 0);
        using var good = new Server(content, _ => 0);
        using var wrong = new Server(other, _ => 0);
        var rec = new DownloadRecord
        {
            Url = $"http://127.0.0.1:{main.Port}/f.bin", Folder = Directory.CreateTempSubdirectory().FullName, FileName = "f.bin",
            Mirrors = new() { $"http://127.0.0.1:{good.Port}/f.bin", $"http://127.0.0.1:{wrong.Port}/f.bin", "file:///etc/passwd" },
        };
        var t = new Transfer(rec, 8, new SpeedLimiter());
        var r = await Run(t);
        Assert.Equal(DownloadStatus.Finished, r.Status);
        Assert.True(content.AsSpan().SequenceEqual(File.ReadAllBytes(r.SavedPath!)));
        var sources = t.Sources!;
        Assert.Equal(2, sources.Count);  // the wrong mirror was left out after the sample check
        Assert.True(sources.All(x => x.Bytes > 0));  // both really sent parts
        Assert.True(good.Requests > 3);
        Assert.True(wrong.Requests <= 3);  // only the samples
    }

    [Fact]
    public void PoolPrefersTheFasterSourceAndDropsFailingMirrors()
    {
        var a = new Uri("http://a.test/f");
        var b = new Uri("http://b.test/f");
        var pool = new MirrorPool(a, new[] { b, a });
        Assert.Equal(2, pool.Count);
        var first = pool.Take();
        var second = pool.Take();
        Assert.NotEqual(first, second);  // each unmeasured source gets a part first
        pool.Done(a, 1_000_000, TimeSpan.FromSeconds(1), false);
        pool.Done(b, 10_000_000, TimeSpan.FromSeconds(1), false);
        Assert.Equal(b, pool.Take());  // measured faster
        pool.Done(b, 0, TimeSpan.Zero, true);
        pool.Done(b, 0, TimeSpan.Zero, true);
        Assert.Equal(1, pool.Count);
        Assert.Equal(a, pool.Take());
        pool.Done(a, 0, TimeSpan.Zero, true);
        pool.Done(a, 0, TimeSpan.Zero, true);
        pool.Drop(a);
        Assert.Equal(1, pool.Count);  // the main address is never dropped
    }

    [Fact]
    public void ReadsMetalinkFilesSafely()
    {
        const string v4 = """
            <?xml version="1.0" encoding="UTF-8"?>
            <metalink xmlns="urn:ietf:params:xml:ns:metalink">
              <file name="../evil/linux.iso">
                <size>4096</size>
                <hash type="md5">0123456789abcdef0123456789abcdef</hash>
                <hash type="sha-256">ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12</hash>
                <url priority="2">https://two.test/linux.iso</url>
                <url priority="1">https://one.test/linux.iso</url>
                <url priority="3">javascript:alert(1)</url>
              </file>
            </metalink>
            """;
        var e = Assert.Single(Metalink.Parse(v4));
        Assert.Equal("linux.iso", e.Name);
        Assert.Equal(4096, e.Size);
        Assert.Equal("sha256:ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12", e.Checksum);
        Assert.Equal(2, e.Urls.Count);
        Assert.Equal("https://one.test/linux.iso", e.Urls[0]);
        const string v3 = """<metalink version="3.0" xmlns="http://www.metalinker.org/"><files><file name="a.zip"><resources><url type="http" preference="10">http://low.test/a.zip</url><url type="http" preference="90">http://high.test/a.zip</url></resources></file></files></metalink>""";
        Assert.Equal("http://high.test/a.zip", Assert.Single(Metalink.Parse(v3)).Urls[0]);
        const string bomb = """<?xml version="1.0"?><!DOCTYPE m [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;">]><metalink><file name="x"><url>http://x.test/&b;</url></file></metalink>""";
        Assert.Empty(Metalink.Parse(bomb));  // DTDs are refused
        Assert.Empty(Metalink.Parse("not xml"));
        Assert.True(Metalink.Looks("a.meta4"));
    }
}

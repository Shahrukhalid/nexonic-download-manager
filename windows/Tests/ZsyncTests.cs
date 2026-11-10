using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Nexonic.Core;
using Xunit;

/// <summary>Differential AppImage updates: only changed blocks are downloaded, and a wrong result is never kept.</summary>
public class ZsyncTests
{
    /// <summary>A control file in zsyncmake's format (header, blank line, per block the rolling sum's last bytes and MD4).</summary>
    internal static byte[] Make(byte[] file, int bs = 2048, int seq = 2, int rb = 2, int cb = 4)
    {
        var head = $"zsync: 0.6.2\nFilename: app\nBlocksize: {bs}\nLength: {file.Length}\nHash-Lengths: {seq},{rb},{cb}\nURL: app\n" +
                   $"SHA-1: {Convert.ToHexString(SHA1.HashData(file)).ToLowerInvariant()}\n\n";
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(head));
        for (var off = 0; off < file.Length; off += bs)
        {
            var block = new byte[bs];
            Array.Copy(file, off, block, 0, Math.Min(bs, file.Length - off));
            var r = Zsync.Rsum(block);
            var be = new[] { (byte)(r >> 24), (byte)(r >> 16), (byte)(r >> 8), (byte)r };
            ms.Write(be, 4 - rb, rb);
            ms.Write(Md4.Hash(block), 0, cb);
        }
        return ms.ToArray();
    }

    sealed class RangeServer(byte[] content) : HttpMessageHandler
    {
        public long Sent;
        public int Requests;
        public bool IgnoreRanges;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var r = request.Headers.Range?.Ranges.Single();
            if (r == null || IgnoreRanges) { Sent += content.Length; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }); }
            var (from, to) = (r.From!.Value, r.To!.Value);
            var part = content[(int)from..(int)(to + 1)];
            Sent += part.Length;
            var res = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(part) };
            res.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, content.Length);
            return Task.FromResult(res);
        }
    }

    static byte[] Random(int n, int seed) { var b = new byte[n]; new Random(seed).NextBytes(b); return b; }

    [Fact]
    public void Md4MatchesTheStandardTestVectors()
    {
        Assert.Equal("31d6cfe0d16ae931b73c59d7e0c089c0", Convert.ToHexString(Md4.Hash(Array.Empty<byte>())).ToLowerInvariant());
        Assert.Equal("a448017aaf21d8525fc10ae87aa6729d", Convert.ToHexString(Md4.Hash("abc"u8)).ToLowerInvariant());
        Assert.Equal("e33b4ddc9c38f2199c3e7b164fcc0536", Convert.ToHexString(Md4.Hash(Encoding.ASCII.GetBytes(
            "12345678901234567890123456789012345678901234567890123456789012345678901234567890"))).ToLowerInvariant());
    }

    [Fact]
    public async Task OnlyChangedBlocksAreDownloadedAndTheResultIsExact()
    {
        // The new version: an inserted piece (shifts everything after it), a changed piece and a new tail.
        var old = Random(600_000, 1);
        var insert = Random(5000, 2);
        var changed = old[..200_000].Concat(insert).Concat(old[200_000..]).ToArray();
        Random(3000, 3).CopyTo(changed, 400_000);
        var next = changed.Concat(Random(7777, 4)).ToArray();
        var control = ZsyncControl.Parse(Make(next))!;
        Assert.NotNull(control);
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var oldFile = Path.Combine(dir.FullName, "old");
            var target = Path.Combine(dir.FullName, "new");
            await File.WriteAllBytesAsync(oldFile, old);
            var server = new RangeServer(next);
            using var http = new HttpClient(server);
            var got = await Zsync.BuildAsync(control, oldFile, new Uri("https://example.invalid/app"), target, http, null, CancellationToken.None);
            Assert.True(got > 0);
            Assert.True(got < next.Length / 10, $"downloaded {got} of {next.Length}");
            Assert.Equal(got, server.Sent);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(next)), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(target))));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task ServerWithoutRangesOrAWrongResultIsRefused()
    {
        var old = Random(100_000, 5);
        var next = old.ToArray();
        Random(4096, 6).CopyTo(next, 50_000);
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var oldFile = Path.Combine(dir.FullName, "old");
            var target = Path.Combine(dir.FullName, "new");
            await File.WriteAllBytesAsync(oldFile, old);
            using (var http = new HttpClient(new RangeServer(next) { IgnoreRanges = true }))
                await Assert.ThrowsAsync<IOException>(() => Zsync.BuildAsync(ZsyncControl.Parse(Make(next))!, oldFile, new Uri("https://example.invalid/a"), target, http, null, CancellationToken.None));
            // The server sends other bytes than the control file describes: the rebuilt file is deleted.
            var tampered = next.ToArray();
            tampered[51_000] ^= 0xFF;
            using (var http = new HttpClient(new RangeServer(tampered)))
                await Assert.ThrowsAsync<InvalidDataException>(() => Zsync.BuildAsync(ZsyncControl.Parse(Make(next))!, oldFile, new Uri("https://example.invalid/a"), target, http, null, CancellationToken.None));
            Assert.False(File.Exists(target));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task AMostlyNewFileIsLeftToTheFullDownload()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var oldFile = Path.Combine(dir.FullName, "old");
            await File.WriteAllBytesAsync(oldFile, Random(50_000, 7));
            var next = Random(50_000, 8);
            using var http = new HttpClient(new RangeServer(next));
            Assert.Null(await Zsync.BuildAsync(ZsyncControl.Parse(Make(next))!, oldFile, new Uri("https://example.invalid/a"), Path.Combine(dir.FullName, "n"), http, null, CancellationToken.None));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void ControlFilesThatAreNotRightAreRefused()
    {
        var file = Random(10_000, 9);
        var good = Make(file);
        Assert.NotNull(ZsyncControl.Parse(good));
        Assert.Null(ZsyncControl.Parse(good[..^1]));                                  // block list cut short
        Assert.Null(ZsyncControl.Parse(Encoding.ASCII.GetBytes("not a control file\n\n")));
        Assert.Null(ZsyncControl.Parse(Make(file, bs: 1000)));                          // not a power of two
        // Every supported hash-length shape round-trips.
        foreach (var (seq, rb, cb) in new[] { (1, 4, 16), (2, 3, 5), (1, 2, 8) })
            Assert.Equal(5, ZsyncControl.Parse(Make(file, seq: seq, rb: rb, cb: cb))!.BlockCount);
    }
}

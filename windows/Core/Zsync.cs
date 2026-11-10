using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Nexonic.Core;

/// <summary>
/// Differential updates from a zsync control file (the format zsyncmake writes next to a release file): the blocks of the
/// new file that the old file already has are copied from it, and only the rest is downloaded with HTTP range requests.
/// The result must match the control file's SHA-1 (and the caller's SHA-256), otherwise it is thrown away.
/// </summary>
public sealed class ZsyncControl
{
    public int BlockSize { get; private init; }
    public long Length { get; private init; }
    public int SeqMatches { get; private init; }
    public int RsumBytes { get; private init; }
    public int ChecksumBytes { get; private init; }
    public string Sha1 { get; private init; } = "";
    public int BlockCount => (int)((Length + BlockSize - 1) / BlockSize);
    internal uint[] Rsums = Array.Empty<uint>();
    internal byte[] Checksums = Array.Empty<byte>();

    /// <summary>Reads a control file; null when it is not one this code can use.</summary>
    public static ZsyncControl? Parse(byte[] data)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var at = 0;
        while (true)
        {
            var nl = Array.IndexOf(data, (byte)'\n', at);
            if (nl < 0 || nl - at > 4096) return null;
            var line = Encoding.ASCII.GetString(data, at, nl - at);
            at = nl + 1;
            if (line.Length == 0) break;
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        if (!headers.TryGetValue("zsync", out var version) || !version.StartsWith("0.6")) return null;
        if (!int.TryParse(headers.GetValueOrDefault("Blocksize"), out var bs) || bs < 512 || bs > 1 << 20 || (bs & (bs - 1)) != 0) return null;
        if (!long.TryParse(headers.GetValueOrDefault("Length"), out var length) || length <= 0) return null;
        var hl = (headers.GetValueOrDefault("Hash-Lengths") ?? "").Split(',');
        if (hl.Length != 3 || !int.TryParse(hl[0], out var seq) || !int.TryParse(hl[1], out var rb) || !int.TryParse(hl[2], out var cb)) return null;
        if (seq is < 1 or > 2 || rb is < 1 or > 4 || cb is < 3 or > 16) return null;
        var sha1 = headers.GetValueOrDefault("SHA-1") ?? "";
        if (sha1.Length != 40 || !sha1.All(Uri.IsHexDigit)) return null;
        var c = new ZsyncControl { BlockSize = bs, Length = length, SeqMatches = seq, RsumBytes = rb, ChecksumBytes = cb, Sha1 = sha1.ToLowerInvariant() };
        var n = c.BlockCount;
        if ((long)n * (rb + cb) != data.Length - at) return null;
        c.Rsums = new uint[n];
        c.Checksums = new byte[n * cb];
        for (var i = 0; i < n; i++)
        {
            uint r = 0;
            for (var k = 0; k < rb; k++) r = (r << 8) | data[at++];
            c.Rsums[i] = r;
            Buffer.BlockCopy(data, at, c.Checksums, i * cb, cb);
            at += cb;
        }
        return c;
    }

    /// <summary>The part of a 32-bit rolling sum (a in the high half, b in the low) that the file stores.</summary>
    internal uint Mask => RsumBytes switch { 1 => 0xFFu, 2 => 0xFFFFu, 3 => 0xFFFFFFu, _ => 0xFFFFFFFFu };
}

public static class Zsync
{
    /// <summary>zsync's rolling checksum of one block: a = Σc, b = Σ(len − i)·c, both 16-bit, as (a &lt;&lt; 16) | b.</summary>
    public static uint Rsum(ReadOnlySpan<byte> block)
    {
        ushort a = 0, b = 0;
        var len = block.Length;
        foreach (var c in block) { a += c; b += (ushort)(len-- * c); }
        return ((uint)a << 16) | b;
    }

    /// <summary>For each block of the new file, where the old data has it (an offset), or −1 when it must be downloaded.</summary>
    public static long[] Match(ZsyncControl c, byte[] old)
    {
        // The old data padded with zeros, as the new file's last block is.
        var data = new byte[old.Length + 2 * c.BlockSize];
        old.CopyTo(data, 0);
        return Match(c, data, old.Length);
    }

    /// <param name="data">The old file followed by at least two blocks of zeros.</param>
    static long[] Match(ZsyncControl c, byte[] data, int limit)
    {
        int bs = c.BlockSize, n = c.BlockCount;
        var found = new long[n];
        Array.Fill(found, -1L);
        var mask = c.Mask;
        var pairs = c.SeqMatches == 2 && n > 1;
        // Candidates by their stored sums: a block and (when the file asks for two in a row) the block after it.
        var index = new Dictionary<ulong, List<int>>();
        for (var i = 0; i < n; i++)
        {
            if (pairs && i + 1 == n) break;  // the last block stands alone (below)
            var key = pairs ? ((ulong)c.Rsums[i] << 32) | c.Rsums[i + 1] : c.Rsums[i];
            if (!index.TryGetValue(key, out var list)) index[key] = list = new List<int>();
            list.Add(i);
        }
        var lastAlone = pairs ? new Dictionary<uint, List<int>> { [c.Rsums[n - 1]] = new() { n - 1 } } : null;
        if (limit == 0) return found;
        var md4Block = new byte[bs];
        var w1 = Rsum(data.AsSpan(0, bs));
        var w2 = Rsum(data.AsSpan(bs, bs));
        var shift = (ushort)bs;
        for (var p = 0; p < limit;)
        {
            var matched = false;
            var r1 = w1 & mask;
            if (pairs)
            {
                if (index.TryGetValue(((ulong)r1 << 32) | (w2 & mask), out var list)) matched = Confirm(c, data, p, list, found, 2, md4Block);
                if (!matched && lastAlone!.TryGetValue(r1, out var last) && found[n - 1] < 0) matched = Confirm(c, data, p, last, found, 1, md4Block);
            }
            else if (index.TryGetValue(r1, out var list)) matched = Confirm(c, data, p, list, found, 1, md4Block);
            if (matched && p + bs < limit)
            {
                p += bs;
                w1 = Rsum(data.AsSpan(p, bs));
                w2 = Rsum(data.AsSpan(p + bs, bs));
                continue;
            }
            // Roll both windows one byte on.
            w1 = Roll(w1, data[p], data[p + bs], shift);
            w2 = Roll(w2, data[p + bs], p + 2 * bs < data.Length ? data[p + 2 * bs] : (byte)0, shift);
            p++;
        }
        return found;
    }

    static uint Roll(uint r, byte gone, byte come, ushort bs)
    {
        var a = (ushort)((r >> 16) - gone + come);
        var b = (ushort)((ushort)r - (ushort)(bs * gone) + a);
        return ((uint)a << 16) | b;
    }

    /// <summary>Checks the strong (MD4) sums of the candidate block(s) at <paramref name="p"/> and records the ones that match.</summary>
    static bool Confirm(ZsyncControl c, byte[] data, int p, List<int> candidates, long[] found, int run, byte[] scratch)
    {
        var any = false;
        foreach (var i in candidates)
        {
            var ok = true;
            for (var k = 0; k < run && ok; k++)
                ok = Md4.Hash(data.AsSpan(p + k * c.BlockSize, c.BlockSize)).AsSpan(0, c.ChecksumBytes)
                    .SequenceEqual(c.Checksums.AsSpan((i + k) * c.ChecksumBytes, c.ChecksumBytes));
            if (!ok) continue;
            for (var k = 0; k < run; k++) if (found[i + k] < 0) found[i + k] = p + (long)k * c.BlockSize;
            any = true;
        }
        return any;
    }

    /// <summary>The byte ranges (start, end inclusive) still to download, neighbouring blocks merged.</summary>
    public static List<(long Start, long End)> Missing(ZsyncControl c, long[] found)
    {
        var list = new List<(long, long)>();
        for (var i = 0; i < found.Length; i++)
        {
            if (found[i] >= 0) continue;
            var start = (long)i * c.BlockSize;
            var end = Math.Min(c.Length, start + c.BlockSize) - 1;
            if (list.Count > 0 && list[^1].Item2 + 1 == start) list[^1] = (list[^1].Item1, end);
            else list.Add((start, end));
        }
        return list;
    }

    /// <summary>
    /// Builds the new file at <paramref name="target"/> from <paramref name="oldFile"/> plus the missing ranges of
    /// <paramref name="url"/>. Returns the bytes downloaded, or null when it is not worth it (most of the file changed) —
    /// the caller then downloads the whole file. Throws when the server or the result is not right.
    /// </summary>
    public static async Task<long?> BuildAsync(ZsyncControl c, string oldFile, Uri url, string target, HttpClient http,
        IProgress<double>? progress, CancellationToken ct)
    {
        byte[] old;
        int oldLength;
        await using (var o = File.OpenRead(oldFile))
        {
            if (o.Length > int.MaxValue - 4 * c.BlockSize) return null;
            oldLength = (int)o.Length;
            old = new byte[oldLength + 2 * c.BlockSize];
            await o.ReadExactlyAsync(old.AsMemory(0, oldLength), ct);
        }
        var found = await Task.Run(() => Match(c, old, oldLength), ct);
        var missing = Missing(c, found);
        var toFetch = missing.Sum(m => m.End - m.Start + 1);
        if (toFetch > c.Length * 7 / 10 || missing.Count > 2000) return null;
        await using (var f = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            f.SetLength(c.Length);
            for (var i = 0; i < found.Length; i++)
            {
                if (found[i] < 0) continue;
                var len = (int)Math.Min(c.BlockSize, c.Length - (long)i * c.BlockSize);
                f.Position = (long)i * c.BlockSize;
                await f.WriteAsync(old.AsMemory((int)found[i], len), ct);
            }
            long got = 0;
            var buffer = new byte[81920];
            foreach (var (start, end) in missing)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(start, end);
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (res.StatusCode != HttpStatusCode.PartialContent || res.Content.Headers.ContentRange?.From != start)
                    throw new IOException("The update server does not send parts of files.");
                f.Position = start;
                await using var body = await res.Content.ReadAsStreamAsync(ct);
                long left = end - start + 1;
                while (left > 0)
                {
                    var r = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct);
                    if (r == 0) throw new IOException("The update download stopped early.");
                    await f.WriteAsync(buffer.AsMemory(0, r), ct);
                    left -= r;
                    got += r;
                    progress?.Report((double)got / Math.Max(1, toFetch));
                }
            }
        }
        string sha;
        await using (var check = File.OpenRead(target)) sha = Convert.ToHexString(await SHA1.HashDataAsync(check, ct)).ToLowerInvariant();
        if (sha != c.Sha1) { File.Delete(target); throw new InvalidDataException("The rebuilt update did not match the published file."); }
        return toFetch;
    }
}

/// <summary>MD4 (RFC 1320), only for zsync's block sums; it is not used for anything that needs to be secure.</summary>
public static class Md4
{
    public static byte[] Hash(ReadOnlySpan<byte> input)
    {
        uint a = 0x67452301, b = 0xefcdab89, c = 0x98badcfe, d = 0x10325476;
        var total = ((input.Length + 8) / 64 + 1) * 64;
        var msg = new byte[total];
        input.CopyTo(msg);
        msg[input.Length] = 0x80;
        BinaryPrimitives.WriteUInt64LittleEndian(msg.AsSpan(total - 8), (ulong)input.Length * 8);
        var x = new uint[16];
        for (var off = 0; off < total; off += 64)
        {
            for (var i = 0; i < 16; i++) x[i] = BinaryPrimitives.ReadUInt32LittleEndian(msg.AsSpan(off + i * 4));
            uint aa = a, bb = b, cc = c, dd = d;
            static uint L(uint v, int s) => (v << s) | (v >> (32 - s));
            foreach (var i in new[] { 0, 4, 8, 12 })
            {
                a = L(a + ((b & c) | (~b & d)) + x[i], 3);
                d = L(d + ((a & b) | (~a & c)) + x[i + 1], 7);
                c = L(c + ((d & a) | (~d & b)) + x[i + 2], 11);
                b = L(b + ((c & d) | (~c & a)) + x[i + 3], 19);
            }
            foreach (var i in new[] { 0, 1, 2, 3 })
            {
                a = L(a + ((b & c) | (b & d) | (c & d)) + x[i] + 0x5a827999, 3);
                d = L(d + ((a & b) | (a & c) | (b & c)) + x[i + 4] + 0x5a827999, 5);
                c = L(c + ((d & a) | (d & b) | (a & b)) + x[i + 8] + 0x5a827999, 9);
                b = L(b + ((c & d) | (c & a) | (d & a)) + x[i + 12] + 0x5a827999, 13);
            }
            foreach (var i in new[] { 0, 2, 1, 3 })
            {
                a = L(a + (b ^ c ^ d) + x[i] + 0x6ed9eba1, 3);
                d = L(d + (a ^ b ^ c) + x[i + 8] + 0x6ed9eba1, 9);
                c = L(c + (d ^ a ^ b) + x[i + 4] + 0x6ed9eba1, 11);
                b = L(b + (c ^ d ^ a) + x[i + 12] + 0x6ed9eba1, 15);
            }
            a += aa; b += bb; c += cc; d += dd;
        }
        var result = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(result, a);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), b);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), c);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), d);
        return result;
    }
}

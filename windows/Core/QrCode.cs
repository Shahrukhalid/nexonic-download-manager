using System.Text;

namespace Nexonic.Core;

/// <summary>
/// QR code (ISO/IEC 18004) for showing download links to a phone: byte mode, error correction level L or M,
/// versions 1–40. Written for this app; the phone's camera opens the nexonic:// link it holds.
/// </summary>
public sealed class QrCode
{
    public enum Level { L, M }

    public int Version { get; }
    public int Size { get; }
    public int Mask { get; }
    readonly bool[,] dark;      // [y, x]
    readonly bool[,] function;  // [y, x]

    public bool this[int x, int y] => x >= 0 && y >= 0 && x < Size && y < Size && dark[y, x];

    static readonly int[] EccPerBlockL = { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 };
    static readonly int[] EccPerBlockM = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 };
    static readonly int[] BlocksL = { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 };
    static readonly int[] BlocksM = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 };

    QrCode(int version, int mask)
    {
        Version = version;
        Size = version * 4 + 17;
        Mask = mask;
        dark = new bool[Size, Size];
        function = new bool[Size, Size];
    }

    /// <summary>Encodes text (UTF-8) in the smallest version that fits; throws when it is too long for any version.</summary>
    public static QrCode Encode(string text, Level level = Level.M, int? version = null, int? mask = null)
    {
        var data = Encoding.UTF8.GetBytes(text);
        var ver = version ?? Enumerable.Range(1, 40).FirstOrDefault(v => Fits(data.Length, v, level));
        if (ver == 0 || !Fits(data.Length, ver, level)) throw new ArgumentException("Too much text for a QR code");
        var codewords = AddEcc(DataCodewords(data, ver, level), ver, level);
        if (mask is int m) return Build(ver, level, m, codewords);
        QrCode? best = null;
        var bestPenalty = int.MaxValue;
        for (var m2 = 0; m2 < 8; m2++)
        {
            var q = Build(ver, level, m2, codewords);
            var p = q.Penalty();
            if (p < bestPenalty) { best = q; bestPenalty = p; }
        }
        return best!;
    }

    static int CountBits(int version) => version <= 9 ? 8 : 16;

    static int RawDataModules(int ver)
    {
        var result = (16 * ver + 128) * ver + 64;
        if (ver >= 2)
        {
            var align = ver / 7 + 2;
            result -= (25 * align - 10) * align - 55;
            if (ver >= 7) result -= 36;
        }
        return result;
    }

    static int DataCapacity(int ver, Level level)
    {
        var ecc = level == Level.L ? EccPerBlockL[ver] : EccPerBlockM[ver];
        var blocks = level == Level.L ? BlocksL[ver] : BlocksM[ver];
        return RawDataModules(ver) / 8 - ecc * blocks;
    }

    static bool Fits(int bytes, int ver, Level level) => 4 + CountBits(ver) + bytes * 8 <= DataCapacity(ver, level) * 8;

    static byte[] DataCodewords(byte[] data, int ver, Level level)
    {
        var bits = new List<bool>();
        void Append(int value, int count) { for (var i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
        Append(0b0100, 4);                 // byte mode
        Append(data.Length, CountBits(ver));
        foreach (var b in data) Append(b, 8);
        var capacity = DataCapacity(ver, level) * 8;
        Append(0, Math.Min(4, capacity - bits.Count));   // terminator
        Append(0, (8 - bits.Count % 8) % 8);
        for (var pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(pad, 8);
        var result = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++) if (bits[i]) result[i >> 3] |= (byte)(0x80 >> (i & 7));
        return result;
    }

    static byte[] AddEcc(byte[] data, int ver, Level level)
    {
        var blocks = level == Level.L ? BlocksL[ver] : BlocksM[ver];
        var eccLen = level == Level.L ? EccPerBlockL[ver] : EccPerBlockM[ver];
        var raw = RawDataModules(ver) / 8;
        var shortBlocks = blocks - raw % blocks;
        var shortLen = raw / blocks;
        var divisor = RsDivisor(eccLen);
        var dataBlocks = new List<byte[]>();
        var eccBlocks = new List<byte[]>();
        for (int i = 0, k = 0; i < blocks; i++)
        {
            var len = shortLen - eccLen + (i < shortBlocks ? 0 : 1);
            var block = data.AsSpan(k, len).ToArray();
            k += len;
            dataBlocks.Add(block);
            eccBlocks.Add(RsRemainder(block, divisor));
        }
        var result = new List<byte>(raw);
        for (var i = 0; i < dataBlocks.Max(b => b.Length); i++)
            foreach (var b in dataBlocks) if (i < b.Length) result.Add(b[i]);
        for (var i = 0; i < eccLen; i++)
            foreach (var e in eccBlocks) result.Add(e[i]);
        return result.ToArray();
    }

    static byte Multiply(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    static byte[] RsDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    static byte[] RsRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++) result[i] ^= Multiply(divisor[i], factor);
        }
        return result;
    }

    static QrCode Build(int ver, Level level, int mask, byte[] codewords)
    {
        var q = new QrCode(ver, mask);
        q.DrawFunctionPatterns(level);
        q.DrawCodewords(codewords);
        q.ApplyMask(mask);
        q.DrawFormatBits(level, mask);
        return q;
    }

    void Set(int x, int y, bool isDark)
    {
        dark[y, x] = isDark;
        function[y, x] = true;
    }

    void DrawFunctionPatterns(Level level)
    {
        for (var i = 0; i < Size; i++)
        {
            Set(6, i, i % 2 == 0);
            Set(i, 6, i % 2 == 0);
        }
        Finder(3, 3);
        Finder(Size - 4, 3);
        Finder(3, Size - 4);
        var align = AlignmentPositions();
        for (var i = 0; i < align.Length; i++)
            for (var j = 0; j < align.Length; j++)
            {
                var corner = (i == 0 && j == 0) || (i == 0 && j == align.Length - 1) || (i == align.Length - 1 && j == 0);
                if (corner) continue;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                        Set(align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        DrawFormatBits(level, 0);   // reserves the area; drawn again after masking
        if (Version >= 7)
        {
            var rem = Version;
            for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            var bits = (Version << 12) | rem;
            for (var i = 0; i < 18; i++)
            {
                var bit = ((bits >> i) & 1) != 0;
                var a = Size - 11 + i % 3;
                var b = i / 3;
                Set(a, b, bit);
                Set(b, a, bit);
            }
        }
    }

    void Finder(int cx, int cy)
    {
        for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                var d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                Set(x, y, d != 2 && d != 4);
            }
    }

    int[] AlignmentPositions()
    {
        if (Version == 1) return Array.Empty<int>();
        var count = Version / 7 + 2;
        var step = Version == 32 ? 26 : (Version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
        var result = new int[count];
        result[0] = 6;
        for (int i = count - 1, pos = Size - 7; i >= 1; i--, pos -= step) result[i] = pos;
        return result;
    }

    void DrawFormatBits(Level level, int mask)
    {
        var data = (level == Level.L ? 1 : 0) << 3 | mask;
        var rem = data;
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;
        for (var i = 0; i <= 5; i++) Set(8, i, Bit(i));
        Set(8, 7, Bit(6));
        Set(8, 8, Bit(7));
        Set(7, 8, Bit(8));
        for (var i = 9; i < 15; i++) Set(14 - i, 8, Bit(i));
        for (var i = 0; i < 8; i++) Set(Size - 1 - i, 8, Bit(i));
        for (var i = 8; i < 15; i++) Set(8, Size - 15 + i, Bit(i));
        Set(8, Size - 8, true);
    }

    void DrawCodewords(byte[] data)
    {
        var i = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vert = 0; vert < Size; vert++)
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vert : vert;
                    if (function[y, x] || i >= data.Length * 8) continue;
                    dark[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                    i++;
                }
        }
    }

    void ApplyMask(int mask)
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                if (function[y, x]) continue;
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert) dark[y, x] = !dark[y, x];
            }
    }

    /// <summary>Penalty from the standard's four rules; the mask with the lowest one is used.</summary>
    int Penalty()
    {
        var penalty = 0;
        for (var pass = 0; pass < 2; pass++)
            for (var a = 0; a < Size; a++)
            {
                var run = 1;
                for (var b = 1; b < Size; b++)
                {
                    bool cur = pass == 0 ? dark[a, b] : dark[b, a], prev = pass == 0 ? dark[a, b - 1] : dark[b - 1, a];
                    if (cur == prev) { run++; if (run == 5) penalty += 3; else if (run > 5) penalty++; }
                    else run = 1;
                }
                // Finder-like 1:1:3:1:1 patterns with four light modules on one side.
                for (var b = 0; b + 10 < Size + 4; b++)
                {
                    bool M(int k) => k >= 0 && k < Size && (pass == 0 ? dark[a, k] : dark[k, a]);
                    var core = M(b) && !M(b + 1) && M(b + 2) && M(b + 3) && M(b + 4) && !M(b + 5) && M(b + 6);
                    if (!core) continue;
                    if (!M(b + 7) && !M(b + 8) && !M(b + 9) && !M(b + 10)) penalty += 40;
                    if (!M(b - 1) && !M(b - 2) && !M(b - 3) && !M(b - 4)) penalty += 40;
                }
            }
        for (var y = 0; y + 1 < Size; y++)
            for (var x = 0; x + 1 < Size; x++)
            {
                var c = dark[y, x];
                if (c == dark[y, x + 1] && c == dark[y + 1, x] && c == dark[y + 1, x + 1]) penalty += 3;
            }
        var darkCount = 0;
        foreach (var d in dark) if (d) darkCount++;
        var total = Size * Size;
        var k2 = (Math.Abs(darkCount * 20 - total * 10) + total - 1) / total - 1;
        return penalty + Math.Max(0, k2) * 10;
    }
}

/// <summary>Download links for a QR code: a nexonic://add link that the phone app opens. Never holds cookies or passwords.</summary>
public static class ShareLinks
{
    /// <summary>Longest link that still scans easily from a screen.</summary>
    public const int MaxLength = 1200;

    /// <summary>The link for as many of the downloads as fit, and how many that is.</summary>
    public static (string Link, int Count) Link(IEnumerable<string> urls)
    {
        var sb = new StringBuilder("nexonic://add");
        var n = 0;
        foreach (var url in urls.Where(u => new DownloadJob(u).IsValid).Select(Clean).Distinct())
        {
            var part = (n == 0 ? "?" : "&") + "url=" + Uri.EscapeDataString(url);
            if (sb.Length + part.Length > MaxLength) break;
            sb.Append(part);
            n++;
        }
        return (n == 0 ? "" : sb.ToString(), n);
    }

    /// <summary>The link without a user name or password (https://user:pass@host/… → https://host/…).</summary>
    public static string Clean(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.UserInfo.Length == 0) return url;
        return new UriBuilder(u) { UserName = "", Password = "" }.Uri.AbsoluteUri;
    }

    static readonly string[] PrivateKeys =
    [
        "token", "access_token", "auth", "authorization", "key", "apikey", "api_key", "sig", "signature", "session",
        "sessionid", "sid", "password", "pass", "secret", "x-amz-signature", "x-amz-credential", "x-goog-signature",
        "googleaccessid", "se", "sp", "sv", "hdnts", "policy", "key-pair-id", "hmac", "expires",
    ];

    /// <summary>True when a link seems to carry a private access key (a token or signed-link parameter), so anyone
    /// who scans the code could use it. The app warns; the user decides.</summary>
    public static bool LooksPrivate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.UserInfo.Length > 0) return true;
        foreach (var pair in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(pair.Split('=')[0]).ToLowerInvariant();
            if (PrivateKeys.Contains(name) || name.EndsWith("token") || name.EndsWith("signature")) return true;
        }
        return false;
    }
}

using System.Security.Cryptography;

namespace Nexonic.Core;

public enum ChecksumAlgorithm { Md5, Sha1, Sha256, Sha512 }

public static class Checksum
{
    public static string Name(ChecksumAlgorithm a) => a switch
    {
        ChecksumAlgorithm.Md5 => "MD5",
        ChecksumAlgorithm.Sha1 => "SHA-1",
        ChecksumAlgorithm.Sha256 => "SHA-256",
        _ => "SHA-512",
    };

    /// <summary>Reads "sha256:abc…", "SHA-256 abc…" or a bare hex value; the length of the hex value picks the algorithm.</summary>
    public static (ChecksumAlgorithm Algorithm, string Hex)? Parse(string? text)
    {
        var hex = (text ?? "").ToLowerInvariant().Split(new[] { ':', ' ', '=' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        if (hex.Length == 0 || !hex.All(Uri.IsHexDigit)) return null;
        return hex.Length switch
        {
            32 => (ChecksumAlgorithm.Md5, hex),
            40 => (ChecksumAlgorithm.Sha1, hex),
            64 => (ChecksumAlgorithm.Sha256, hex),
            128 => (ChecksumAlgorithm.Sha512, hex),
            _ => null,
        };
    }

    static HashAlgorithm Create(ChecksumAlgorithm a) => a switch
    {
        ChecksumAlgorithm.Md5 => MD5.Create(),
        ChecksumAlgorithm.Sha1 => SHA1.Create(),
        ChecksumAlgorithm.Sha256 => SHA256.Create(),
        _ => SHA512.Create(),
    };

    public static string Compute(ChecksumAlgorithm a, byte[] data)
    {
        using var h = Create(a);
        return Convert.ToHexString(h.ComputeHash(data)).ToLowerInvariant();
    }

    public static string Compute(ChecksumAlgorithm a, string path)
    {
        using var h = Create(a);
        using var f = File.OpenRead(path);
        return Convert.ToHexString(h.ComputeHash(f)).ToLowerInvariant();
    }

    /// <summary>Calculates MD5, SHA-1, SHA-256 and SHA-512 in a single stream pass.</summary>
    public static (string Md5, string Sha1, string Sha256, string Sha512) ComputeAll(string path)
    {
        using var md5 = MD5.Create();
        using var sha1 = SHA1.Create();
        using var sha256 = SHA256.Create();
        using var sha512 = SHA512.Create();
        using var stream = File.OpenRead(path);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            md5.TransformBlock(buffer, 0, read, null, 0);
            sha1.TransformBlock(buffer, 0, read, null, 0);
            sha256.TransformBlock(buffer, 0, read, null, 0);
            sha512.TransformBlock(buffer, 0, read, null, 0);
        }
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        sha512.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return (
            Convert.ToHexString(md5.Hash!).ToLowerInvariant(),
            Convert.ToHexString(sha1.Hash!).ToLowerInvariant(),
            Convert.ToHexString(sha256.Hash!).ToLowerInvariant(),
            Convert.ToHexString(sha512.Hash!).ToLowerInvariant()
        );
    }

    /// <summary>"SHA-256 matches" or "SHA-256 does not match (got …)".</summary>
    public static string Verify(string expected, string path)
    {
        if (Parse(expected) is not { } p) return "Not a valid checksum";
        var actual = Compute(p.Algorithm, path);
        return actual == p.Hex ? $"{Name(p.Algorithm)} matches" : $"{Name(p.Algorithm)} does not match (got {actual})";
    }
}

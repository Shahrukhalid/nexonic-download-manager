using Nexonic.Core;
using Xunit;

namespace Nexonic.Tests;

public class QrTests
{
    // Version 2, level M, mask 3, byte mode "nexonic://add?url=x", as made by an independent QR encoder.
    const string Reference =
        "1111111011101110001111111100000101111100000100000110111010011001100010111011011101011101110001011101101110100100110110101110110000010010111001010000011111111010101010101111111000000001101101100000000010110111000001000010010111101000100011100100001110111100101100010010110000000111001100000000110111100111101000001011111011111011010000101101111101111101111111100101000001110101000110010010010111010000000010111100101111111010100000000101000111000110011111111010000110101011111100000101011011010001100110111010011001101111100001011101010100001011111111101110101000001101101101010000010010111100100111001111111011010000001001111";

    [Fact]
    public void MatchesAReferenceEncoder()
    {
        var q = QrCode.Encode("nexonic://add?url=x", QrCode.Level.M, version: 2, mask: 3);
        Assert.Equal(25, q.Size);
        var bits = string.Concat(Enumerable.Range(0, q.Size * q.Size).Select(i => q[i % q.Size, i / q.Size] ? '1' : '0'));
        Assert.Equal(Reference, bits);
    }

    [Fact]
    public void PicksTheSmallestVersionAndRejectsTooMuch()
    {
        Assert.Equal(1, QrCode.Encode("hello").Version);
        Assert.Equal(40, QrCode.Encode(new string('a', 2331)).Version);
        Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('a', 2332)));
    }

    [Fact]
    public void ShareLinksHoldOnlyWebLinksAndStayShort()
    {
        var (link, n) = ShareLinks.Link(new[] { "https://example.com/a b.zip", "file:///etc/passwd", "https://example.com/a b.zip", "http://x.org/c?d=1&e=2" });
        Assert.Equal(2, n);
        Assert.Equal("nexonic://add?url=https%3A%2F%2Fexample.com%2Fa%20b.zip&url=http%3A%2F%2Fx.org%2Fc%3Fd%3D1%26e%3D2", link);
        var many = Enumerable.Range(0, 100).Select(i => $"https://example.com/files/{i:000}/archive-{i}.zip");
        var (big, count) = ShareLinks.Link(many);
        Assert.True(big.Length <= ShareLinks.MaxLength);
        Assert.InRange(count, 10, 99);
        Assert.Equal(("", 0), ShareLinks.Link(new[] { "not a link" }));
    }

    [Fact]
    public void ShareLinksDropPasswordsAndFlagPrivateLinks()
    {
        var (link, _) = ShareLinks.Link(new[] { "https://me:secret@example.com/f.zip" });
        Assert.DoesNotContain("secret", link);
        Assert.Contains(Uri.EscapeDataString("https://example.com/f.zip"), link);
        Assert.True(ShareLinks.LooksPrivate("https://me:secret@example.com/f.zip"));
        Assert.True(ShareLinks.LooksPrivate("https://bucket.s3.amazonaws.com/f.zip?X-Amz-Signature=abc&X-Amz-Expires=60"));
        Assert.True(ShareLinks.LooksPrivate("https://cdn.example.com/f.zip?access_token=abc"));
        Assert.True(ShareLinks.LooksPrivate("https://cdn.example.com/f.zip?downloadToken=abc"));
        Assert.False(ShareLinks.LooksPrivate("https://example.com/f.zip?version=2&lang=en"));
        Assert.False(ShareLinks.LooksPrivate("not a link"));
    }
}

using System.Net;
using Nexonic.Core;
using Xunit;

namespace Nexonic.Tests;

/// <summary>Custom categories, image galleries, IP version, remote access checks and pairing.</summary>
[Collection("Settings")]
public class ServerAndGalleryTests
{
    [Fact]
    public void CustomCategoriesComeFirstAndKeepTheirFolder()
    {
        var books = Path.Combine(Path.GetTempPath(), "books");
        var (list, problem) = CustomCategory.ParseLines($"Comics: cbz cbr > Comics\nBooks: epub pdf > {books}\n# a note\n\nZips: .ZIP");
        Assert.Null(problem);
        Assert.Equal(3, list.Count);
        var keep = FileCategories.Custom;
        try
        {
            FileCategories.Custom = list;
            Assert.Equal(Path.Combine("/dl", "Comics"), FileCategories.TargetFolder("/dl", "issue 1.CBZ", sortBuiltIn: false));
            Assert.Equal(books, FileCategories.TargetFolder("/dl", "a.pdf", sortBuiltIn: true));
            Assert.Equal(Path.Combine("/dl", "Zips"), FileCategories.TargetFolder("/dl", "x.zip", sortBuiltIn: true));
            Assert.Equal(Path.Combine("/dl", "Video"), FileCategories.TargetFolder("/dl", "x.mp4", sortBuiltIn: true));
            Assert.Equal("/dl", FileCategories.TargetFolder("/dl", "x.mp4", sortBuiltIn: false));
            Assert.Equal("Books", FileCategories.NameOf("x.epub"));
            Assert.Equal("Music", FileCategories.NameOf("x.mp3"));
        }
        finally { FileCategories.Custom = keep; }
        Assert.Equal($"Comics: cbz cbr > Comics\nBooks: epub pdf > {books}\nZips: zip", CustomCategory.ToLines(list));
    }

    [Fact]
    public void CustomCategoryLinesThatCannotBeRead()
    {
        Assert.Contains("Line 1", CustomCategory.ParseLines("no colon here").Problem!);
        Assert.Contains("Line 2", CustomCategory.ParseLines("A: zip\nEmpty:  > folder").Problem!);
        // A type belongs to the first category that lists it; folders cannot climb out of the download folder.
        var clean = CustomCategory.Clean(new[]
        {
            new CustomCategory { Name = "A", Extensions = "zip rar" },
            new CustomCategory { Name = "B", Extensions = "zip" },
            new CustomCategory { Name = "C", Extensions = "iso", Folder = "../outside" },
        });
        Assert.Equal(2, clean.Count);
        Assert.Null(clean[1].FolderPath("/dl"));
    }

    [Fact]
    public void GalleryFindsFullSizePicturesInPageOrder()
    {
        const string html = """
            <html><head><title>Summer  &amp; Sea</title><meta property="og:image" content="/cover.jpg"></head><body>
            <a href="full/1.jpg"><img src="thumb/1.jpg" width="150" height="100"></a>
            <img src="icon.png" width="16" height="16">
            <img data-src="lazy.webp" src="data:image/gif;base64,R0lGOD">
            <img srcset="small.jpg 480w, large.jpg 1600w, medium.jpg 800w" src="small.jpg">
            <picture><source type="image/avif" srcset="p.avif 1x, p@2x.avif 2x"></picture>
            <a href="page.html">not a picture</a><img src="logo.svg">
            </body></html>
            """;
        var page = new Uri("https://example.com/album/index.html");
        var found = ImageGallery.Find(html, page).Select(i => i.Url.AbsoluteUri).ToList();
        Assert.Equal(new[]
        {
            "https://example.com/cover.jpg", "https://example.com/album/full/1.jpg", "https://example.com/album/thumb/1.jpg",
            "https://example.com/album/lazy.webp", "https://example.com/album/large.jpg", "https://example.com/album/p@2x.avif",
        }, found);
        Assert.Equal("Summer & Sea", ImageGallery.FolderName(html, page));
        Assert.Equal("Gallery from example.com", ImageGallery.FolderName("<p>no title</p>", page));
        Assert.Equal("b.jpg", ImageGallery.Largest("a.jpg 1x, b.jpg 2x"));
        Assert.Equal("x.jpg?w=1,2", ImageGallery.Largest("x.jpg?w=1,2"));
        Assert.Null(ImageGallery.Largest(" "));
    }

    [Fact]
    public void IpVersionFiltersAddresses()
    {
        var both = new[] { IPAddress.Parse("2001:db8::1"), IPAddress.Parse("192.0.2.1") };
        Assert.Equal(IPAddress.Parse("192.0.2.1"), NetworkOptions.OfVersion(both, "ipv4").Single());
        Assert.Equal(IPAddress.Parse("2001:db8::1"), NetworkOptions.OfVersion(both, "ipv6").Single());
        Assert.Equal(2, NetworkOptions.OfVersion(both, "any").Length);
        Assert.Equal("any", new NetworkOptions { IpVersion = "bogus" }.Clamped().IpVersion);
        Assert.Equal("ipv6", new NetworkOptions { IpVersion = "ipv6" }.Clamped().IpVersion);
    }

    [Fact]
    public void RemoteAccessNeedsThePairingKeyAndRefusesOtherPages()
    {
        var key = AutomationSettings.NewKey();
        Dictionary<string, string> H(params (string, string)[] h) => h.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);
        Assert.Null(RemoteAccess.Check(H(("Host", "192.168.1.5:47813"), ("Authorization", "Bearer " + key)), key));
        Assert.Null(RemoteAccess.Check(H(("Host", "192.168.1.5:47813"), ("Origin", "http://192.168.1.5:47813"), ("Authorization", "Bearer " + key)), key));
        Assert.Equal(401, RemoteAccess.Check(H(("Host", "192.168.1.5:47813")), key)!.Value.Status);
        Assert.Equal(401, RemoteAccess.Check(H(("Host", "192.168.1.5:47813"), ("Authorization", "Bearer wrong")), key)!.Value.Status);
        Assert.Equal(403, RemoteAccess.Check(H(("Host", "192.168.1.5:47813"), ("Origin", "http://evil.example"), ("Authorization", "Bearer " + key)), key)!.Value.Status);
        Assert.Equal(401, RemoteAccess.Check(H(("Host", "x"), ("Authorization", "Bearer short")), "short")!.Value.Status);
    }

    [Fact]
    public void PairingAddressKeepsTheKeyInTheFragment()
    {
        var url = RemoteAccess.PairingUrl("192.168.1.5", 47813, "a+b/c");
        Assert.Equal("http://192.168.1.5:47813/#k=a%2Bb%2Fc", url);
        var art = RemoteAccess.TerminalQr(url);
        Assert.True(art.Split('\n').Length > 10);
        Assert.True(RemoteAccess.IsPrivate(IPAddress.Parse("192.168.1.5")));
        Assert.True(RemoteAccess.IsPrivate(IPAddress.Parse("10.0.0.2")));
        Assert.False(RemoteAccess.IsPrivate(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void BackupsNeverCarryThePairingKey()
    {
        var s = new AppSettings { Remote = new RemoteSettings { Enabled = true, Key = AutomationSettings.NewKey() } };
        var json = Backup.Export(s, new[] { DownloadQueue.Main() }, Array.Empty<DownloadRecord>(), "test");
        Assert.DoesNotContain(s.Remote.Key, json);
        var back = Backup.Parse(json).Settings!;
        Assert.False(back.Remote.Enabled);
        Assert.Equal("", back.Remote.Key);
    }
}

using Nexonic.Core;
using Xunit;

/// <summary>The gallery engine: picking the checked build, its arguments and reading its output.</summary>
public class GalleryEngineTests
{
    const string Release = """
        {"tag_name":"2026.11.09","assets":[
          {"name":"gallery-dl_linux","digest":"sha256:9DCB","browser_download_url":"https://github.com/gdl-org/builds/releases/download/2026.11.09/gallery-dl_linux"},
          {"name":"gallery-dl_windows.exe","digest":null,"browser_download_url":"https://github.com/gdl-org/builds/releases/download/2026.11.09/gallery-dl_windows.exe"},
          {"name":"gallery-dl_macos","digest":"sha256:aa","browser_download_url":"https://evil.test/gallery-dl_macos"}]}
        """;

    [Fact]
    public void PicksOnlyChecksummedBuildsFromGitHub()
    {
        Assert.Equal(("https://github.com/gdl-org/builds/releases/download/2026.11.09/gallery-dl_linux", "9dcb"), GalleryDl.Pick(Release, "gallery-dl_linux"));
        Assert.Null(GalleryDl.Pick(Release, "gallery-dl_windows.exe")); // no checksum
        Assert.Null(GalleryDl.Pick(Release, "gallery-dl_macos"));       // not from GitHub
        Assert.Null(GalleryDl.Pick(Release, "other"));
    }

    [Fact]
    public void AProfileListingBecomesPhotosAndVideosToChooseFrom()
    {
        Assert.Equal(new[] { "-j", "--range", "1-300", "-o", GalleryDl.Pace, "--config", "/p/g.json", "--", "https://site.test/u" },
            GalleryList.Arguments("https://site.test/u", 300, "/p/g.json"));
        const string json = """
            [[2, {"username": "u"}],
             [3, "https://scontent.cdninstagram.com/v/a.jpg?sig=1", {"extension": "jpg", "post_shortcode": "AbC", "description": "Sunny day\nat home", "num": 1, "count": 2, "date": "2025-03-25 10:00:00"}],
             [3, "https://scontent.cdninstagram.com/v/b.mp4?sig=2", {"extension": "mp4", "post_shortcode": "AbC", "description": "Sunny day\nat home", "num": 2, "count": 2}],
             [3, "https://scontent.cdninstagram.com/v/b.mp4?sig=2", {"extension": "mp4"}],
             [6, "https://site.test/other", {}]]
            """;
        var list = GalleryList.Entries(json, "u");
        Assert.Equal(2, list.Count);
        Assert.Equal(("Sunny day at home [AbC-1].jpg", "photo", true), (list[0].Title, list[0].Kind, list[0].Direct));
        Assert.Equal(("Sunny day at home [AbC-2].mp4", "video"), (list[1].Title, list[1].Kind));
        Assert.Equal(new DateTime(2025, 3, 25, 10, 0, 0, DateTimeKind.Utc), list[0].Date);
        Assert.Empty(GalleryList.Entries("not json", "u"));
        // Signed addresses of the same picture count as one file.
        Assert.Equal(ProfileBatch.Key("https://scontent.cdninstagram.com/v/a.jpg?sig=1"), ProfileBatch.Key("https://scontent.cdninstagram.com/v/a.jpg?sig=9"));
    }

    [Fact]
    public void ArgumentsEndWithTheAddress()
    {
        var args = GalleryDl.Arguments("https://site.test/user/a", "/data/Gallery", 500, "https://site.test/", "/p/g.json");
        Assert.Equal(new[] { "--directory", "/data/Gallery", "-o", GalleryDl.Pace, "--config", "/p/g.json", "--limit-rate", "500k", "-o", "extractor.referer=https://site.test/", "--", "https://site.test/user/a" }, args);
        Assert.Equal(new[] { "--directory", "/d", "-o", GalleryDl.Pace, "--", "-x" }, GalleryDl.Arguments("-x", "/d", 0, null));
    }

    [Fact]
    public void SiteLoginAndCookiesStayOffTheCommandLine()
    {
        Assert.Equal("", GalleryDl.Settings(null, "", null, null, ""));
        var json = GalleryDl.Settings("sid=a=b; theme=dark ;bad", "Agent/1", "me", "pw\"x", "http://p:8080");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var e = doc.RootElement.GetProperty("extractor");
        Assert.Equal("a=b", e.GetProperty("cookies").GetProperty("sid").GetString());
        Assert.Equal("dark", e.GetProperty("cookies").GetProperty("theme").GetString());
        Assert.Equal(2, e.GetProperty("cookies").EnumerateObject().Count());
        Assert.Equal("Agent/1", e.GetProperty("user-agent").GetString());
        Assert.Equal("me", e.GetProperty("username").GetString());
        Assert.Equal("pw\"x", e.GetProperty("password").GetString());
        Assert.Equal("http://p:8080", e.GetProperty("proxy").GetString());
        var file = GalleryDl.WriteSettings(json)!;
        try
        {
            Assert.Equal(json, File.ReadAllText(file));
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
        finally { File.Delete(file); }
        Assert.Null(GalleryDl.WriteSettings(""));
    }

    [Fact]
    public void ReadsSavedAndSkippedFiles()
    {
        var saved = Path.Combine(Path.GetTempPath(), "a.jpg");
        Assert.True(GalleryDl.SavedFile(saved, out var p1) == true);
        Assert.Equal(saved, p1);
        Assert.True(GalleryDl.SavedFile("# " + saved, out var p2) == false);
        Assert.Equal(saved, p2);
        Assert.Null(GalleryDl.SavedFile("[gallery-dl][error] Unsupported URL", out _));
    }

    [Fact]
    public void GalleryRecordsUseTheGalleryEngine()
    {
        var r = new DownloadRecord { Url = "https://site.test/g", Gallery = true, StreamBytes = 1234 };
        Assert.Equal(DownloadEngine.Gallery, DownloadProviders.EngineOf(r));
        Assert.Equal(1234, r.Received);
        Assert.True(new DownloadProviders().Create(r, new DownloadContext(1, new(), false, new() { Mode = ProxyMode.None }, new())) is GalleryTransfer);
    }
}

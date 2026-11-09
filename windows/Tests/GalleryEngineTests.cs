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
    public void ArgumentsEndWithTheAddress()
    {
        var args = GalleryDl.Arguments("https://site.test/user/a", "/data/Gallery", 500, "http://p:8080", "https://site.test/");
        Assert.Equal(new[] { "--directory", "/data/Gallery", "--limit-rate", "500k", "--proxy", "http://p:8080", "-o", "extractor.referer=https://site.test/", "--", "https://site.test/user/a" }, args);
        Assert.Equal(new[] { "--directory", "/d", "--", "-x" }, GalleryDl.Arguments("-x", "/d", 0, "", null));
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

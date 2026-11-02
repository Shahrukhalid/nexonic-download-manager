using Nexonic.Core;
using Xunit;

/// <summary>latest.json (attached to each release by release.yml) reads the same as GitHub's API answer.</summary>
public class MirrorTests
{
    [Fact]
    public void LatestJsonReadsLikeTheApi()
    {
        const string json = """
        {"tag_name": "v0.6.5", "html_url": "https://github.com/Shahrukhalid/nexonic-download-manager/releases/tag/v0.6.5",
         "draft": false, "prerelease": false, "body": "## 0.6.5\n- x",
         "assets": [{"name": "NexonicDownloadManager-Windows-Setup.exe",
                     "browser_download_url": "https://github.com/Shahrukhalid/nexonic-download-manager/releases/download/v0.6.5/NexonicDownloadManager-Windows-Setup.exe",
                     "digest": "sha256:ABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABAB"}]}
        """;
        var r = Updates.Parse(json)!;
        Assert.Equal("0.6.5", r.Version);
        var url = r.Assets["NexonicDownloadManager-Windows-Setup.exe"];
        Assert.True(Updates.TrustedAsset(url));
        Assert.Equal("abababababababababababababababababababababababababababababababab", Updates.Digest(r, "NexonicDownloadManager-Windows-Setup.exe"));
        Assert.True(Updates.MirrorUrl.StartsWith("https://github.com/", StringComparison.Ordinal));
    }
}

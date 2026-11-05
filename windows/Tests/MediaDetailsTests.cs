using Nexonic.Core;
using Xunit;

namespace Nexonic.Tests;

public sealed class MediaDetailsTests
{
    [Fact]
    public void VideosKeepDetailsAndChaptersWhenFfmpegIsThere()
    {
        var a = SiteVideo.Arguments("https://youtu.be/abc", "/o", "/t", "/app/ffmpeg", null, false, null, null, null, null, null, 0);
        Assert.Contains("--embed-metadata", a);
        Assert.Contains("--embed-chapters", a);
        Assert.Equal("https://youtu.be/abc", a[^1]);
        var noTool = SiteVideo.Arguments("https://youtu.be/abc", "/o", "/t", null, null, false, null, null, null, null, null, 0);
        Assert.DoesNotContain("--embed-metadata", noTool);
    }
}

using Nexonic.Core;
using Xunit;

/// <summary>The qualities a video is offered in, read from the video engine's details, and how a chosen one is fetched.</summary>
public class QualityTests
{
    const string Sample = """
    {"title": "Talk", "duration": 100, "formats": [
      {"format_id": "sb0", "vcodec": "none", "acodec": "none", "height": 90, "width": 160},
      {"format_id": "140", "vcodec": "none", "acodec": "mp4a.40.2", "abr": 129, "filesize": 4000000},
      {"format_id": "251", "vcodec": "none", "acodec": "opus", "abr": 140, "filesize": 4500000},
      {"format_id": "18", "vcodec": "avc1.42001E", "acodec": "mp4a.40.2", "width": 640, "height": 360, "filesize": 10000000},
      {"format_id": "136", "vcodec": "avc1.4d401f", "acodec": "none", "width": 1280, "height": 720, "tbr": 2000},
      {"format_id": "248", "vcodec": "vp9", "acodec": "none", "width": 1920, "height": 1080, "tbr": 5000, "filesize": 90000000},
      {"format_id": "137", "vcodec": "avc1.640028", "acodec": "none", "width": 1920, "height": 1080, "tbr": 4000, "filesize": 100000000},
      {"format_id": "up", "vcodec": "avc1", "acodec": "none", "width": 1080, "height": 1920, "tbr": 1000, "filesize": 1}
    ]}
    """;

    [Fact]
    public void QualitiesOnOffer()
    {
        var c = SiteVideo.ParseChoices(Sample)!;
        Assert.Equal("Talk", c.Title);
        Assert.Equal(4_000_000, c.SoundSize);  // the m4a sound the download picks
        Assert.Equal(new[] { 1080, 720, 360 }, c.Video.Select(q => q.Height));  // an upright 1080×1920 picture counts as 1080p
        Assert.Equal(new long?[] { 104_000_000, 29_000_000, 10_000_000 }, c.Video.Select(q => q.Size));  // 360p already has sound
        Assert.Null(SiteVideo.ParseChoices("not json"));
    }

    [Fact]
    public void SizeDescribesTheFormatTheDownloadPicks()
    {
        // Frame rate comes before codec when the download chooses, so 60 fps VP9 beats 30 fps H.264 at the same size.
        const string json = """
        {"duration": 10, "formats": [
          {"vcodec": "avc1", "acodec": "none", "width": 1920, "height": 1080, "fps": 30, "filesize": 100},
          {"vcodec": "vp9", "acodec": "none", "width": 1920, "height": 1080, "fps": 60, "filesize": 150},
          {"vcodec": "avc1", "acodec": "none", "width": 1280, "height": 720, "fps": 30, "filesize": 50},
          {"vcodec": "vp9", "acodec": "none", "width": 1280, "height": 720, "fps": 30, "filesize": 40}
        ]}
        """;
        var c = SiteVideo.ParseChoices(json)!;
        Assert.Equal(new long?[] { 150, 50 }, c.Video.Select(q => q.Size));  // 1080p: the 60 fps one; 720p: same frame rate, so H.264
    }

    [Fact]
    public void ChosenQualityLimitsThePicture()
    {
        Assert.Equal("res:720,fps,hdr:12,vcodec:avc,acodec:m4a", SiteVideo.Format(false, 720)[3]);
        Assert.Equal("res,fps,hdr:12,vcodec:avc,acodec:m4a", SiteVideo.Format(false, null)[3]);
        Assert.Equal(new[] { "-f", "ba[ext=m4a]/ba/b" }, SiteVideo.Format(true, 720));
        var args = SiteVideo.Arguments("https://www.youtube.com/watch?v=x", "/tmp", "/tmp", null, null, false, null, null, null, null, null, 0, null, 480);
        Assert.Contains("res:480,fps,hdr:12,vcodec:avc,acodec:m4a", args);
        Assert.Equal("up to 2160p (4K)", SiteVideo.ChoiceText(null, 2160));
        Assert.Equal("sound only", SiteVideo.ChoiceText(true, 720));
        Assert.Equal("best quality", SiteVideo.ChoiceText(null, null));
    }
}

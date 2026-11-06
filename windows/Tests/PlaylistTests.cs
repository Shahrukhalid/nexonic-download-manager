using Nexonic.Core;
using Xunit;

namespace Nexonic.Tests;

public sealed class PlaylistTests
{
    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PL123", true)]
    [InlineData("https://www.youtube.com/@channel", true)]
    [InlineData("https://www.youtube.com/channel/UC123/videos", true)]
    [InlineData("https://www.youtube.com/watch?v=abc&list=PL123", false)]
    [InlineData("https://youtu.be/abc", false)]
    [InlineData("https://soundcloud.com/artist/sets/album", true)]
    [InlineData("https://example.com/file.zip", false)]
    public void KnowsPlaylistLinks(string url, bool expected) => Assert.Equal(expected, Playlists.IsPlaylist(url));

    [Fact]
    public void ChannelHomeListsItsVideos()
    {
        Assert.Equal("https://www.youtube.com/@channel/videos", Playlists.Normalize("https://www.youtube.com/@channel"));
        Assert.Equal("https://www.youtube.com/@channel/shorts", Playlists.Normalize("https://www.youtube.com/@channel/shorts"));
        var a = Playlists.Arguments("https://www.youtube.com/@channel", null, 50);
        Assert.Contains("--flat-playlist", a);
        Assert.Equal(new[] { "--", "https://www.youtube.com/@channel/videos" }, a.TakeLast(2));
        Assert.Equal("50", a[a.IndexOf("--playlist-end") + 1]);
    }

    [Fact]
    public void ReadsEntriesWithoutDuplicates()
    {
        var json = "{\"title\":\"Mix\",\"entries\":[{\"ie_key\":\"Youtube\",\"id\":\"abc\",\"url\":\"abc\",\"title\":\"One\",\"duration\":61}," +
                   "{\"url\":\"https://www.youtube.com/watch?v=def\",\"title\":\"Two\"},{\"url\":\"https://www.youtube.com/watch?v=def\"},{\"title\":\"no link\"}]}";
        var r = Playlists.Parse(json)!;
        Assert.Equal("Mix", r.Title);
        Assert.Equal(new[] { "https://www.youtube.com/watch?v=abc", "https://www.youtube.com/watch?v=def" }, r.Entries.Select(e => e.Url));
        Assert.Equal(61, r.Entries[0].Duration);
        Assert.Null(Playlists.Parse("not json"));
    }

    [Fact]
    public void SubtitlesAreOptionalAndNeverFailTheVideo()
    {
        Assert.Equal("ur.*,en.*", VideoSubtitles.Languages(new System.Globalization.CultureInfo("ur-PK")));
        Assert.Equal("en.*", VideoSubtitles.Languages(new System.Globalization.CultureInfo("en-GB")));
        Assert.Contains("--ignore-errors", VideoSubtitles.Arguments());
        Assert.DoesNotContain("--write-subs", SiteVideo.Arguments("https://youtu.be/abc", "/o", "/t", "/app/ffmpeg", null, false, null, null, null, null, null, 0));
    }

    [Fact]
    public void ThumbnailCommandTakesOnePicture()
    {
        var a = MediaLibrary.ThumbnailArguments("/v/clip.mp4", "/t/x.jpg");
        Assert.Equal("/t/x.jpg", a[^1]);
        Assert.Contains("-frames:v", a);
    }
}

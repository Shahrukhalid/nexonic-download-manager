using Nexonic.Core;
using Xunit;

/// <summary>Profile Batch: reading the engine's list, filters, duplicates, New Only, folders, export and the engine command.</summary>
public class ProfileBatchTests
{
    static BatchEntry E(string id, string title = "Clip", double? seconds = 120, string? day = "2026-11-03", string kind = "video", bool ok = true) =>
        new("https://v.test/watch?v=" + id, title, seconds, day == null ? null : DateTime.Parse(day), kind, ok, id);

    [Fact]
    public void ReadsTheEnginesLines()
    {
        var line = "{\"url\": \"https://v.test/a.mp4#__youtubedl_smuggle=x\", \"webpage_url\": \"https://v.test/a.mp4#__youtubedl_smuggle=x\", \"title\": \"Episode two\", \"timestamp\": 1793700000, \"playlist_title\": \"Test Channel\", \"duration\": 61}";
        var (e, owner) = ProfileBatch.ParseLine(line)!.Value;
        Assert.Equal("Test Channel", owner);
        Assert.Equal("Episode two", e.Title);
        Assert.Equal(61, e.Duration);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793700000).LocalDateTime, e.Date);
        Assert.Equal("https://v.test/a.mp4", ProfileBatch.Key(e.Url).TrimEnd('/'));
        var yt = ProfileBatch.ParseLine("{\"url\": \"abc\", \"id\": \"abc\", \"ie_key\": \"Youtube\", \"title\": \"[Private video]\", \"upload_date\": \"20261101\"}")!.Value.Entry;
        Assert.Equal("https://www.youtube.com/watch?v=abc", yt.Url);
        Assert.False(yt.Available);
        Assert.Equal(new DateTime(2026, 11, 1), yt.Date);
        Assert.Equal("shorts", ProfileBatch.ParseLine("{\"url\": \"https://www.youtube.com/shorts/x1\", \"title\": \"s\"}")!.Value.Entry.Kind);
        Assert.Equal("live", ProfileBatch.ParseLine("{\"url\": \"https://v.test/l\", \"live_status\": \"was_live\"}")!.Value.Entry.Kind);
        Assert.Null(ProfileBatch.ParseLine("WARNING: something"));
        Assert.Null(ProfileBatch.ParseLine("{\"title\": \"no address\"}"));
    }

    [Fact]
    public void ChoosesFiltersAndCountsHonestly()
    {
        var found = new[]
        {
            E("1", "Trip to the sea"), E("2", "Trip outtakes", 30), E("3", "Cooking", 900, "2026-10-01"), E("4", "Old", 200, "2025-01-01"),
            E("5", "Private", ok: false), E("6", "Have it"), E("1", "Trip to the sea"), E("7", "No date", 100, null), E("8", "Short one", 20, kind: "shorts"),
        };
        var filter = new BatchFilter { From = new DateTime(2026, 1, 1), MinSeconds = 60, Kind = "videos" };
        var (take, c) = ProfileBatch.Choose(found, filter, url => url.EndsWith("=6"));
        Assert.Equal(new[] { "1", "3", "7" }, take.Select(e => e.Id));  // an unknown date never excludes a video
        Assert.Equal(new BatchCounts(8, 3, 1, 3, 1), c);
        var words = ProfileBatch.Choose(found, new BatchFilter { Keywords = "trip !outtakes" }, _ => false).Take;
        Assert.Equal(new[] { "1" }, words.Select(e => e.Id));
        var newOnly = ProfileBatch.Choose(found, new BatchFilter(), _ => false, new HashSet<string> { ProfileBatch.Key(E("1").Url), ProfileBatch.Key(E("3").Url) }).Take;
        Assert.DoesNotContain(newOnly, e => e.Id is "1" or "3");
        Assert.Equal(2, ProfileBatch.Choose(found, new BatchFilter { Max = 2 }, _ => false).Take.Count);
    }

    [Fact]
    public void FoldersExportAndQualities()
    {
        Assert.Equal(Path.Combine("/d", "My Channel_ Clips"), ProfileBatch.FolderFor("/d", "My Channel: Clips", "https://v.test/c"));
        Assert.StartsWith(Path.Combine("/d", "v.test"), ProfileBatch.FolderFor("/d", "", "https://v.test/c/x"));
        var list = new[] { E("1", "Say \"hi\", world"), E("2", "Second", day: null, ok: false) };
        Assert.Equal("https://v.test/watch?v=1\nhttps://v.test/watch?v=2\n", ProfileBatch.Export(list, "txt"));
        var csv = ProfileBatch.Export(list, "csv").Split("\r\n");
        Assert.Equal("url,title,date,duration_seconds,type,accessible", csv[0]);
        Assert.Equal("https://v.test/watch?v=1,\"Say \"\"hi\"\", world\",2026-11-03,120,video,yes", csv[1]);
        Assert.Contains("\"accessible\": false", ProfileBatch.Export(list, "json"));
        Assert.Equal((2160, false), ProfileBatch.Quality("4k"));
        Assert.Equal((null, true), ProfileBatch.Quality("audio"));
        Assert.Equal((SiteVideo.OriginalQuality, false), ProfileBatch.Quality("original"));
        Assert.Contains("res,fps,hdr:12", SiteVideo.Format(false, SiteVideo.OriginalQuality));
        Assert.Contains("res:2160,fps,hdr:12,vcodec:avc,acodec:m4a", SiteVideo.Format(false, 2160));
    }

    [Fact]
    public void TheListingCommandUsesNoLoginAndContinues()
    {
        var a = ProfileBatch.Arguments("https://www.youtube.com/@someone", null, start: 41, max: 60);
        Assert.Contains("--flat-playlist", a);
        Assert.Equal("41", a[a.IndexOf("--playlist-start") + 1]);
        Assert.Equal("100", a[a.IndexOf("--playlist-end") + 1]);
        Assert.Equal("https://www.youtube.com/@someone/videos", a[^1]);
        Assert.DoesNotContain("--username", a);
        Assert.DoesNotContain("--cookies", a);
        Assert.DoesNotContain("--cookies-from-browser", a);
    }

    [Fact]
    public void SavedProfilesAreDueOnTheirSchedule()
    {
        var p = new SavedProfile { CheckHours = 6 };
        Assert.True(p.Due(DateTime.Now));
        p.LastChecked = DateTime.Now.AddHours(-2);
        Assert.False(p.Due(DateTime.Now));
        p.LastChecked = DateTime.Now.AddHours(-7);
        Assert.True(p.Due(DateTime.Now));
        Assert.False(new SavedProfile { CheckHours = 0 }.Due(DateTime.Now));
    }
}

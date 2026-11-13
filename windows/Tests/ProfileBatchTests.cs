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
    public void NexonicListersAreHandedToTheEngineAndTheirMessagesShownAsWritten()
    {
        var a = ProfileBatch.Arguments("https://www.instagram.com/someone/", null);
        var dir = a[a.IndexOf("--plugin-dirs") + 1];
        Assert.True(File.Exists(Path.Combine(dir, "nexonic", "yt_dlp_plugins", "extractor", "nexonic_profiles.py")));
        Assert.Equal("https://www.instagram.com/someone/", a[^1]);
        Assert.Equal("This Instagram profile is private. Only its approved followers can see its videos.",
            SiteVideo.Error("ERROR: [instagram:profile] someone: Nexonic: This Instagram profile is private. Only its approved followers can see its videos."));
        Assert.Equal("Instagram shows only the latest 12.", ProfileBatch.Note(
            "[instagram:profile] x: Reading\nWARNING: [instagram:profile] x: Nexonic: Instagram shows only the latest 12.\nWARNING: other"));
        var raw = "ERROR: [instagram:user] alexvkino: Unable to extract data; please report this issue on  https://github.com/yt-dlp/yt-dlp/issues?q= , "
            + "filling out the appropriate issue template. Confirm you are on the latest version using  yt-dlp -U";
        var shown = SiteVideo.Error(raw);
        Assert.False(shown.Contains("github"));
        Assert.False(shown.Contains("-U"));
        Assert.StartsWith("This site changed its pages", shown);
    }

    [Fact]
    public void InstagramProfilePagesAreReadLikeAVisitorSeesThem()
    {
        Assert.Equal("nasa", InstagramPage.User("https://www.instagram.com/nasa/"));
        Assert.Equal("natgeo", InstagramPage.User("https://instagram.com/natgeo/reels/"));
        Assert.Null(InstagramPage.User("https://www.instagram.com/p/Dfc1mYxFevS/"));
        Assert.Null(InstagramPage.User("https://www.youtube.com/@nasa"));
        var json = "{\"posts\":12,\"videos\":[{\"u\":\"https://www.instagram.com/reel/Dfc1mYxFevS/\",\"t\":\"Video by NASA on November 07, 2026. May be an image\"},"
            + "{\"u\":\"https://www.instagram.com/p/Dfdiuv0lI2K/\",\"t\":\"\"},{\"u\":\"https://evil.example/x\",\"t\":\"\"}]}";
        foreach (var handed in new[] { json, System.Text.Json.JsonSerializer.Serialize(json) })
        {
            var (posts, videos) = InstagramPage.Read(handed);
            Assert.Equal(12, posts);
            Assert.Equal(2, videos.Count);
            Assert.Equal(("shorts", "Dfc1mYxFevS", new DateTime(2026, 11, 7)), (videos[0].Kind, videos[0].Id, videos[0].Date!.Value));
            Assert.Equal("video", videos[1].Kind);
        }
        Assert.Equal(0, InstagramPage.Read("not json").Posts);
    }

    [Fact]
    public void ChosenQualitiesAreMp4AndCaptionSitesGetUniqueNames()
    {
        Assert.Equal("mp4", SiteVideo.Format(false, 1080)[^1]);
        Assert.Equal("mp4/mkv", SiteVideo.Format(false, SiteVideo.OriginalQuality)[^1]);
        Assert.Contains("%(title).120B [%(id)s].%(ext)s", SiteVideo.Naming("https://www.instagram.com/reel/Dfc1mYxFevS/"));
        Assert.Contains("--parse-metadata", SiteVideo.Naming("https://m.facebook.com/reel/1"));
        Assert.Equal(new[] { "-o", "%(title).150B.%(ext)s" }, SiteVideo.Naming("https://www.youtube.com/watch?v=x"));
        Assert.Equal(new[] { "-o", "%(title).150B.%(ext)s" }, SiteVideo.Naming("https://notinstagram.com/x"));
    }

    [Fact]
    public void InstagramAndFacebookProfilesAreRecognised()
    {
        var ig = SocialPages.Find("https://www.instagram.com/alexvkino/")!.Value;
        Assert.Equal(("Instagram", "alexvkino", "https://www.instagram.com/alexvkino/"), (ig.Site.Name, ig.Owner, ig.Pages[0]));
        var id = SocialPages.Find("https://www.facebook.com/profile.php?id=100066819891059&sk=reels_tab")!.Value;
        Assert.Equal(("Facebook", "100066819891059"), (id.Site.Name, id.Owner));
        Assert.Equal("https://www.facebook.com/profile.php?id=100066819891059&sk=reels_tab", id.Pages[0]);
        var name = SocialPages.Find("https://m.facebook.com/NASA/videos")!.Value;
        Assert.Equal(("NASA", "https://www.facebook.com/NASA/reels/"), (name.Owner, name.Pages[0]));
        Assert.Null(SocialPages.Find("https://www.facebook.com/watch/?v=123"));
        Assert.Null(SocialPages.Find("https://www.facebook.com/reel/123456789"));
        Assert.Null(SocialPages.Find("https://www.youtube.com/@nasa"));
        var found = new Dictionary<string, BatchEntry>();
        var page = new List<BatchEntry> { new("https://www.facebook.com/reel/1/", "a"), new("https://www.facebook.com/reel/2/", "b") };
        Assert.True(SocialPages.Merge(found, page, 10));
        Assert.False(SocialPages.Merge(found, page, 10));
        Assert.True(SocialPages.Merge(found, new List<BatchEntry> { new("https://www.facebook.com/watch/?v=3", "c") }, 10));
        Assert.Equal(3, found.Count);
        var (_, fb) = InstagramPage.Read("{\"posts\":1,\"videos\":[{\"u\":\"https://www.facebook.com/watch/?v=12345678\",\"t\":\"x\"}]}");
        Assert.Equal(("12345678", "video"), (fb[0].Id, fb[0].Kind));
    }

    [Fact]
    public void VisitorsAreToldHowToGetTheRestThroughTheirOwnBrowser()
    {
        var more = SocialPages.More(SocialPages.Instagram);
        Assert.Contains("signed in to Instagram", more);
        Assert.Contains("Download all", more);
        Assert.Contains("never sees your password", more);
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

    [Fact]
    public void ThePagesOwnPostsGoToTheRightEngine()
    {
        var e = ProfileBatch.PageEntries(new[] { "https://www.instagram.com/p/AbC123/", "https://www.instagram.com/p/AbC123/",
            "https://www.facebook.com/reel/42", "javascript:x" });
        Assert.Equal(2, e.Count);
        Assert.Equal("post", e[0].Kind);
        Assert.Equal("AbC123", e[0].Id);
        Assert.Equal("shorts", e[1].Kind);
        Assert.Equal("XyZ", ProfileBatch.PostCode("https://www.instagram.com/reel/XyZ/?igsh=1"));
        Assert.Null(ProfileBatch.PostCode("https://www.instagram.com/someone/"));
    }
}

/// <summary>The Profile Batch behaviour every app shares (tests/profile-batch-vectors.json).</summary>
public class ProfileBatchParityTests
{
    static System.Text.Json.JsonElement Vectors()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "tests", "profile-batch-vectors.json");
            if (File.Exists(f)) return System.Text.Json.JsonDocument.Parse(File.ReadAllText(f)).RootElement;
        }
        throw new FileNotFoundException("tests/profile-batch-vectors.json");
    }

    [Fact]
    public void KeysMatchEveryApp()
    {
        foreach (var pair in Vectors().GetProperty("keys").EnumerateArray())
            Assert.Equal(pair[1].GetString()!.TrimEnd('/'), ProfileBatch.Key(pair[0].GetString()!).TrimEnd('/'));
    }

    [Fact]
    public void DuplicatesMatchEveryApp()
    {
        var c = Vectors().GetProperty("choose");
        var found = c.GetProperty("found").EnumerateArray().Select(u => new BatchEntry(u.GetString()!, "Clip")).ToList();
        var done = c.GetProperty("downloaded").EnumerateArray().Select(u => ProfileBatch.Key(u.GetString()!)).ToHashSet();
        var (take, counts) = ProfileBatch.Choose(found, new BatchFilter { Max = 100 }, u => done.Contains(ProfileBatch.Key(u)));
        Assert.Equal(c.GetProperty("discovered").GetInt32(), counts.Discovered);
        Assert.Equal(c.GetProperty("queued").GetInt32(), counts.Queued);
        Assert.Equal(c.GetProperty("duplicates").GetInt32(), counts.Duplicates);
        Assert.Equal(take.Count, counts.Queued);
    }
}

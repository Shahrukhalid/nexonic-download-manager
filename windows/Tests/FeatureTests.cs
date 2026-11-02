using Nexonic.Core;
using Xunit;

public class FeatureTests
{
    [Fact]
    public void BatchNumbersKeepTheirWidth()
    {
        var urls = BatchPattern.Expand("https://e.com/file[001-100].jpg");
        Assert.Equal(100, urls.Count);
        Assert.Equal("https://e.com/file001.jpg", urls[0]);
        Assert.Equal("https://e.com/file100.jpg", urls[^1]);
        Assert.Equal(new[] { "a1", "a2", "a3" }, BatchPattern.Expand("a[1-3]"));
        Assert.Equal(new[] { "p0", "p10", "p20" }, BatchPattern.Expand("p[0-20:10]"));
    }

    [Fact]
    public void BatchLettersAndSeveralRanges()
    {
        Assert.Equal(new[] { "a.png", "b.png", "c.png" }, BatchPattern.Expand("[a-c].png"));
        Assert.Equal(new[] { "s1e1", "s1e2", "s2e1", "s2e2" }, BatchPattern.Expand("s[1-2]e[1-2]"));
        Assert.Equal(new[] { "http://[::1]/x" }, BatchPattern.Expand("http://[::1]/x"));
        Assert.Throws<ArgumentException>(() => BatchPattern.Expand("[1-200][1-200]"));
    }

    [Fact]
    public void Checksums()
    {
        var abc = "abc"u8.ToArray();
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", Checksum.Compute(ChecksumAlgorithm.Md5, abc));
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", Checksum.Compute(ChecksumAlgorithm.Sha1, abc));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Checksum.Compute(ChecksumAlgorithm.Sha256, abc));
        Assert.Equal(ChecksumAlgorithm.Sha256, Checksum.Parse("SHA-256: BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")?.Algorithm);
        Assert.Null(Checksum.Parse("hello"));
        var file = Path.GetTempFileName();
        File.WriteAllBytes(file, abc);
        Assert.Equal("MD5 matches", Checksum.Verify("900150983cd24fb0d6963f7d28e17f72", file));
        File.Delete(file);
    }

    [Fact]
    public void LinksAndScope()
    {
        var b = new Uri("https://e.com/files/index.html");
        var html = "<a href=\"a.zip\">a</a> <a href='/files/sub/'>s</a> <img src=pic.jpg> <a href=\"#top\">t</a> <a href=\"mailto:x@e.com\">m</a> <a href=\"a.zip#x\">dup</a>";
        Assert.Equal(new[] { "https://e.com/files/a.zip", "https://e.com/files/sub/", "https://e.com/files/pic.jpg" },
            LinkExtractor.Links(html, b).Select(u => u.AbsoluteUri));
        var o = new GrabOptions { Start = b, Depth = 2, Types = new() { "zip" } };
        Assert.True(SiteGrabber.InScope(new Uri("https://e.com/files/sub/x.html"), o));
        Assert.False(SiteGrabber.InScope(new Uri("https://e.com/other/x.html"), o));
        Assert.False(SiteGrabber.InScope(new Uri("https://other.com/files/x.html"), o));
        Assert.True(SiteGrabber.Wanted(new Uri("https://e.com/files/a.ZIP"), o));
        Assert.False(SiteGrabber.Wanted(new Uri("https://e.com/files/pic.jpg"), o));
    }

    [Fact]
    public void QueueTimetable()
    {
        var monday2am = new DateTime(2026, 11, 2, 2, 0, 0);
        Assert.Equal(1, QueueClock.Weekday(monday2am));
        var q = new DownloadQueue { Name = "Night" };
        Assert.False(QueueClock.ShouldStart(q, monday2am));
        q.StartEnabled = true;
        Assert.True(QueueClock.ShouldStart(q, monday2am));
        q.Days = new() { 6, 7 };
        Assert.False(QueueClock.ShouldStart(q, monday2am));
        q.Days = new() { 1 };
        q.LastStartDay = QueueClock.DayKey(monday2am);
        Assert.False(QueueClock.ShouldStart(q, monday2am));
        q.Running = true;
        q.StopEnabled = true;
        q.StopTime = "02:00";
        Assert.True(QueueClock.ShouldStop(q, monday2am));
    }

    [Fact]
    public void ClipboardLinks()
    {
        var types = FileTypes.Parse(".zip, mp4 PDF");
        Assert.Equal("https://e.com/a.zip", FileTypes.DownloadLink(" https://e.com/a.zip \n", types)?.AbsoluteUri);
        Assert.Null(FileTypes.DownloadLink("https://e.com/page.html", types));
        Assert.Null(FileTypes.DownloadLink("see https://e.com/a.zip", types));
        Assert.NotNull(FileTypes.DownloadLink("https://e.com/doc.PDF?x=1", types));
    }

    [Fact]
    public void CommandLine()
    {
        var r = CommandLineTool.Parse(new[] { "https://e.com/a.zip", "--queue", "Night", "--later", "--limit", "200" });
        Assert.Null(r.Error);
        Assert.Single(r.Jobs);
        Assert.Equal("Night", r.Jobs[0].Queue);
        Assert.False(r.Jobs[0].Start);
        Assert.Equal(200, r.Jobs[0].SpeedLimitKBps);
        Assert.Equal(5, CommandLineTool.Parse(new[] { "batch", "https://e.com/f[1-5].jpg" }).Jobs.Count);
        Assert.True(CommandLineTool.Parse(new[] { "help" }).Help);
        Assert.NotNull(CommandLineTool.Parse(new[] { "https://e.com/a", "https://e.com/b", "--name", "x" }).Error);
        Assert.NotNull(CommandLineTool.Parse(new[] { "https://e.com/a", "--checksum", "nope" }).Error);
        Assert.True(CommandLineTool.IsCommand(new[] { "https://e.com/a" }));
        Assert.False(CommandLineTool.IsCommand(new[] { "chrome-extension://abc/" }));
        Assert.False(CommandLineTool.IsCommand(Array.Empty<string>()));
        Assert.Equal(new[] { @"C:\Scan Tool\scan.exe", "/quiet", "-x" }, CommandLineTool.Words("\"C:\\Scan Tool\\scan.exe\" /quiet  -x"));
    }

    [Fact]
    public void OldSettingsAndJobsStillLoad()
    {
        var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"Connections\":4,\"SortIntoCategoryFolders\":true}")!;
        Assert.Equal(4, s.Connections);
        Assert.True(s.MonitorClipboard);
        Assert.Equal(ProxyMode.System, s.Proxy.Mode);
        var j = System.Text.Json.JsonSerializer.Deserialize<DownloadJob>("{\"Url\":\"https://e.com/a\",\"Queue\":\"Night\",\"Start\":false}")!;
        Assert.Equal("Night", j.Queue);
        Assert.False(j.Start);
    }
}

public class HLSTests
{
    [Fact]
    public void MasterPicksBestQualityAndSound()
    {
        var text = "#EXTM3U\n" +
                   "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aud\",NAME=\"English\",DEFAULT=YES,URI=\"audio/en.m3u8\"\n" +
                   "#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,CODECS=\"avc1.4d401e,mp4a.40.2\",AUDIO=\"aud\"\nlow/index.m3u8\n" +
                   "#EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080,AUDIO=\"aud\"\nhd/index.m3u8\n" +
                   "#EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720,AUDIO=\"aud\"\nmid/index.m3u8\n";
        var p = HLS.Parse(text, new Uri("https://e.com/v/master.m3u8"));
        Assert.True(p.IsMaster);
        Assert.Equal(3, p.Variants.Count);
        Assert.Equal("aud", p.Variants[0].AudioGroup);
        Assert.Equal("https://e.com/v/hd/index.m3u8", HLS.Best(p.Variants)!.Url.AbsoluteUri);
        Assert.Equal("https://e.com/v/audio/en.m3u8", p.Audio["aud"].AbsoluteUri);
    }

    [Fact]
    public void MediaPlaylist()
    {
        var text = "#EXTM3U\r\n#EXT-X-TARGETDURATION:6\r\n#EXT-X-MAP:URI=\"init.mp4\"\r\n#EXTINF:6.0,\r\nseg1.m4s\r\n#EXTINF:6.0,\r\nhttps://cdn.e.com/seg2.m4s\r\n#EXT-X-ENDLIST\r\n";
        var m = HLS.Parse(text, new Uri("https://e.com/v/hd/index.m3u8")).Media!;
        Assert.Equal(new[] { "https://e.com/v/hd/seg1.m4s", "https://cdn.e.com/seg2.m4s" }, m.Segments.Select(u => u.AbsoluteUri));
        Assert.Equal("https://e.com/v/hd/init.mp4", m.InitSegment!.AbsoluteUri);
        Assert.True(m.IsFragmentedMp4);
        Assert.Equal("NONE", m.Encryption);
    }

    [Fact]
    public void ProtectedAndInvalid()
    {
        var m = HLS.Parse("#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"skd://x\"\n#EXTINF:6,\na.ts\n", new Uri("https://e.com/i.m3u8")).Media!;
        Assert.Equal("SAMPLE-AES", m.Encryption);
        Assert.Throws<HLS.Failure>(() => HLS.Parse("<html>", new Uri("https://e.com/")));
        Assert.True(HLS.IsPlaylistUrl("https://e.com/live/index.M3U8?token=1"));
    }
}

public class SiteVideoTests
{
    [Fact]
    public void SupportedSites()
    {
        Assert.True(SiteVideo.IsSupported("https://www.youtube.com/watch?v=abc"));
        Assert.True(SiteVideo.IsSupported("https://youtu.be/abc"));
        Assert.True(SiteVideo.IsSupported("https://m.facebook.com/watch/?v=1"));
        Assert.True(SiteVideo.IsSupported("https://www.instagram.com/reel/xyz/"));
        Assert.False(SiteVideo.IsSupported("https://notyoutube.com/x"));
        Assert.False(SiteVideo.IsSupported("https://example.com/a.mp4"));
    }

    [Fact]
    public void ProgressAndResult()
    {
        Assert.Equal(new SiteVideo.Progress(1024, 2048, 512.5), SiteVideo.ParseProgress("NDMP 1024 2048 NA 512.5"));
        Assert.Equal(900L, SiteVideo.ParseProgress("NDMP 10 NA 900.0 NA")!.Total);
        Assert.Null(SiteVideo.ParseProgress("[download] 5%"));
        Assert.Equal(@"C:\Users\me\Downloads\Talk [abc].mp4", SiteVideo.SavedFile(@"NDMF C:\Users\me\Downloads\Talk [abc].mp4"));
        Assert.Equal("Video unavailable", SiteVideo.Error("WARNING: x\nERROR: [youtube] abc: Video unavailable"));
        Assert.Contains("copy-protected", SiteVideo.Error("ERROR: This video is DRM protected"));
        Assert.True(SiteVideo.Error("ERROR: [vimeo] 1111417978: The web client only works when logged-in. Use --cookies, --cookies-from-browser, --username and --password, --netrc-cmd, or --netrc (vimeo) to provide account credentials.").StartsWith("The site only lets signed-in users"));
    }

    [Fact]
    public void Arguments()
    {
        var a = SiteVideo.Arguments("https://youtu.be/abc", "/out", "/tmp/x", "/app/ffmpeg", "/app/qjs", false, null, "a=1", null, null, null, 500);
        Assert.Equal("https://youtu.be/abc", a[^1]);
        Assert.Contains("bv*+ba/b", a);
        Assert.Contains("--progress", a);
        Assert.Contains("quickjs:/app/qjs", a);
        Assert.Contains("500K", a);
        Assert.Contains("Cookie:a=1", a);
        Assert.Contains("ba[ext=m4a]/ba/b", SiteVideo.Arguments("https://youtu.be/abc", "/o", "/t", null, null, true, null, null, null, null, null, 0));
    }

    [Fact]
    public void VideoInfoAndReuse()
    {
        const string json = "{\"title\":\"Koi Song\",\"ext\":\"mp4\",\"requested_formats\":[{\"filesize\":1000},{\"filesize_approx\":234}]}";
        Assert.Equal(new SiteVideo.Info("Koi Song", "mp4", 1234), SiteVideo.ParseInfo(json));
        Assert.Null(SiteVideo.ParseInfo("{}"));
        Assert.Contains("-J", SiteVideo.InfoArguments("https://youtu.be/abc", null, false, null, null, null, null, null));
        var file = Path.Combine(Path.GetTempPath(), "ndm-info-test.json");
        File.WriteAllText(file, json);
        try
        {
            var a = SiteVideo.Arguments("https://youtu.be/abc", "/o", "/t", null, null, false, null, null, null, null, null, 0, file);
            Assert.Equal(new[] { "--load-info-json", file }, a.TakeLast(2).ToArray());
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void VideoPiecesAddUpWithoutInflating()
    {
        var parts = new SiteVideo.PartProgress();
        parts.Add(new SiteVideo.Progress(100, 1000, null, "137"));
        parts.Add(new SiteVideo.Progress(80, 1000, null, "137"));   // fragments report out of order
        parts.Add(new SiteVideo.Progress(1000, 1000, null, "137"));
        var r = parts.Add(new SiteVideo.Progress(50, 200, null, "140"));
        Assert.Equal(1050, r.Done);
        Assert.Equal(1200, r.Total);
        Assert.Equal("137", SiteVideo.ParseProgress("NDMP 10 20 NA 1.5 137")?.Part);
    }

    [Fact]
    public void VideoPiecesStepsAndName()
    {
        var parts = new SiteVideo.PartProgress();
        parts.Add(SiteVideo.ParseProgress("NDMP 300 1000 NA 5.0 137 1080 avc1.640028")!);
        parts.Add(SiteVideo.ParseProgress("NDMP 20 40 NA 5.0 140 NA none")!);
        Assert.Equal(new[] { "Picture 1080p", "Sound" }, parts.Pieces.Select(p => p.Name).ToArray());
        Assert.Equal(300, parts.Pieces[0].Done);
        Assert.Equal("Joining picture and sound…", SiteVideo.Step("[Merger] Merging formats into \"x.mp4\""));
        Assert.Null(SiteVideo.Step("[download] 50%"));
        Assert.Equal("My Song.mp4", SiteVideo.DestinationName("[download] Destination: /t/My Song.f137.mp4"));
    }

    [Fact]
    public void UpdateVersionsAndNotes()
    {
        Assert.True(Updates.IsNewer("0.6.0", "0.5.9"));
        Assert.True(Updates.IsNewer("0.10.0", "0.9.1"));
        Assert.False(Updates.IsNewer("0.5.9", "0.5.9"));
        Assert.False(Updates.IsNewer("0.5.8", "0.5.9"));
        var notes = Updates.Notes("## 0.6.0\n\n- **New:** updates\n\n## 0.5.9\n\n- old", "0.5.9");
        Assert.Contains("Version 0.6.0", notes);
        Assert.Contains("New: updates", notes);
        Assert.DoesNotContain("old", notes);
        var r = Updates.Parse("{\"tag_name\":\"v0.6.0\",\"html_url\":\"https://x/r\",\"body\":\"b\",\"assets\":[{\"name\":\"a.exe\",\"browser_download_url\":\"https://x/a.exe\"}]}");
        Assert.Equal("0.6.0", r!.Version);
        Assert.Equal("https://x/a.exe", r.Assets["a.exe"]);
    }
}

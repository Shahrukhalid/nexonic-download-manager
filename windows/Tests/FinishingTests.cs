using Nexonic.Core;
using Xunit;

/// <summary>Options → After Download extras: rename patterns, phone notices, play while downloading, video tools.</summary>
public class FinishingTests
{
    [Fact]
    public void RenamesByPattern()
    {
        var d = new DateTime(2026, 11, 14, 9, 5, 0);
        Assert.Equal("example.com 2026-11-14 report.pdf", RenameMask.Apply("{site} {date} {name}", "report.pdf", "https://www.example.com/f/report.pdf", d, "Documents"));
        Assert.Equal("Compressed-a.zip", RenameMask.Apply("{category}-{name}.{ext}", "a.zip", "https://e.test/a.zip", d, "Compressed"));
        Assert.Equal("a-b-x.mp4", RenameMask.Apply("a/b:{name}", "x.mp4", "https://e.test/x", d, ""));
        Assert.Null(RenameMask.Apply("", "x.mp4", "https://e.test/x", d, ""));
        Assert.Null(RenameMask.Apply("{name}", "x.mp4", "https://e.test/x", d, ""));
    }

    [Fact]
    public void RenamesTheFinishedFile()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "clip.mp4");
        File.WriteAllText(path, "x");
        var r = new DownloadRecord { Url = "https://www.example.com/clip.mp4", FileName = "clip.mp4", SavedPath = path, Finished = new DateTime(2026, 1, 2) };
        Assert.True(RenameMask.RenameFinished(r, "{site}-{name}"));
        Assert.Equal("example.com-clip.mp4", r.FileName);
        Assert.True(File.Exists(Path.Combine(dir, "example.com-clip.mp4")));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void NoticesGoOnlyToSafeAddresses()
    {
        Assert.NotNull(FinishNotice.Request("https://push.example/topic", "a.zip", true));
        Assert.NotNull(FinishNotice.Request("http://192.168.1.5:8080/hook", "a.zip", true));
        Assert.Null(FinishNotice.Request("http://push.example/topic", "a.zip", true));
        Assert.Null(FinishNotice.Request("file:///etc/passwd", "a.zip", true));
        Assert.Equal("Failed: a.zip", FinishNotice.Request("https://push.example/t", "a.zip", false)!.Content!.ReadAsStringAsync().Result);
    }

    [Fact]
    public void PlaysOnlyWhatIsThereFromTheStart()
    {
        Assert.Equal(150, PartialPlay.Prefix(new[] { new Segment { Id = 1, Start = 100, End = 199, Written = 50 }, new Segment { Id = 0, Start = 0, End = 99, Written = 100 } }));
        Assert.Equal(10, PartialPlay.Prefix(new[] { new Segment { Id = 0, Start = 0, End = 99, Written = 10 }, new Segment { Id = 1, Start = 100, End = 199, Written = 100 } }));
        Assert.True(PartialPlay.IsMedia("a.MKV"));
        Assert.False(PartialPlay.IsMedia("a.zip"));
    }

    [Fact]
    public void VideoTools()
    {
        Assert.Equal("m4a", MediaTools.AudioExtension("aac"));
        Assert.Contains("copy", MediaTools.RemuxArguments("in.mkv", "out.mp4"));
        Assert.Equal(new[] { "--sponsorblock-remove", "sponsor" }, new FinishOptions { SkipSponsors = true }.VideoArguments());
    }
}

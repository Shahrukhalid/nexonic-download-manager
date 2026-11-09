using Nexonic.Core;
using Xunit;

/// <summary>Link Grabber packages, unpacking when a package is done, and wake times for queues.</summary>
public class LinkGrabberTests
{
    [Fact]
    public void FindsLinksInTextAndPages()
    {
        var text = "see https://a.test/one.zip, and <a href=\"/files/two.part1.rar\">x</a> <a href='page.html'>p</a> magnet:?xt=urn:btih:abc https://a.test/one.zip";
        Assert.Equal(new[] { "https://a.test/one.zip", "magnet:?xt=urn:btih:abc" }, LinkGrabber.Find(text));
        var page = new Uri("https://a.test/list/");
        Assert.Equal(new[] { "https://a.test/files/two.part1.rar", "https://a.test/one.zip", "magnet:?xt=urn:btih:abc" }, LinkGrabber.Find(text, page));
    }

    [Fact]
    public void GroupsSplitArchivesIntoPackages()
    {
        var p = LinkGrabber.Group(new[]
        {
            "https://h.test/Movie.part1.rar", "https://h.test/song.mp3", "https://h.test/Movie.part2.rar",
            "https://h.test/Data.7z.001", "https://h.test/Data.7z.002", "https://h.test/Tools.zip",
        });
        Assert.Equal(new[] { "Movie", "Data", "Tools", LinkGrabber.OtherFiles }, p.Select(x => x.Name));
        Assert.Equal(2, p[0].Links.Count);
        Assert.Equal(new[] { "https://h.test/song.mp3" }, p[3].Links);
        Assert.Equal("Movie", LinkGrabber.SetName("Movie.r00"));
        Assert.Null(LinkGrabber.SetName("video.mp4"));
    }

    [Fact]
    public void RecognisesPageAddresses()
    {
        Assert.NotNull(LinkGrabber.PageAddress("https://a.test/downloads/"));
        Assert.NotNull(LinkGrabber.PageAddress("https://a.test/index.php"));
        Assert.Null(LinkGrabber.PageAddress("https://a.test/file.zip"));
        Assert.Null(LinkGrabber.PageAddress("https://a.test/ https://b.test/"));
    }

    [Fact]
    public void UnpacksAPackageOnlyWhenAllOfItIsDone()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            DownloadRecord Rec(string name, DownloadStatus s)
            {
                var path = Path.Combine(dir, name);
                File.WriteAllText(path, "x");
                return new DownloadRecord { FileName = name, Folder = dir, Status = s, SavedPath = path, Package = "Movie", UnpackWhenDone = true };
            }
            var one = Rec("Movie.part1.rar", DownloadStatus.Finished);
            var two = Rec("Movie.part2.rar", DownloadStatus.Downloading);
            Assert.Empty(LinkGrabber.ToUnpack(one, new[] { one, two }, unpackAll: false));
            two.Status = DownloadStatus.Finished;
            Assert.Equal(new[] { one.SavedPath }, LinkGrabber.ToUnpack(two, new[] { one, two }, unpackAll: false));

            // Without a package: split sets wait for every part when "unpack finished archives" is on.
            one.Package = two.Package = null;
            two.Status = DownloadStatus.Paused;
            Assert.Empty(LinkGrabber.ToUnpack(one, new[] { one, two }, unpackAll: true));
            two.Status = DownloadStatus.Finished;
            Assert.Equal(new[] { one.SavedPath }, LinkGrabber.ToUnpack(two, new[] { one, two }, unpackAll: true));
            Assert.Empty(LinkGrabber.ToUnpack(two, new[] { one, two }, unpackAll: false));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NextQueueStartFollowsTheTimetable()
    {
        var q = new DownloadQueue { StartEnabled = true, StartTime = "02:00", Days = new() { 1, 3 } }; // Monday, Wednesday
        var monday = new DateTime(2026, 11, 9, 10, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 11, 2, 0, 0), QueueClock.NextStart(q, monday));
        Assert.Equal(new DateTime(2026, 11, 9, 2, 0, 0), QueueClock.NextStart(q, monday.Date));
        q.StartEnabled = false;
        Assert.Null(QueueClock.NextStart(q, monday));
    }
}

using Nexonic.Core;
using Xunit;

/// <summary>Units, dates, file types, batch lists and filters, sound conversion, missing files and moving the data folder.</summary>
public class DisplayAndDataTests
{
    [Fact]
    public void SizesAndSpeedsFollowTheChosenUnits()
    {
        Assert.Equal("1.5 MB", Units.Size(1_572_864, "binary"));
        Assert.Equal("1.5 MiB", Units.Size(1_572_864, "iec"));
        Assert.Equal("1.6 MB", Units.Size(1_572_864, "decimal"));
        Assert.Equal("999 B", Units.Size(999, "decimal"));
        Assert.Equal("0 B", Units.Size(-5, "binary"));
        Assert.Equal("2 MB/s", Units.Speed(2 * 1024 * 1024, "bytes", "binary"));
        Assert.Equal("16.8 Mbit/s", Units.Speed(2 * 1024 * 1024, "bits", "binary"));
        Assert.Equal("800 kbit/s", Units.Speed(100_000, "bits", "decimal"));
        var s = new AppSettings { SizeUnits = "nonsense", SpeedUnits = "bits", DateStyle = "relative" };
        try
        {
            Units.Apply(s);
            Assert.Equal("binary", Units.SizeStyle);
            Assert.Equal("bits", Units.SpeedStyle);
        }
        finally { Units.Apply(new AppSettings()); }
        Assert.Equal("absolute", Units.DateStyle);
    }

    [Fact]
    public void DatesCanBeRelative()
    {
        var now = new DateTime(2026, 11, 11, 15, 0, 0);
        Assert.Equal("Just now", Units.Date(now.AddSeconds(-20), "relative", now));
        Assert.Equal("5 min ago", Units.Date(now.AddMinutes(-5), "relative", now));
        Assert.Equal("3 h ago", Units.Date(now.AddHours(-3), "relative", now));
        Assert.StartsWith("Yesterday ", Units.Date(now.AddDays(-1), "relative", now));
        Assert.Equal("4 days ago", Units.Date(now.AddDays(-4), "relative", now));
        var old = now.AddDays(-30);
        Assert.Equal(old.ToString("g", System.Globalization.CultureInfo.CurrentCulture), Units.Date(old, "relative", now));
        Assert.Equal(now.ToString("g", System.Globalization.CultureInfo.CurrentCulture), Units.Date(now, "absolute", now));
        Assert.Equal("", Units.Date(null, "relative", now));
        Assert.NotEqual("Just now", Units.Date(now.AddHours(2), "relative", now)); // a clock that moved back shows the date
    }

    [Fact]
    public void MoreFileTypesAreRecognised()
    {
        Assert.Equal(FileCategory.Models, FileCategories.Of("llama-3-8b.Q4_K_M.gguf"));
        Assert.Equal(FileCategory.Models, FileCategories.Of("model.safetensors"));
        Assert.Equal(FileCategory.Images, FileCategories.Of("IMG_0001.CR3"));
        Assert.Equal(FileCategory.Images, FileCategories.Of("photo.dng"));
        Assert.Equal(FileCategory.Documents, FileCategories.Of("bracket.dwg"));
        Assert.Equal(FileCategory.Documents, FileCategories.Of("part.step"));
        Assert.Equal(FileCategory.Compressed, FileCategories.Of("backup.lz4"));
        Assert.Equal(FileCategory.Programs, FileCategories.Of("tool.AppImage"));
        Assert.Equal(FileCategory.Video, FileCategories.Of("clip.m2ts"));
        Assert.Equal("Models", FileCategories.Folder(FileCategory.Models));
        Assert.Equal(FileCategory.Other, FileCategories.Of("readme"));
        // Saved numbers keep their meaning: Other is still 6.
        Assert.Equal(6, (int)FileCategory.Other);
    }

    [Fact]
    public void BatchListsAndFilters()
    {
        var links = BatchPattern.Expand("https://x.test/p[1-2].{jpg, png}");
        Assert.Equal(new[] { "https://x.test/p1.jpg", "https://x.test/p1.png", "https://x.test/p2.jpg", "https://x.test/p2.png" }, links);
        Assert.Equal(1, BatchPattern.Count("https://x.test/{only}.jpg")); // a brace without a comma is not a list
        var all = new[] { "https://x.test/a.jpg?s=1", "https://x.test/a-thumb.jpg", "https://x.test/b.PNG", "https://x.test/c.zip" };
        Assert.Equal(new[] { "https://x.test/a.jpg?s=1" }, LinkFilter.Apply(all, "*.jpg !*thumb*"));
        Assert.Equal(new[] { "https://x.test/a.jpg?s=1", "https://x.test/a-thumb.jpg", "https://x.test/b.PNG" }, LinkFilter.Apply(all, "*.jpg *.png"));
        Assert.Equal(new[] { "https://x.test/c.zip" }, LinkFilter.Apply(all, "zip"));
        Assert.Equal(all, LinkFilter.Apply(all, "  "));
        Assert.Equal(new[] { "https://x.test/b.PNG" }, LinkFilter.Apply(all, "?.png"));
    }

    [Fact]
    public void SoundConversionArguments()
    {
        Assert.Empty(AudioConversion.Arguments("original"));
        Assert.Equal(new[] { "-x", "--audio-format", "mp3", "--audio-quality", "0" }, AudioConversion.Arguments("mp3"));
        Assert.Equal(new[] { "-x", "--audio-format", "opus" }, AudioConversion.Arguments("opus"));
        Assert.Equal(new[] { "-x", "--audio-format", "flac" }, AudioConversion.Arguments("flac"));
        Assert.Empty(AudioConversion.Arguments("wav; rm -rf"));
        try
        {
            AudioConversion.Format = "flac";
            var with = SiteVideo.Arguments("https://v.test/1", "/d", "/t", "/bin/ffmpeg", null, true, null, null, null, null, null, 0);
            Assert.Contains("flac", with);
            var video = SiteVideo.Arguments("https://v.test/1", "/d", "/t", "/bin/ffmpeg", null, false, null, null, null, null, null, 0);
            Assert.DoesNotContain("-x", video);
            var noFfmpeg = SiteVideo.Arguments("https://v.test/1", "/d", "/t", null, null, true, null, null, null, null, null, 0);
            Assert.DoesNotContain("-x", noFfmpeg);
        }
        finally { AudioConversion.Format = "original"; }
    }

    [Fact]
    public void MissingFilesAreFound()
    {
        var dir = Directory.CreateTempSubdirectory("ndm-missing-").FullName;
        try
        {
            var here = Path.Combine(dir, "here.bin");
            File.WriteAllText(here, "x");
            var kept = new DownloadRecord { Status = DownloadStatus.Finished, SavedPath = here };
            var folder = new DownloadRecord { Status = DownloadStatus.Finished, SavedPath = dir };
            var gone = new DownloadRecord { Status = DownloadStatus.Finished, SavedPath = Path.Combine(dir, "gone.bin") };
            var unfinished = new DownloadRecord { Status = DownloadStatus.Paused, SavedPath = Path.Combine(dir, "gone.bin") };
            var noPath = new DownloadRecord { Status = DownloadStatus.Finished };
            Assert.Equal(new[] { gone }, MissingFiles.Find(new[] { kept, folder, gone, unfinished, noPath }));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DataFolderMovesSafelyAndBack()
    {
        var root = Directory.CreateTempSubdirectory("ndm-data-").FullName;
        try
        {
            var usual = Path.Combine(root, "usual");
            var other = Path.Combine(root, "big drive", "NDM data");
            Directory.CreateDirectory(Path.Combine(usual, "Parts", "abc"));
            File.WriteAllText(Path.Combine(usual, "settings.json"), "{\"a\":1}");
            File.WriteAllBytes(Path.Combine(usual, "Parts", "abc", "0.part"), new byte[100_000]);
            DataLocation.Forget();
            Assert.Equal(usual, DataLocation.Current(usual));
            Assert.NotNull(DataLocation.Request(usual, "relative/path"));
            Assert.NotNull(DataLocation.Request(usual, Path.Combine(usual, "inside")));
            Directory.CreateDirectory(Path.Combine(root, "full"));
            File.WriteAllText(Path.Combine(root, "full", "x"), "");
            Assert.NotNull(DataLocation.Request(usual, Path.Combine(root, "full")));
            Assert.Null(DataLocation.ApplyPending(usual)); // nothing asked

            Assert.Null(DataLocation.Request(usual, other));
            Assert.Equal(usual, DataLocation.Current(usual)); // nothing moves before the next start
            Assert.Null(DataLocation.ApplyPending(usual));
            Assert.Equal(other, DataLocation.Current(usual));
            Assert.Equal("{\"a\":1}", File.ReadAllText(Path.Combine(other, "settings.json")));
            Assert.Equal(100_000, new FileInfo(Path.Combine(other, "Parts", "abc", "0.part")).Length);
            Assert.False(File.Exists(Path.Combine(usual, "settings.json")));
            Assert.True(File.Exists(Path.Combine(usual, DataLocation.PointerName)));

            // Back to the usual place: the pointer goes away.
            Assert.Null(DataLocation.Request(usual, usual));
            Assert.Null(DataLocation.ApplyPending(usual));
            Assert.Equal(usual, DataLocation.Current(usual));
            Assert.False(File.Exists(Path.Combine(usual, DataLocation.PointerName)));
            Assert.True(File.Exists(Path.Combine(usual, "Parts", "abc", "0.part")));
            Assert.False(Directory.Exists(other));
        }
        finally { DataLocation.Forget(); Directory.Delete(root, true); }
    }
}

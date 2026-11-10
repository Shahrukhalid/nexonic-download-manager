using Nexonic.Core;
using Xunit;

/// <summary>cURL import (browser "Copy as cURL" for bash and Windows) and export; the 128-connection limit.</summary>
public class CurlCommandTests
{
    [Fact]
    public void ReadsABrowserBashCommand()
    {
        const string text = "curl 'https://cdn.test/files/a%20b.zip?sig=1' \\\n  -H 'accept: */*' \\\n  -H 'referer: https://site.test/page' \\\n" +
                            "  -H $'cookie: sid=1; theme=it\\'s' \\\n  -H 'user-agent: Agent/1' \\\n  -H 'x-token: abc' \\\n  --compressed";
        var (r, problem) = CurlCommand.Parse(text);
        Assert.Null(problem);
        Assert.Equal("https://cdn.test/files/a%20b.zip?sig=1", r!.Url);
        Assert.Equal("https://site.test/page", r.Referrer);
        Assert.Equal("sid=1; theme=it's", r.Cookies);
        Assert.Equal("Agent/1", r.UserAgent);
        Assert.Equal(1, r.Ignored.Count);
        Assert.Equal("x-token", r.Ignored[0]);
    }

    [Fact]
    public void ReadsAWindowsCmdCommandAndBasicLogin()
    {
        const string text = "curl ^\"https://h.test/f.iso^\" ^\r\n  -H ^\"Authorization: Basic dXNlcjpwYXNzOndvcmQ=^\" ^\r\n  -b ^\"a=1^\"";
        var (r, problem) = CurlCommand.Parse(text);
        Assert.Null(problem);
        Assert.Equal("https://h.test/f.iso", r!.Url);
        Assert.Equal(("user", "pass:word"), (r.Username, r.Password));
        Assert.Equal("a=1", r.Cookies);
    }

    [Fact]
    public void RefusesWhatADownloadCannotRepeat()
    {
        Assert.Contains("POST", CurlCommand.Parse("curl -X POST https://a.test/x").Problem);
        Assert.Contains("sends data", CurlCommand.Parse("curl https://a.test/x --data-raw 'a=1'").Problem);
        Assert.Contains("not closed", CurlCommand.Parse("curl 'https://a.test/x").Problem);
        Assert.Contains("no web address", CurlCommand.Parse("curl file:///etc/passwd").Problem);
        Assert.Contains("not a cURL", CurlCommand.Parse("wget https://a.test/x").Problem);
        Assert.True(CurlCommand.Looks("  curl https://a.test"));
        Assert.False(CurlCommand.Looks("https://a.test/curl x"));
    }

    [Fact]
    public void ExportsACommandThatReadsBackTheSame()
    {
        var rec = new DownloadRecord
        {
            Url = "https://h.test/it's.zip", FileName = "it's.zip", Referrer = "https://h.test/", Cookies = "a=1; b=2",
            UserAgent = "Agent/1", Username = "me", Password = "p",
        };
        var cmd = CurlCommand.ToCommand(rec);
        Assert.StartsWith("curl -L -C - -o 'it'\\''s.zip'", cmd);
        var (r, problem) = CurlCommand.Parse(cmd);
        Assert.Null(problem);
        Assert.Equal((rec.Url, rec.Referrer, rec.Cookies, rec.UserAgent, rec.Username, rec.Password),
                     (r!.Url, r.Referrer, r.Cookies, r.UserAgent, r.Username, r.Password));
    }

    [Fact]
    public void UpTo256Connections()
    {
        Assert.Equal(256, Limits.MaxConnections);
        Assert.Equal(256, SegmentPlanner.Plan(1024L * 1024 * 1024, 300).Count);
        Assert.Equal(2, SegmentPlanner.Plan(1024L * 1024, 128).Count);
    }
}

/// <summary>The user's own yt-dlp and FFmpeg.</summary>
public class EnginePathTests
{
    [Fact]
    public void CustomProgramsAreUsedWhenTheyExist()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var keep = (EnginePaths.YtDlp, EnginePaths.FFmpeg);
        try
        {
            var y = Path.Combine(dir, "yt-dlp");
            File.WriteAllText(y, "");
            Assert.Null(EnginePaths.Problem("", "yt-dlp"));
            Assert.Null(EnginePaths.Problem(y, "yt-dlp"));
            Assert.Contains("full path", EnginePaths.Problem("yt-dlp", "yt-dlp"));
            Assert.Contains("There is no", EnginePaths.Problem(Path.Combine(dir, "none"), "FFmpeg"));
            EnginePaths.YtDlp = y;
            Assert.Equal(y, SiteVideoTools.YtDlp);
            Assert.True(SiteVideoTools.EnsureInstalled());
            EnginePaths.FFmpeg = Path.Combine(dir, "missing-ffmpeg");
            Assert.Null(FFmpeg.Path);  // set but missing: no silent fallback to another copy
            EnginePaths.YtDlp = Path.Combine(dir, "gone");
            Assert.False(SiteVideoTools.EnsureInstalled());  // never downloads over the user's choice
        }
        finally { (EnginePaths.YtDlp, EnginePaths.FFmpeg) = keep; Directory.Delete(dir, true); }
    }

    [Fact]
    public void BackupsNeverSetProgramsToRun()
    {
        var json = Backup.Export(new AppSettings { YtDlpPath = "/tmp/evil", FFmpegPath = "/tmp/evil2" },
            new[] { DownloadQueue.Main() }, Array.Empty<DownloadRecord>(), "1.0");
        var back = Backup.Parse(json).Settings!;
        Assert.Equal(("", ""), (back.YtDlpPath, back.FFmpegPath));
    }
}

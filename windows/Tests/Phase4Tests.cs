using System.Globalization;
using Nexonic.Core;
using Xunit;

/// <summary>IDM-style command line, JSON list, crash reports, several QR codes and regional dates.</summary>
public class Phase4Tests
{
    [Fact]
    public void AcceptsIdmSwitches()
    {
        var folder = Path.GetTempPath();
        var r = CommandLineTool.Parse(new[] { "/d", "https://a.test/f.zip", "/p", folder, "/f", "x.zip", "/a", "/n", "/q" });
        Assert.Null(r.Error);
        var job = Assert.Single(r.Jobs);
        Assert.Equal("https://a.test/f.zip", job.Url);
        Assert.Equal("x.zip", job.FileName);
        Assert.Equal(Path.GetFullPath(folder), job.Folder);
        Assert.True(job.Start == false);
        Assert.True(CommandLineTool.IsCommand(new[] { "/d", "https://a.test/f.zip" }));
    }

    [Fact]
    public void ListsDownloadsAsJson()
    {
        var r = CommandLineTool.Parse(new[] { "list", "--json" });
        Assert.True(r.List && r.Json);
        Assert.True(CommandLineTool.Parse(new[] { "https://a.test/f.zip", "--json" }).Json);
        var rec = new DownloadRecord { Url = "https://a.test/f.zip", FileName = "f.zip", Folder = "/d", Cookies = "secret=1", Password = "pw", Size = 10 };
        var json = CommandLineTool.ListJson(new[] { rec });
        Assert.Contains("\"fileName\": \"f.zip\"", json);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("pw", json);
        Assert.Equal("No downloads.", CommandLineTool.ListText(Array.Empty<DownloadRecord>()));
    }

    [Fact]
    public void CrashReportsLeaveOutLinks()
    {
        var text = CrashReports.Text(new InvalidOperationException("failed https://h.test/f?token=abc"), "test", "1.0", new DateTime(2026, 11, 10));
        Assert.Contains("App version: 1.0", text);
        Assert.Contains("[link]", text);
        Assert.DoesNotContain("token=abc", text);
    }

    [Fact]
    public void SplitsManyLinksOverSeveralCodes()
    {
        var links = Enumerable.Range(0, 60).Select(i => $"https://downloads.example.test/folder/file-number-{i:000}.zip").ToList();
        var codes = ShareLinks.Codes(links);
        Assert.True(codes.Count > 1);
        Assert.Equal(60, codes.Sum(c => c.Count));
        Assert.All(codes, c => Assert.True(c.Link.Length <= ShareLinks.MaxLength));
    }

    [Fact]
    public void DatesFollowTheRegion()
    {
        var keep = CultureInfo.CurrentCulture;
        try
        {
            var t = new DateTime(2026, 11, 3, 14, 5, 0);
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(t.ToString("g", new CultureInfo("de-DE")), ListColumns.When(t));
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal(t.ToString("g", new CultureInfo("en-US")), ListColumns.When(t));
        }
        finally { CultureInfo.CurrentCulture = keep; }
    }
}

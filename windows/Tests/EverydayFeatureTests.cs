using Nexonic.Core;
using Xunit;

/// <summary>Download limit, proxy scripts, list columns and the remembered folder.</summary>
public class EverydayFeatureTests
{
    [Fact]
    public void DownloadLimitStopsAtTheAmountAndStartsAgainNextPeriod()
    {
        var cap = new DataCap();
        var t = new DateTime(2026, 11, 9, 10, 0, 0);
        Assert.True(cap.Roll(t, 24));
        Assert.False(cap.Add(600L << 20, 1024));
        Assert.True(cap.Add(500L << 20, 1024));
        Assert.False(cap.Roll(t.AddHours(23), 24));
        Assert.True(cap.Reached(1024));
        Assert.True(cap.Roll(t.AddHours(24), 24));
        Assert.False(cap.Reached(1024));
        Assert.False(cap.Add(5L << 30, 0)); // 0 = no limit
        Assert.Equal(t.AddHours(48), cap.NextPeriod(24));
    }

    [Theory]
    [InlineData("DIRECT", null)]
    [InlineData("PROXY proxy.local:3128; DIRECT", "http://proxy.local:3128/")]
    [InlineData("SOCKS5 10.0.0.2:1080", "socks5://10.0.0.2:1080/")]
    [InlineData("SOCKS 10.0.0.2:1080", "socks5://10.0.0.2:1080/")]
    [InlineData("BOGUS x; PROXY a:8080", "http://a:8080/")]
    [InlineData("", null)]
    public void ReadsTheFirstProxyOfAnAnswer(string answer, string? expected) => Assert.Equal(expected, Pac.First(answer)?.ToString());

    [Fact]
    public void ProxyScriptAddressesAreChecked()
    {
        Assert.True(Pac.AddressOk("http://wpad/proxy.pac"));
        Assert.True(Pac.AddressOk(Path.Combine(Path.GetTempPath(), "proxy.pac")));
        Assert.False(Pac.AddressOk("proxy.pac"));
        Assert.False(Pac.AddressOk("ftp://host/proxy.pac"));
        Assert.False(Pac.AddressOk(""));
    }

    [Fact]
    public void ScriptsThatLoadModulesAreRefused()
    {
        Assert.False(Pac.Allowed("function FindProxyForURL(u,h){ import('qjs:os'); return 'DIRECT'; }"));
        Assert.True(Pac.Allowed("function FindProxyForURL(u,h){ return 'DIRECT'; }"));
    }

    /// <summary>Runs real scripts when the JavaScript engine is next to the tests (CI) or named in NDM_QJS.</summary>
    [Fact]
    public void RunsProxyScripts()
    {
        var engine = Environment.GetEnvironmentVariable("NDM_QJS") is { Length: > 0 } e && File.Exists(e) ? e : SiteVideoTools.QuickJs;
        if (engine == null) return;
        const string script = """
            function FindProxyForURL(url, host) {
              if (isPlainHostName(host) || shExpMatch(host, "*.local")) return "DIRECT";
              if (dnsDomainIs(host, ".example.org")) return "SOCKS5 127.0.0.1:1080";
              if (isInNet(host, "10.0.0.0", "255.0.0.0")) return "DIRECT";
              try { (function(){}).constructor("return 1")(); return "PROXY escaped:1"; } catch (e) {}
              return "PROXY proxy.test:3128; DIRECT";
            }
            """;
        Assert.Equal("DIRECT", Pac.Run(script, new Uri("http://printer/"), engine));
        Assert.Equal("SOCKS5 127.0.0.1:1080", Pac.Run(script, new Uri("https://www.example.org/a.zip"), engine));
        Assert.Equal("DIRECT", Pac.Run(script, new Uri("http://10.1.2.3/file"), engine));
        Assert.Equal("PROXY proxy.test:3128; DIRECT", Pac.Run(script, new Uri("https://203.0.113.9/file"), engine));
        Assert.Null(Pac.Run("function FindProxyForURL(){ while(true){} }", new Uri("http://a.test/"), engine, timeoutMs: 1500));
    }

    [Fact]
    public void ColumnsDescribeTheDownload()
    {
        var r = new DownloadRecord { Folder = "/data", FileName = "a.zip" };
        Assert.Equal("", ListColumns.Resume(r));
        r.Size = 100;
        Assert.Equal("No", ListColumns.Resume(r));
        r.AcceptsRanges = true;
        Assert.Equal("Yes", ListColumns.Resume(r));
        r.Status = DownloadStatus.Finished;
        r.SavedPath = Path.Combine("/elsewhere", "a.zip");
        Assert.Equal("", ListColumns.Resume(r));
        Assert.Equal("/elsewhere", ListColumns.Folder(r));
        Assert.Equal("", ListColumns.When(null));
    }

    [Fact]
    public void RemembersTheLastFolderOnlyWhenAnotherOneWasChosen()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var s = new AppSettings { Folder = "/home/x/Downloads" };
            Assert.Equal(s.Folder, s.StartFolder);
            s.UsedFolder(dir);
            Assert.Equal(dir, s.StartFolder);
            s.UsedFolder(s.Folder);
            Assert.Equal(s.Folder, s.StartFolder);
            s.RememberLastFolder = false;
            s.UsedFolder(dir);
            Assert.Equal(s.Folder, s.StartFolder);
        }
        finally { Directory.Delete(dir, true); }
    }
}

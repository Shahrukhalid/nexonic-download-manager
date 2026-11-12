using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Nexonic.Core;
using Xunit;

/// <summary>Archives other than .zip, RSS feeds and rules, the debrid service, the MCP server and the "while in use" limit.</summary>
public class AutomationFeatureTests
{
    static string Temp() { var d = Path.Combine(Path.GetTempPath(), "ndm-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    [Fact]
    public void UnpacksTarGzAndSkipsEscapingEntries()
    {
        var dir = Temp();
        try
        {
            var path = Path.Combine(dir, "Photos.tar.gz");
            using (var file = File.Create(path))
            using (var gz = new GZipStream(file, CompressionLevel.Fastest))
            using (var tar = new TarWriter(gz))
            {
                void Add(string name, string text)
                {
                    var e = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) };
                    tar.WriteEntry(e);
                }
                Add("a/one.txt", "one");
                Add("../evil.txt", "no");
            }
            Assert.True(Archives.IsArchive(path));
            var folder = Archives.Extract(path);
            Assert.Equal("Photos", Path.GetFileName(folder));
            Assert.Equal("one", File.ReadAllText(Path.Combine(folder, "a", "one.txt")));
            Assert.False(File.Exists(Path.Combine(dir, "evil.txt")));
            Assert.Equal("Photos (2)", Path.GetFileName(Archives.FolderFor(path)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Unpacks7z()
    {
        var dir = Temp();
        try
        {
            var path = Path.Combine(dir, "sample.7z");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.7z"), path);
            var folder = Archives.Extract(path);
            Assert.Equal("hello from 7z\n", File.ReadAllText(Path.Combine(folder, "docs", "readme.txt")));
            Assert.Equal(1000, new FileInfo(Path.Combine(folder, "data.bin")).Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ArchivesWithAPasswordUseTheSavedPasswords()
    {
        var dir = Temp();
        try
        {
            // inside.txt ("secret file\n") in a zip protected with the password "pass123".
            var path = Path.Combine(dir, "locked.zip");
            File.WriteAllBytes(path, Convert.FromBase64String(
                "UEsDBAoACQAAADUpbF082kmDGAAAAAwAAAAKAAAAaW5zaWRlLnR4dExiV+qS7xAnpNSSLuKU9+R6CJvoy1fn1VBLBwg82kmDGAAAAAwAAABQSwECHgMKAAkAAAA1KWxdPNpJgxgAAAAMAAAACgAAAAAAAAABAAAApIEAAAAAaW5zaWRlLnR4dFBLBQYAAAAAAQABADgAAABQAAAAAAA="));
            var none = Assert.Throws<InvalidDataException>(() => Archives.Extract(path));
            Assert.StartsWith(Archives.NeedsPassword, none.Message);
            Assert.Contains("Archive Passwords", none.Message);
            var wrong = Assert.Throws<InvalidDataException>(() => Archives.Extract(path, new[] { "nope" }));
            Assert.Contains("None of the saved", wrong.Message);
            Assert.False(Directory.Exists(Path.Combine(dir, "locked")));  // nothing left behind by the wrong password
            var folder = Archives.Extract(path, new[] { "nope", "pass123" });
            Assert.Equal("secret file\n", File.ReadAllText(Path.Combine(folder, "inside.txt")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("movie.part1.rar", true)]
    [InlineData("movie.part2.rar", false)]
    [InlineData("movie.rar", true)]
    [InlineData("notes.txt", false)]
    public void RarParts(string name, bool archive)
    {
        var dir = Temp();
        try { var p = Path.Combine(dir, name); File.WriteAllText(p, ""); Assert.Equal(archive, Archives.IsArchive(p)); }
        finally { Directory.Delete(dir, true); }
    }

    const string Rss = """
        <?xml version="1.0"?><rss xmlns:torrent="http://xmlns.ezrss.it/0.1/"><channel>
        <item><title>Show S01E01 1080p</title><link>https://example.com/1</link><guid>a1</guid><enclosure url="https://example.com/1.torrent" type="application/x-bittorrent"/></item>
        <item><title>Show S01E02 720p</title><torrent:magnetURI>magnet:?xt=urn:btih:abc</torrent:magnetURI><guid>a2</guid></item>
        <item><title>Other 1080p</title><link>https://example.com/3.zip</link></item></channel></rss>
        """;

    [Fact]
    public void FeedsAndRules()
    {
        var items = Feeds.Parse(Rss);
        Assert.Equal(new[] { "https://example.com/1.torrent", "magnet:?xt=urn:btih:abc", "https://example.com/3.zip" }, items.Select(i => i.Link));
        Assert.Equal("https://example.com/a", Feeds.Parse("""<feed xmlns="http://www.w3.org/2005/Atom"><entry><title>A</title><id>x</id><link href="https://example.com/a"/></entry></feed>""")[0].Link);
        Assert.ThrowsAny<System.Xml.XmlException>(() => Feeds.Parse("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><rss/>"));
        var f = new FeedSubscription { Include = "show 1080p", Exclude = "cam" };
        Assert.True(Feeds.Wanted(f, items[0]));
        Assert.False(Feeds.Wanted(f, items[1]));
        f.Include = "/S01E0[12]/";
        Assert.True(Feeds.Wanted(f, items[1]));
        Assert.NotNull(Feeds.RuleProblem("/[/"));
    }

    [Fact]
    public async Task FeedCheckRemembersWhatItSaw()
    {
        using var server = new HttpListener();
        var port = 47000 + Random.Shared.Next(900);
        server.Prefixes.Add($"http://127.0.0.1:{port}/");
        server.Start();
        _ = Task.Run(async () =>
        {
            while (server.IsListening)
            {
                try { var c = await server.GetContextAsync(); var b = Encoding.UTF8.GetBytes(Rss); c.Response.OutputStream.Write(b); c.Response.Close(); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
            }
        });
        using var http = new HttpClient(Network.Handler(new ProxySettings { Mode = ProxyMode.None }));
        var feed = new FeedSubscription { Url = $"http://127.0.0.1:{port}/feed", Include = "1080p" };
        Assert.Empty(await Feeds.CheckAsync(feed, http));            // the first check only remembers what is there
        feed.Seen.RemoveAt(0);                                      // as if the first item were new
        Assert.Single(await Feeds.CheckAsync(feed, http));
        Assert.Empty(await Feeds.CheckAsync(feed, http));
        Assert.Empty(Feeds.Due(new[] { feed }, DateTime.UtcNow));
        Assert.Single(Feeds.Due(new[] { feed }, DateTime.UtcNow.AddMinutes(31)));
    }

    [Fact]
    public async Task DebridOnlyForChosenSites()
    {
        var s = new DebridSettings { Service = DebridService.RealDebrid, Key = "k", Hosts = "filehost.example, other.example" };
        Assert.True(Debrid.Applies("https://www.filehost.example/file/1", s));
        Assert.False(Debrid.Applies("https://example.com/x", s));
        Assert.False(Debrid.Applies("https://filehost.example/x", new DebridSettings { Hosts = "filehost.example" }));
        Assert.Equal("https://dl.example/x.bin", Debrid.ParseAnswer(DebridService.RealDebrid, """{"download":"https://dl.example/x.bin"}"""));
        Assert.Equal("https://ad.example/x", Debrid.ParseAnswer(DebridService.AllDebrid, """{"status":"success","data":{"link":"https://ad.example/x"}}"""));
        Assert.Equal("https://pm.example/x", Debrid.ParseAnswer(DebridService.Premiumize, """{"status":"success","content":[{"link":"https://pm.example/x"}]}"""));
        Assert.Contains("bad_token", Assert.Throws<InvalidOperationException>(() => Debrid.ParseAnswer(DebridService.RealDebrid, """{"error":"bad_token"}""")).Message);
        using var req = Debrid.Request(s, "https://filehost.example/f");
        Assert.Equal("Bearer k", req.Headers.Authorization!.ToString());
        Assert.Equal("link=https%3A%2F%2Ffilehost.example%2Ff", await req.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task McpServer()
    {
        DownloadJob? added = null;
        var mcp = new McpServer(j => { added = j; return true; }, api: null);
        async Task<JsonObject?> Call(string json) => await mcp.HandleAsync(JsonNode.Parse(json)!.AsObject());
        Assert.Equal("nexonic-download-manager", (await Call("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}"""))!["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal(5, (await Call("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""))!["result"]!["tools"]!.AsArray().Count);
        var r = await Call("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"add_download","arguments":{"url":"https://example.com/a.zip"}}}""");
        Assert.False(r!["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal("https://example.com/a.zip", added!.Url);
        r = await Call("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"add_download","arguments":{"url":"file:///etc/passwd"}}}""");
        Assert.True(r!["result"]!["isError"]!.GetValue<bool>());
        Assert.Null(await Call("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        var output = new StringWriter();
        await mcp.RunAsync(new StringReader("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ping\"}\nnot json\n"), output);
        Assert.Contains("\"id\":9", output.ToString());
        Assert.Contains("-32700", output.ToString());
    }

    [Theory]
    [InlineData(0, 500, 5, 500)]
    [InlineData(0, 500, 300, 0)]
    [InlineData(300, 500, -1, 300)]
    [InlineData(800, 0, 5, 800)]
    public void LimitWhileInUse(int general, int inUse, int idleSeconds, int expected) =>
        Assert.Equal(expected, Idle.Effective(general, inUse, idleSeconds < 0 ? null : TimeSpan.FromSeconds(idleSeconds)));
}

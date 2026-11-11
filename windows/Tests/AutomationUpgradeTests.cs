using System.Net;
using System.Net.Sockets;
using System.Text;
using Nexonic.Core;
using Xunit;

/// <summary>RSS episode tracking, sizes, qualities and preview; torrents through debrid (against a local fake API).</summary>
public class AutomationUpgradeTests
{
    static FeedItem Item(string title, long? size = null) => new(title, title, "https://t.test/" + Uri.EscapeDataString(title), null, size);

    [Fact]
    public void EpisodesAndQualitiesAreRecognised()
    {
        Assert.Equal("my show|s01e02", Feeds.EpisodeKey("[Group] My Show - S01E02 [1080p]"));
        Assert.Equal("my show|s01e02", Feeds.EpisodeKey("My.Show.1x02.720p.WEB"));
        Assert.Equal("news hour|2026-11-10", Feeds.EpisodeKey("News Hour 2026.11.10 1080p"));
        Assert.Null(Feeds.EpisodeKey("Some Movie 2026 1080p"));
        Assert.Equal("2160p", Feeds.QualityOf("Show S01E01 4K HDR"));
        Assert.Equal("720p", Feeds.QualityOf("show.s01e01.720P"));
        Assert.Equal(1_610_612_736L, Feeds.ParseSize("1.5 GiB"));
        Assert.Equal(700L * 1024 * 1024, Feeds.ParseSize("700 MB"));
        Assert.Null(Feeds.ParseSize("big"));
    }

    [Fact]
    public void EachEpisodeOnceInTheBestPreferredQuality()
    {
        var f = new FeedSubscription { OneOfEachEpisode = true, Quality = "1080p 720p", MinSizeMB = 100, MaxSizeMB = 4000, Exclude = "CAM" };
        var items = new List<FeedItem>
        {
            Item("Show S01E01 720p", 500L << 20),
            Item("Show S01E01 1080p", 1500L << 20),
            Item("Show S01E01 2160p", 9000L << 20),
            Item("Show S01E02 480p", 300L << 20),
            Item("Show S01E03 1080p sample", 20L << 20),
            Item("Show S01E04 1080p", null),
            Item("Show S01E05 1080p CAM", 900L << 20),
        };
        var d = Feeds.Decide(f, items).ToDictionary(x => x.Item.Title, x => x.Decision);
        Assert.Equal(Feeds.Decision.BetterQualityInFeed, d["Show S01E01 720p"]);
        Assert.Equal(Feeds.Decision.Download, d["Show S01E01 1080p"]);
        Assert.Equal(Feeds.Decision.TooLarge, d["Show S01E01 2160p"]);
        Assert.Equal(Feeds.Decision.OtherQuality, d["Show S01E02 480p"]);
        Assert.Equal(Feeds.Decision.TooSmall, d["Show S01E03 1080p sample"]);
        Assert.Equal(Feeds.Decision.Download, d["Show S01E04 1080p"]);  // no size given: allowed
        Assert.Equal(Feeds.Decision.Filtered, d["Show S01E05 1080p CAM"]);
        f.Episodes.Add("show|s01e04");
        Assert.Equal(Feeds.Decision.HaveEpisode, Feeds.Decide(f, items).Single(x => x.Item.Title == "Show S01E04 1080p").Decision);
    }

    [Fact]
    public void ReadsSizesFromFeeds()
    {
        const string rss = """
            <rss version="2.0" xmlns:torrent="http://xmlns.ezrss.it/0.1/" xmlns:nyaa="https://nyaa.si/xmlns/nyaa"><channel>
              <item><title>A</title><enclosure url="https://t.test/a.torrent" length="1048576" type="application/x-bittorrent"/></item>
              <item><title>B</title><link>https://t.test/b</link><torrent:contentLength>2097152</torrent:contentLength></item>
              <item><title>C</title><link>https://t.test/c</link><nyaa:size>1.5 GiB</nyaa:size></item>
            </channel></rss>
            """;
        var items = Feeds.Parse(rss);
        Assert.Equal(1048576L, items[0].Size);
        Assert.Equal(2097152L, items[1].Size);
        Assert.Equal(1_610_612_736L, items[2].Size);
    }

    /// <summary>A fake debrid API: answers by request line.</summary>
    static (TcpListener, int) FakeApi(Func<string, string, (int, string)> answer)
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient c;
                try { c = await l.AcceptTcpClientAsync(); } catch { return; }
                _ = Task.Run(async () =>
                {
                    using var _ = c;
                    var s = c.GetStream();
                    var r = new StreamReader(s, Encoding.ASCII);
                    while (await r.ReadLineAsync() is { Length: > 0 } first)
                    {
                        string? line; var len = 0; var auth = "";
                        while (!string.IsNullOrEmpty(line = await r.ReadLineAsync()))
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) len = int.Parse(line[15..].Trim());
                            if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) auth = line[14..].Trim();
                        }
                        var body = new char[len];
                        var got = 0;
                        while (got < len) got += await r.ReadAsync(body, got, len - got);
                        var (code, json) = auth is "" or "Bearer KEY" ? answer(first, new string(body)) : (401, "{\"error\":\"bad_token\"}");
                        var bytes = Encoding.UTF8.GetBytes(json);
                        await s.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {code} X\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\n\r\n"));
                        await s.WriteAsync(bytes);
                    }
                });
            }
        });
        return (l, ((IPEndPoint)l.LocalEndpoint).Port);
    }

    [Fact]
    public async Task RealDebridTorrentBecomesDirectDownloads()
    {
        var polls = 0;
        var (l, port) = FakeApi((first, body) => first switch
        {
            _ when first.StartsWith("POST /torrents/addMagnet") && body.Contains("magnet") => (201, "{\"id\":\"T1\"}"),
            _ when first.StartsWith("GET /torrents/info/T1") => Interlocked.Increment(ref polls) switch
            {
                1 => (200, "{\"status\":\"waiting_files_selection\"}"),
                2 => (200, "{\"status\":\"downloading\",\"progress\":40}"),
                _ => (200, "{\"status\":\"downloaded\",\"links\":[\"https://rd.test/d/1\",\"https://rd.test/d/2\"]}"),
            },
            _ when first.StartsWith("POST /torrents/selectFiles/T1") && body == "files=all" => (204, ""),
            _ when first.StartsWith("POST /unrestrict/link") => (200, body.Contains("%2F1")
                ? "{\"filename\":\"one.mkv\",\"download\":\"https://cdn.test/one.mkv\",\"filesize\":100}"
                : "{\"filename\":\"../two.srt\",\"download\":\"https://cdn.test/two.srt\",\"filesize\":5}"),
            _ => (404, "{\"error\":\"unknown\"}"),
        });
        var s = new DebridSettings { Service = DebridService.RealDebrid, Key = "KEY" };
        using var http = new HttpClient();
        var seen = new List<string>();
        var files = await DebridTorrents.ResolveAsync(s, "magnet:?xt=urn:btih:abc", http, seen.Add, CancellationToken.None,
            $"http://127.0.0.1:{port}", TimeSpan.FromSeconds(20));
        l.Stop();
        Assert.Equal(2, files.Count);
        Assert.Equal(("one.mkv", "https://cdn.test/one.mkv", (long?)100), (files[0].Name, files[0].Url, files[0].Size));
        Assert.False(files[1].Name.Contains('/'));  // a name from the service cannot leave the folder
        Assert.True(seen.Count >= 1);
    }

    [Fact]
    public async Task DebridProblemsAreReportedNotHidden()
    {
        var (l, port) = FakeApi((first, _) => first.StartsWith("POST /transfer/directdl")
            ? (200, "{\"status\":\"error\",\"message\":\"Not cached\"}") : (404, "{}"));
        using var http = new HttpClient();
        var pm = new DebridSettings { Service = DebridService.Premiumize, Key = "KEY" };
        var ex = await Record(() => DebridTorrents.ResolveAsync(pm, "magnet:?xt=urn:btih:abc", http, null, CancellationToken.None, $"http://127.0.0.1:{port}"));
        Assert.Contains("Not cached", ex);
        var bad = new DebridSettings { Service = DebridService.RealDebrid, Key = "WRONG" };
        Assert.Contains("bad_token", await Record(() => DebridTorrents.ResolveAsync(bad, "magnet:?xt=urn:btih:abc", http, null, CancellationToken.None, $"http://127.0.0.1:{port}")));
        l.Stop();
        Assert.Null(DebridTorrents.Unsupported(new DebridSettings { Service = DebridService.AllDebrid, Key = "k" }));
        Assert.Contains("Choose", DebridTorrents.Unsupported(new DebridSettings()));
        Assert.Contains("magnet", await Record(() => DebridTorrents.ResolveAsync(pm, "https://x.test/a.torrent", http, null, CancellationToken.None)));
    }

    [Fact]
    public async Task AllDebridTorrentBecomesDirectDownloads()
    {
        var polls = 0;
        var (l, port) = FakeApi((first, body) => first switch
        {
            _ when first.StartsWith("POST /magnet/upload?agent=nexonic") && body.Contains("magnet") => (200, "{\"status\":\"success\",\"data\":{\"magnets\":[{\"id\":77}]}}"),
            _ when first.StartsWith("GET /magnet/status?id=77&agent=nexonic") => Interlocked.Increment(ref polls) switch
            {
                1 => (200, "{\"status\":\"success\",\"data\":{\"magnets\":{\"id\":77,\"status\":\"Downloading\",\"statusCode\":1}}}"),
                _ => (200, "{\"status\":\"success\",\"data\":{\"magnets\":{\"id\":77,\"status\":\"Ready\",\"statusCode\":4}}}"),
            },
            _ when first.StartsWith("POST /magnet/files") => (200, "{\"status\":\"success\",\"data\":{\"magnets\":[{\"id\":\"77\",\"files\":[{\"n\":\"Show\",\"e\":[{\"n\":\"one.mkv\",\"s\":100,\"l\":\"https://ad.test/f/1\"},{\"n\":\"two.srt\",\"s\":5,\"l\":\"https://ad.test/f/2\"}]}]}]}}"),
            _ when first.StartsWith("POST /link/unlock") => (200, body.Contains("%2F1")
                ? "{\"status\":\"success\",\"data\":{\"link\":\"https://cdn.test/one.mkv\",\"filename\":\"one.mkv\",\"filesize\":100}}"
                : "{\"status\":\"success\",\"data\":{\"link\":\"https://cdn.test/two.srt\",\"filename\":\"../two.srt\",\"filesize\":5}}"),
            _ => (200, "{\"status\":\"error\",\"error\":{\"code\":\"UNKNOWN\",\"message\":\"unknown\"}}"),
        });
        using var http = new HttpClient();
        var s = new DebridSettings { Service = DebridService.AllDebrid, Key = "KEY" };
        var seen = new List<string>();
        var files = await DebridTorrents.ResolveAsync(s, "magnet:?xt=urn:btih:abc", http, seen.Add, CancellationToken.None, $"http://127.0.0.1:{port}", TimeSpan.FromSeconds(20));
        Assert.Equal(2, files.Count);
        Assert.Equal("one.mkv", files[0].Name);
        Assert.False(files[1].Name.Contains('/'));
        Assert.Equal("https://cdn.test/one.mkv", files[0].Url);
        Assert.Contains(seen, x => x.Contains("Downloading"));
        Assert.Contains("unknown", await Record(() => DebridTorrents.ResolveAsync(s, "magnet:?xt=urn:btih:zzz", http, null, CancellationToken.None, $"http://127.0.0.1:{port}/bad")));
        l.Stop();
    }

    static async Task<string> Record(Func<Task> f)
    {
        try { await f(); return ""; }
        catch (InvalidOperationException e) { return e.Message; }
    }

    [Fact]
    public void FindsDownloadsYouHaveAlready()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var done = Path.Combine(dir, "done.zip");
            File.WriteAllText(done, "x");
            var list = new[]
            {
                new DownloadRecord { Url = "https://h.test/a.zip", FileName = "a.zip", Status = DownloadStatus.Paused },
                new DownloadRecord { Url = "https://h.test/done.zip", FileName = "done.zip", Status = DownloadStatus.Finished, SavedPath = done },
                new DownloadRecord { Url = "https://h.test/gone.zip", FileName = "gone.zip", Status = DownloadStatus.Finished, SavedPath = Path.Combine(dir, "gone.zip") },
            };
            Assert.Equal(Duplicates.Kind.InList, Duplicates.Find(list, "HTTPS://H.TEST/a.zip#part", "a.zip", dir).Kind);
            Assert.Equal(Duplicates.Kind.Downloaded, Duplicates.Find(list, "https://h.test/done.zip", "done.zip", dir).Kind);
            Assert.Equal(Duplicates.Kind.None, Duplicates.Find(list, "https://h.test/gone.zip", "gone.zip", dir).Kind);  // file deleted: fine to get again
            Assert.Equal(Duplicates.Kind.FileExists, Duplicates.Find(list, "https://other.test/done.zip", "done.zip", dir).Kind);
            Assert.Equal(Duplicates.Kind.None, Duplicates.Find(list, "https://h.test/new.zip", "new.zip", dir).Kind);
            Assert.Contains("already in the folder", Duplicates.Describe(Duplicates.Kind.FileExists, "done.zip"));
        }
        finally { Directory.Delete(dir, true); }
    }
}

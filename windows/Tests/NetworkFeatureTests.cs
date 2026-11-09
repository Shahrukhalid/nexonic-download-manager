using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Nexonic.Core;
using Xunit;

/// <summary>These tests change the shared network options, so nothing else runs at the same time.</summary>
[CollectionDefinition("Network options", DisableParallelization = true)]
public class NetworkOptionsCollection { }

/// <summary>Share links, the torrent blocklist and keeping downloads on one network adapter (kill switch).</summary>
[Collection("Network options")]
public class NetworkFeatureTests
{
    [Theory]
    [InlineData("https://drive.google.com/file/d/1AbCdEfGhIjKlMn/view?usp=sharing", "https://drive.usercontent.google.com/download?id=1AbCdEfGhIjKlMn&export=download&confirm=t")]
    [InlineData("https://drive.google.com/open?id=1AbCdEfGhIjKlMn&resourcekey=0-x", "https://drive.usercontent.google.com/download?id=1AbCdEfGhIjKlMn&export=download&confirm=t&resourcekey=0-x")]
    [InlineData("https://drive.google.com/uc?id=1AbCdEfGhIjKlMn&export=download", "https://drive.usercontent.google.com/download?id=1AbCdEfGhIjKlMn&export=download&confirm=t")]
    [InlineData("https://docs.google.com/document/d/1AbCdEfGhIjKlMn/edit", "https://docs.google.com/document/d/1AbCdEfGhIjKlMn/export?format=docx")]
    [InlineData("https://docs.google.com/spreadsheets/d/1AbCdEfGhIjKlMn/edit#gid=0", "https://docs.google.com/spreadsheets/d/1AbCdEfGhIjKlMn/export?format=xlsx")]
    [InlineData("https://docs.google.com/presentation/d/1AbCdEfGhIjKlMn/view", "https://docs.google.com/presentation/d/1AbCdEfGhIjKlMn/export?format=pptx")]
    [InlineData("https://www.dropbox.com/scl/fi/abc/f.zip?rlkey=k&dl=0", "https://www.dropbox.com/scl/fi/abc/f.zip?rlkey=k&dl=1")]
    [InlineData("https://github.com/u/r/blob/main/a/b.txt", "https://raw.githubusercontent.com/u/r/main/a/b.txt")]
    [InlineData("https://drive.google.com/drive/folders/abc", "https://drive.google.com/drive/folders/abc")]
    [InlineData("https://example.com/x.zip", "https://example.com/x.zip")]
    public void ShareLinksBecomeTheFile(string share, string direct) => Assert.Equal(direct, Links.Unwrap(share));

    [Fact]
    public void BlocklistFormats()
    {
        var text = "# comment\nBad:1.2.3.0-1.2.3.255\n001.002.004.000 - 001.002.004.010 , 000 , x\n10.0.0.0/8\n5.6.7.8\n2001:db8::/32\nnot a line\n9.9.9.9-1.1.1.1\n";
        var ranges = TorrentBlocklist.Parse(new StringReader(text)).Select(r => $"{r.First}-{r.Last}");
        Assert.Equal(new[] { "1.2.3.0-1.2.3.255", "1.2.4.0-1.2.4.10", "10.0.0.0-10.255.255.255", "5.6.7.8-5.6.7.8",
            "2001:db8::-2001:db8:ffff:ffff:ffff:ffff:ffff:ffff" }, ranges);
        var data = new MemoryStream();
        using (var gz = new GZipStream(data, CompressionLevel.Fastest, true)) gz.Write("x:9.9.9.0-9.9.9.9\n"u8);
        data.Position = 0;
        Assert.Single(TorrentBlocklist.Parse(data));
    }

    [Fact]
    public async Task BlocklistFromFileAndEmptyIsAnError()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "1.2.3.4\n");
            Assert.Single(await TorrentBlocklist.LoadAsync(file, null));
            File.WriteAllText(file, "# nothing\n");
            await Assert.ThrowsAsync<InvalidDataException>(() => TorrentBlocklist.LoadAsync(file, null));
            await Assert.ThrowsAsync<InvalidOperationException>(() => TorrentBlocklist.LoadAsync("http://example.com/list", null));
            Assert.Empty(await TorrentBlocklist.LoadAsync("", null));
        }
        finally { File.Delete(file); }
    }

    static string LoopbackAdapter() => Adapters.List().First(a => a.Addresses.Any(IPAddress.IsLoopback) && a.Up).Name;

    [Fact]
    public async Task DownloadsKeepToTheChosenAdapter()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var client = await server.AcceptTcpClientAsync();
            var from = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
            var stream = client.GetStream();
            var buffer = new byte[4096];
            await stream.ReadAsync(buffer);
            var body = from.ToString();
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"));
        });
        var keep = Network.Options;
        try
        {
            Network.Options = new NetworkOptions { BindAdapter = LoopbackAdapter() };
            using var http = new HttpClient(Network.Handler(new ProxySettings { Mode = ProxyMode.None }));
            Assert.Equal("127.0.0.1", await http.GetStringAsync($"http://127.0.0.1:{port}/"));
            await serve;
        }
        finally { Network.Options = keep; }
    }

    [Fact]
    public async Task KillSwitchWhenTheAdapterIsDown()
    {
        var keep = Network.Options;
        try
        {
            Network.Options = new NetworkOptions { BindAdapter = "nexonic-missing-vpn0" };
            using var http = new HttpClient(Network.Handler(new ProxySettings { Mode = ProxyMode.None }));
            var e = await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetStringAsync("http://127.0.0.1:9/"));
            Assert.True(e is AdapterDownException || e.InnerException is AdapterDownException, e.ToString());
            Assert.Throws<AdapterDownException>(() => SiteVideo.Prepare(new List<string> { "https://example.com/v" }, null));
            Assert.Contains(await NetworkCheck.RunAsync(new ProxySettings { Mode = ProxyMode.None }, "http://127.0.0.1:9/"), l => l.Contains("not connected"));
        }
        finally { Network.Options = keep; }
    }

    [Fact]
    public void VideoEngineUsesTheAdapterAddress()
    {
        var keep = Network.Options;
        try
        {
            Network.Options = new NetworkOptions { BindAdapter = LoopbackAdapter() };
            var (args, file) = SiteVideo.Prepare(new List<string> { "https://example.com/v" }, null);
            Assert.Null(file);
            Assert.Equal(new[] { "--source-address", "127.0.0.1", "https://example.com/v" }, args);
        }
        finally { Network.Options = keep; }
    }
}

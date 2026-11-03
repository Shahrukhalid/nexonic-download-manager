using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Nexonic.Core;
using Xunit;

/// <summary>Security and integrity fixes: private headers stay on the download's own server, resumes never mix files.</summary>
public class HardeningTests
{
    /// <summary>A tiny HTTP server that records every request's headers.</summary>
    sealed class Server : IDisposable
    {
        readonly TcpListener listener;
        readonly Func<string, Dictionary<string, string>, (int Code, Dictionary<string, string> Headers, byte[] Body)> handle;
        public readonly List<Dictionary<string, string>> Seen = new();
        public int Port { get; }

        public Server(Func<string, Dictionary<string, string>, (int, Dictionary<string, string>, byte[])> handle)
        {
            this.handle = handle;
            listener = new TcpListener(IPAddress.Loopback, 0);  // reached as both "localhost" and "127.0.0.1"
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(Loop);
        }

        async Task Loop()
        {
            while (true)
            {
                TcpClient c;
                try { c = await listener.AcceptTcpClientAsync(); } catch { return; }
                _ = Task.Run(() => Serve(c));
            }
        }

        async Task Serve(TcpClient c)
        {
            using var _ = c;
            var s = c.GetStream();
            var reader = new StreamReader(s, Encoding.ASCII);
            while (true)
            {
                var first = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(first)) return;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    var i = line.IndexOf(':');
                    headers[line[..i].Trim()] = line[(i + 1)..].Trim();
                }
                lock (Seen) Seen.Add(headers);
                var (code, h, body) = handle(first.Split(' ')[1], headers);
                var sb = new StringBuilder($"HTTP/1.1 {code} X\r\nContent-Length: {body.Length}\r\n");
                foreach (var kv in h) sb.Append($"{kv.Key}: {kv.Value}\r\n");
                sb.Append("\r\n");
                try
                {
                    await s.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
                    await s.WriteAsync(body);
                }
                catch { return; }
            }
        }

        public void Dispose() => listener.Stop();
    }

    /// <summary>Serves `content` with byte ranges and the given ETag.</summary>
    static (int, Dictionary<string, string>, byte[]) Ranged(byte[] content, string etag, Dictionary<string, string> req)
    {
        var h = new Dictionary<string, string> { ["ETag"] = etag, ["Accept-Ranges"] = "bytes" };
        if (req.TryGetValue("If-Range", out var ir) && ir != etag) return (200, h, content);
        if (!req.TryGetValue("Range", out var r)) return (200, h, content);
        var parts = r["bytes=".Length..].Split('-');
        var from = long.Parse(parts[0]);
        var to = parts[1].Length > 0 ? long.Parse(parts[1]) : content.Length - 1;
        h["Content-Range"] = $"bytes {from}-{to}/{content.Length}";
        return (206, h, content[(int)from..((int)to + 1)]);
    }

    static async Task<DownloadRecord> Run(IDownloader t)
    {
        var done = new TaskCompletionSource();
        t.Changed += r => { if (r.Status is DownloadStatus.Finished or DownloadStatus.Failed) done.TrySetResult(); };
        await t.StartAsync();
        await Task.WhenAny(done.Task, Task.Delay(20000));
        return t.Record;
    }

    static string TempFolder() => Directory.CreateTempSubdirectory("ndm-test").FullName;

    [Fact]
    public async Task RedirectToAnotherHostDropsCookiesAndLogin()
    {
        var content = Encoding.ASCII.GetBytes("0123456789");
        using var b = new Server((_, req) => Ranged(content, "\"v1\"", req));
        using var a = new Server((_, _) => (302, new() { ["Location"] = $"http://127.0.0.1:{b.Port}/file.bin" }, Array.Empty<byte>()));
        var rec = new DownloadRecord
        {
            Url = $"http://localhost:{a.Port}/file.bin", Folder = TempFolder(), FileName = "file.bin",
            Cookies = "session=secret", Username = "me", Password = "pw",
        };
        var r = await Run(new Transfer(rec, 2, new SpeedLimiter()));
        Assert.Equal(DownloadStatus.Finished, r.Status);
        Assert.Equal("0123456789", File.ReadAllText(r.SavedPath!));
        Assert.Contains(a.Seen, h => h.ContainsKey("Cookie"));  // the original host still gets them
        Assert.DoesNotContain(b.Seen, h => h.ContainsKey("Cookie") || h.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task VideoPiecesOnOtherHostsGetNoCookiesOrLogin()
    {
        using var cdn = new Server((_, _) => (200, new(), Encoding.ASCII.GetBytes("PIECE")));
        using var site = new Server((path, _) => (200, new() { ["Content-Type"] = "application/vnd.apple.mpegurl" },
            Encoding.ASCII.GetBytes($"#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXTINF:4,\nhttp://127.0.0.1:{cdn.Port}/a.ts\n#EXTINF:4,\nhttp://127.0.0.1:{cdn.Port}/b.ts\n#EXT-X-ENDLIST\n")));
        var rec = new DownloadRecord
        {
            Url = $"http://localhost:{site.Port}/v.m3u8", Folder = TempFolder(), FileName = "v.mp4", IsStream = true,
            Cookies = "session=secret", Username = "me", Password = "pw",
        };
        var r = await Run(new StreamTransfer(rec, 2, new SpeedLimiter()));
        Assert.Equal(DownloadStatus.Finished, r.Status);
        Assert.Contains(site.Seen, h => h.ContainsKey("Cookie") && h.ContainsKey("Authorization"));
        Assert.Equal(2, cdn.Seen.Count);
        Assert.DoesNotContain(cdn.Seen, h => h.ContainsKey("Cookie") || h.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task ResumeNeverMixesTwoVersionsOfAFile()
    {
        var content = Encoding.ASCII.GetBytes("BBBBBBBBBB");
        using var server = new Server((_, req) => Ranged(content, "\"v2\"", req));
        var rec = new DownloadRecord
        {
            Url = $"http://localhost:{server.Port}/f.bin", Folder = TempFolder(), FileName = "f.bin",
            Size = 10, AcceptsRanges = true, Validator = "\"v1\"", Status = DownloadStatus.Paused,
            Segments = new() { new Segment { Id = 0, Start = 0, End = 4, Written = 5 }, new Segment { Id = 1, Start = 5, End = 9 } },
        };
        var parts = Path.Combine(AppPaths.Parts, rec.Id.ToString("N"));
        Directory.CreateDirectory(parts);
        File.WriteAllText(Path.Combine(parts, "0.part"), "AAAAA");  // half of the old version
        var r = await Run(new Transfer(rec, 2, new SpeedLimiter()));
        Assert.Equal(DownloadStatus.Finished, r.Status);
        Assert.Equal("BBBBBBBBBB", File.ReadAllText(r.SavedPath!));
        Assert.Equal("\"v2\"", r.Validator);
    }

    [Fact]
    public void PrivateHeaderAndRangeRules()
    {
        var origin = new Uri("https://files.example.com/a");
        Assert.True(Network.SameSite(new Uri("https://FILES.example.com/b"), origin));
        Assert.False(Network.SameSite(new Uri("http://files.example.com/b"), origin));  // no step down to http
        Assert.False(Network.SameSite(new Uri("https://cdn.example.com/b"), origin));
        Assert.False(Network.SameSite(new Uri("https://files.example.com:8443/b"), origin));
        Assert.True(Network.RangeMatches(new ContentRangeHeaderValue(5, 9, 10), 5, 10));
        Assert.False(Network.RangeMatches(new ContentRangeHeaderValue(0, 4, 10), 5, 10));  // wrong part
        Assert.False(Network.RangeMatches(new ContentRangeHeaderValue(5, 9, 99), 5, 10));  // another file
        Assert.False(Network.RangeMatches(null, 5, 10));
        Assert.True(Network.SameVersion(null, "\"x\""));
        Assert.False(Network.SameVersion("\"a\"", "\"b\""));
        var creds = Network.CredentialsFor(origin, new NetworkCredential("u", "p"));
        Assert.NotNull(creds.GetCredential(new Uri("https://files.example.com/x"), "Basic"));
        Assert.Null(creds.GetCredential(new Uri("https://evil.example.net/x"), "Basic"));
    }
}

/// <summary>Secrets saved encrypted, old plain lists still load, unreadable lists are kept.</summary>
[Collection("Secrets")]
public class StorageTests
{
    sealed class Failing : ISecretProtector
    {
        public string? Protect(string plain) => null;
        public string? Unprotect(string sealedText) => null;
    }

    sealed class Reverse : ISecretProtector
    {
        public string? Protect(string plain) => new string(plain.Reverse().ToArray());
        public string? Unprotect(string sealedText) => sealedText == "broken" ? null : new string(sealedText.Reverse().ToArray());
    }

    [Fact]
    public void SecretsAreSavedEncryptedAndReadBack()
    {
        var before = Secrets.Protector;
        Secrets.Protector = new Reverse();
        try
        {
            var list = new List<DownloadRecord> { new() { Url = "https://e.com/a", Cookies = "sid=abc", Username = "me", Password = "pw" } };
            var json = Secrets.SerializeSealed(list);
            Assert.DoesNotContain("sid=abc", json);
            Assert.DoesNotContain("\"pw\"", json);
            Assert.Contains("dpapi:", json);
            var back = Secrets.DeserializeOpened<List<DownloadRecord>>(json)!;
            Assert.Equal("sid=abc", back[0].Cookies);
            Assert.Equal("pw", back[0].Password);
            Assert.Equal("me", back[0].Username);  // user names are not secret

            // a list saved by 0.6.1 (plain text) still loads, and is encrypted on the next save
            var old = "[{\"Url\":\"https://e.com/b\",\"Cookies\":\"old=1\",\"Password\":\"p\"}]";
            var loaded = Secrets.DeserializeOpened<List<DownloadRecord>>(old)!;
            Assert.Equal("old=1", loaded[0].Cookies);
            Assert.DoesNotContain("old=1", Secrets.SerializeSealed(loaded));

            var settings = new AppSettings();
            settings.Proxy.Password = "proxy-pw";
            var sj = Secrets.SerializeSealed(settings);
            Assert.DoesNotContain("proxy-pw", sj);
            Assert.Equal("proxy-pw", Secrets.DeserializeOpened<AppSettings>(sj)!.Proxy.Password);

            // a secret that can't be decrypted (another Windows user) is dropped, the download is kept
            Assert.Equal("", Secrets.Open(Secrets.Prefix + "broken"));

            // encryption failing never writes the secret in plain text
            Secrets.Protector = new Failing();
            Assert.Null(Secrets.Seal("sid=abc"));
            Assert.DoesNotContain("sid=abc", Secrets.SerializeSealed(list));
        }
        finally { Secrets.Protector = before; }
    }

    [Fact]
    public void WindowsEncryptionRoundTrip()
    {
        if (!OperatingSystem.IsWindows()) return;  // DPAPI exists only on Windows (CI runs this on windows-latest)
        var sealedText = Secrets.Seal("sid=abc; token=xyz")!;
        Assert.True(sealedText.StartsWith(Secrets.Prefix));
        Assert.DoesNotContain("sid=abc", sealedText);
        Assert.Equal("sid=abc; token=xyz", Secrets.Open(sealedText));
    }

    [Fact]
    public void SavedListsSurviveCrashesAndDamage()
    {
        var dir = Directory.CreateTempSubdirectory("ndm-store").FullName;
        var path = Path.Combine(dir, "downloads.json");
        SafeFile.Write(path, "[1,2]");
        Assert.Equal("[1,2]", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
        File.WriteAllText(path, "[{\"Url\": ");  // a damaged list
        Assert.Null(SafeFile.Load(path, Secrets.DeserializeOpened<List<DownloadRecord>>));
        Assert.Single(Directory.GetFiles(dir, "downloads.unreadable-*.json"));  // kept for recovery, not overwritten
        // a file that can't be read at all (locked, no permission) does not stop the app from starting
        Assert.Null(SafeFile.Load<List<DownloadRecord>>(path, _ => throw new UnauthorizedAccessException()));
    }
}

/// <summary>yt-dlp: cookies, logins and the proxy never on the command line; the app's proxy is used.</summary>
public class VideoEngineTests
{
    [Fact]
    public void PrivateOptionsLeaveTheCommandLine()
    {
        var args = SiteVideo.Arguments("https://youtu.be/abc", "/out", "/tmp", null, null, false, "https://ref", "sid=a'b",
            "UA", "me", "p w", 0);
        var (rest, config) = SiteVideo.Private(args, "http://u:p@proxy:8080");
        var line = string.Join(" ", rest);
        Assert.DoesNotContain("sid=", line);
        Assert.DoesNotContain("--password", line);
        Assert.DoesNotContain("proxy:8080", line);
        Assert.Contains("--referer", line);  // not secret: stays
        Assert.Equal("https://youtu.be/abc", rest[^1]);
        Assert.Contains("--add-header 'Cookie:sid=a'\"'\"'b'", config);
        Assert.Contains("--username 'me'", config);
        Assert.Contains("--password 'p w'", config);
        Assert.Contains("--proxy 'http://u:p@proxy:8080'", config);
        Assert.Equal("", SiteVideo.Private(new List<string> { "-J", "--", "u" }, null).Config);
    }

    [Fact]
    public void ProxyForTheVideoEngine()
    {
        Assert.Null(SiteVideo.ProxyUrl(new ProxySettings()));  // system settings
        Assert.Equal("", SiteVideo.ProxyUrl(new ProxySettings { Mode = ProxyMode.None }));
        Assert.Equal("socks5://a%40b:p%3Aw@10.0.0.1:1080",
            SiteVideo.ProxyUrl(new ProxySettings { Mode = ProxyMode.Socks, Host = "10.0.0.1", Port = 1080, Username = "a@b", Password = "p:w" }));
        Assert.Equal("http://h:3128", SiteVideo.ProxyUrl(new ProxySettings { Mode = ProxyMode.Http, Host = " h ", Port = 3128 }));
        var (args, file) = SiteVideo.Prepare(new List<string> { "-J", "--add-header", "Cookie:x=1", "--", "u" },
            new ProxySettings { Mode = ProxyMode.None });
        try
        {
            Assert.Equal("--config-locations", args[0]);
            Assert.Contains("Cookie:x=1", File.ReadAllText(file!));
            Assert.Contains("--proxy ''", File.ReadAllText(file!));
        }
        finally { new ConfigCleanup(file).Dispose(); }
        Assert.False(File.Exists(file));
    }
}

/// <summary>Updates: only this app's own release file, checked against GitHub's published SHA-256.</summary>
public class UpdateTests
{
    [Fact]
    public void UpdatesAreVerified()
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, "abc");
        const string sha = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        var r = Updates.Parse("{\"tag_name\":\"v0.7.0\",\"html_url\":\"https://x/r\",\"assets\":[{\"name\":\"Setup.exe\"," +
                              "\"browser_download_url\":\"https://github.com/Shahrukhalid/nexonic-download-manager/releases/download/v0.7.0/Setup.exe\"," +
                              "\"digest\":\"sha256:BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD\"},{\"name\":\"old.zip\",\"browser_download_url\":\"https://github.com/x\"}]}")!;
        Assert.Equal(sha, Updates.Digest(r, "Setup.exe"));
        Assert.Null(Updates.Digest(r, "old.zip"));  // no published checksum: not installed automatically
        Assert.True(Updates.TrustedAsset(r.Assets["Setup.exe"]));
        Assert.False(Updates.TrustedAsset("http://github.com/Shahrukhalid/nexonic-download-manager/releases/download/v1/a.exe"));
        Assert.False(Updates.TrustedAsset("https://github.com.evil.io/Shahrukhalid/nexonic-download-manager/releases/download/v1/a.exe"));
        Assert.False(Updates.TrustedAsset("https://github.com/someone/else/releases/download/v1/a.exe"));
        Assert.True(Updates.Verify(file, sha));
        File.WriteAllText(file, "abd");
        Assert.False(Updates.Verify(file, sha));
        File.Delete(file);
    }
}

public class TimeLeftTests
{
    [Fact]
    public void TimeLeftNeverWrapsOrCrashes()
    {
        Assert.Equal("1:40", TimeLeftText.Of(1000, 0, 10));
        Assert.Equal("30:00:00", TimeLeftText.Of(108_000, 0, 1));  // 30 hours, not "06:00:00"
        Assert.Equal("0:00", TimeLeftText.Of(100, 150, 10));  // more received than expected: not negative
        Assert.Equal("more than 99 days", TimeLeftText.Of(long.MaxValue, 0, 1));  // used to throw
        Assert.Equal("", TimeLeftText.Of(null, 0, 10));
        Assert.Equal("", TimeLeftText.Of(1000, 0, 0));
    }
}

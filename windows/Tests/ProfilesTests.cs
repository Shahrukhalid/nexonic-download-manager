using Nexonic.Core;
using Xunit;

namespace Nexonic.Tests;

// Tests that swap the app-wide secret protector run one at a time.
[Collection("Secrets")]
public class ProfilesTests
{
    [Fact]
    public void MostSpecificSiteProfileWins()
    {
        var list = new List<HostProfile>
        {
            new() { Host = "example.com", MaxConnections = 4 },
            new() { Host = "https://Files.Example.com/path", MaxConnections = 2 },
            new() { Host = "*.other.org", Enabled = false },
        };
        Assert.Equal(2, HostProfiles.Match(list, "https://files.example.com/a.zip")!.MaxConnections);
        Assert.Equal(4, HostProfiles.Match(list, "https://www.example.com/a.zip")!.MaxConnections);
        Assert.Equal(4, HostProfiles.Match(list, "http://example.com:8080/a.zip")!.MaxConnections);
        Assert.Null(HostProfiles.Match(list, "https://notexample.com/a.zip"));
        Assert.Null(HostProfiles.Match(list, "https://x.other.org/a.zip"));
        Assert.Equal("example.com", HostProfiles.Normalize(" *.Example.com:443/x "));
    }

    [Fact]
    public void SiteLoginsAreSavedEncryptedAndNeverBackedUp()
    {
        var before = (Secrets.Protector, Secrets.AllowPlainText);
        try
        {
            Secrets.Protector = new Reverse();
            var s = new AppSettings();
            s.HostProfiles.Add(new HostProfile { Host = "example.com", Username = "me", Password = "site-secret", Proxy = new ProxySettings { Password = "proxy-secret" } });
            var json = Secrets.SerializeSealed(s);
            Assert.DoesNotContain("site-secret", json);
            Assert.DoesNotContain("proxy-secret", json);
            var back = Secrets.DeserializeOpened<AppSettings>(json)!;
            Assert.Equal("site-secret", back.HostProfiles[0].Password);
            Assert.Equal("proxy-secret", back.HostProfiles[0].Proxy!.Password);

            var records = new[] { new DownloadRecord { Url = "https://example.com/a.zip", FileName = "a.zip", Cookies = "sid=1", Password = "pw" } };
            var backup = Backup.Export(back, new[] { DownloadQueue.Main() }, records, "1.0");
            foreach (var secret in new[] { "site-secret", "proxy-secret", "sid=1", "\"pw\"" }) Assert.DoesNotContain(secret, backup);
        }
        finally { (Secrets.Protector, Secrets.AllowPlainText) = before; }
    }

    [Fact]
    public void BackupRoundTripSkipsDuplicatesAndRestartsUnfinished()
    {
        var done = new DownloadRecord { Url = "https://example.com/a.zip", FileName = "a.zip", Status = DownloadStatus.Finished };
        var part = new DownloadRecord { Url = "https://example.com/b.zip", FileName = "b.zip", Status = DownloadStatus.Downloading, Validator = "\"x\"" };
        part.Segments.Add(new Segment { Id = 0, Start = 0, End = 99, Written = 50 });
        var queue = new DownloadQueue { Name = "Night" };
        var json = Backup.Export(new AppSettings(), new[] { DownloadQueue.Main(), queue }, new[] { done, part }, "1.0");
        var file = Backup.Parse(json);
        Assert.Equal(2, file.Queues!.Count);
        var b = file.Downloads!.Single(r => r.FileName == "b.zip");
        Assert.Equal(DownloadStatus.Paused, b.Status);
        Assert.Empty(b.Segments);
        Assert.Null(b.Validator);
        Assert.Equal(DownloadStatus.Finished, file.Downloads!.Single(r => r.FileName == "a.zip").Status);
        var existing = new[] { new DownloadRecord { Url = done.Url, FileName = done.FileName } };
        Assert.Equal(new[] { "b.zip" }, Backup.NewDownloads(file, existing).Select(r => r.FileName));
    }

    [Fact]
    public void RejectsOtherFilesAndNewerBackups()
    {
        Assert.Throws<InvalidDataException>(() => Backup.Parse("{\"hello\":1}"));
        Assert.Throws<InvalidDataException>(() => Backup.Parse("not json"));
        Assert.Throws<InvalidDataException>(() => Backup.Parse("{\"Format\":\"nexonic-backup\",\"Version\":99}"));
    }

    [Fact]
    public void NetworkOptionsAreKeptInRange()
    {
        var o = new NetworkOptions { MinSegmentKB = 1, Retries = 0, ConnectTimeoutSeconds = 9999, ReadTimeoutSeconds = 1, DnsOverHttps = "http://insecure/dns" }.Clamped();
        Assert.Equal(64, o.MinSegmentKB);
        Assert.Equal(1, o.Retries);
        Assert.Equal(300, o.ConnectTimeoutSeconds);
        Assert.Equal(5, o.ReadTimeoutSeconds);
        Assert.Equal("", o.DnsOverHttps);
        var t = TransferOptions.From(new NetworkOptions { MinSegmentKB = 2048 }, new HostProfile { Retries = 3, TimeoutSeconds = 20 });
        Assert.Equal(2048 * 1024L, t.MinSegment);
        Assert.Equal(3, t.Retries);
        Assert.Equal(20, t.ReadTimeoutSeconds);
    }

    [Fact]
    public void MinimumPartSizeLimitsSplitting()
    {
        Assert.Equal(8, SegmentPlanner.Plan(100L << 20, 8).Count);
        Assert.Equal(2, SegmentPlanner.Plan(100L << 20, 8, 40L << 20).Count);
        var one = SegmentPlanner.Plan(10L << 20, 1);
        Assert.NotNull(SegmentPlanner.SplitLargest(one, 1));
        Assert.Null(SegmentPlanner.SplitLargest(one, 1, 8L << 20));
    }

    [Fact]
    public void ReadsDnsOverHttpsAnswers()
    {
        const string json = "{\"Status\":0,\"Answer\":[{\"name\":\"example.com\",\"type\":5,\"TTL\":60,\"data\":\"alias.example.net.\"}," +
                            "{\"name\":\"alias.example.net\",\"type\":1,\"TTL\":120,\"data\":\"93.184.216.34\"},{\"name\":\"x\",\"type\":28,\"TTL\":45,\"data\":\"2606:2800:220:1::1\"}]}";
        var (ips, ttl) = DnsOverHttps.ParseAnswer(json);
        Assert.Equal(new[] { "93.184.216.34", "2606:2800:220:1::1" }, ips.Select(i => i.ToString()));
        Assert.Equal(45, ttl);
        Assert.Empty(DnsOverHttps.ParseAnswer("{\"Status\":3}").Addresses);
    }

    sealed class Reverse : ISecretProtector
    {
        public string? Protect(string plain) => new string(plain.Reverse().ToArray());
        public string? Unprotect(string sealedText) => new string(sealedText.Reverse().ToArray());
    }
}

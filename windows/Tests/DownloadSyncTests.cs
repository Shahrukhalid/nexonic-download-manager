using System.Net;
using System.Net.Sockets;
using System.Text;
using Nexonic.Core;
using Xunit;

/// <summary>Keeping finished files in sync with the server: when to check, what counts as a change, safe replacement.</summary>
public class DownloadSyncTests
{
    static DownloadRecord Synced(int hours = 24) => new()
    {
        Url = "https://files.test/report.csv", FileName = "report.csv", Status = DownloadStatus.Finished, SyncHours = hours,
        Size = 100, Validator = "\"v1\"",
    };

    [Fact]
    public void ChecksOnlyFinishedSyncedFilesWhenDue()
    {
        var now = new DateTime(2026, 11, 16, 12, 0, 0);
        var r = Synced(6);
        Assert.True(DownloadSync.Due(r, now));                       // never checked
        r.SyncChecked = now.AddHours(-5);
        Assert.False(DownloadSync.Due(r, now));
        r.SyncChecked = now.AddHours(-6);
        Assert.True(DownloadSync.Due(r, now));
        r.SyncChecked = now.AddHours(3);                             // the clock went back: check rather than wait for days
        Assert.True(DownloadSync.Due(r, now));
        r.SyncHours = null;
        Assert.False(DownloadSync.Due(r, now));
        var running = Synced();
        running.Status = DownloadStatus.Downloading;
        Assert.False(DownloadSync.Due(running, now));
        var video = Synced();
        video.SiteVideo = true;
        Assert.False(DownloadSync.Due(video, now));
    }

    [Fact]
    public void DecidesWhatIsAChange()
    {
        var r = Synced();
        Assert.Equal(DownloadSync.Result.Unchanged, DownloadSync.Compare(r, HttpStatusCode.NotModified, null, null));
        Assert.Equal(DownloadSync.Result.Unchanged, DownloadSync.Compare(r, HttpStatusCode.PartialContent, "\"v1\"", 999));
        Assert.Equal(DownloadSync.Result.Changed, DownloadSync.Compare(r, HttpStatusCode.PartialContent, "\"v2\"", 100));
        Assert.Equal(DownloadSync.Result.Failed, DownloadSync.Compare(r, HttpStatusCode.NotFound, null, null));
        r.Validator = null;                                          // no version from the server: the size decides
        Assert.Equal(DownloadSync.Result.Changed, DownloadSync.Compare(r, HttpStatusCode.PartialContent, null, 101));
        Assert.Equal(DownloadSync.Result.Unchanged, DownloadSync.Compare(r, HttpStatusCode.PartialContent, null, 100));
        Assert.Equal(DownloadSync.Result.Unchanged, DownloadSync.Compare(r, HttpStatusCode.OK, null, null));
    }

    [Fact]
    public void ReplacesTheOldCopyOnlyWhenTheNewOneIsComplete()
    {
        var dir = Directory.CreateTempSubdirectory("ndm-sync-").FullName;
        try
        {
            var old = Path.Combine(dir, "report.csv");
            File.WriteAllText(old, "old");
            var r = Synced();
            r.SavedPath = old;
            DownloadSync.PrepareRedownload(r);
            Assert.Equal(DownloadStatus.Queued, r.Status);
            Assert.Equal(old, r.SyncReplace);
            Assert.Null(r.Validator);
            Assert.Equal("old", File.ReadAllText(old));             // nothing is touched before the new copy is done

            var fresh = Path.Combine(dir, "report (1).csv");         // the engine saved the update beside the old copy
            File.WriteAllText(fresh, "new");
            r.SavedPath = fresh;
            r.FileName = "report (1).csv";
            r.Status = DownloadStatus.Finished;
            var now = DateTime.Now;
            Assert.True(DownloadSync.Finished(r, now));
            Assert.Equal(old, r.SavedPath);
            Assert.Equal("report.csv", r.FileName);
            Assert.Equal("new", File.ReadAllText(old));
            Assert.False(File.Exists(fresh));
            Assert.Null(r.SyncReplace);
            Assert.Equal(now, r.SyncChecked);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task AsksTheServerOnlyIfChanged()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = new List<string>();
        var server = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var buffer = new byte[4096];
                var read = await stream.ReadAsync(buffer);
                var request = Encoding.ASCII.GetString(buffer, 0, read);
                requests.Add(request);
                var answer = request.Contains("If-None-Match: \"v1\"", StringComparison.OrdinalIgnoreCase) && i == 0
                    ? "HTTP/1.1 304 Not Modified\r\nETag: \"v1\"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                    : "HTTP/1.1 206 Partial Content\r\nETag: \"v2\"\r\nContent-Range: bytes 0-0/150\r\nContent-Length: 1\r\nConnection: close\r\n\r\nx";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
            }
        });
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        var r = Synced();
        r.Url = $"http://127.0.0.1:{port}/report.csv";
        r.Cookies = "sid=1";

        var first = await DownloadSync.CheckAsync(r, http, CancellationToken.None);
        Assert.Equal(DownloadSync.Result.Unchanged, first.Result);
        var second = await DownloadSync.CheckAsync(r, http, CancellationToken.None);
        Assert.Equal(DownloadSync.Result.Changed, second.Result);
        Assert.Equal(("\"v2\"", 150L), (second.Validator, second.Size));
        await server;
        listener.Stop();
        Assert.Contains("Range: bytes=0-0", requests[0]);
        Assert.Contains("Cookie: sid=1", requests[0]);
    }

    [Fact]
    public async Task ProblemsCountAsFailedNeverThrow()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var r = Synced();
        r.Url = "http://127.0.0.1:1/nothing-listens-here";
        Assert.Equal(DownloadSync.Result.Failed, (await DownloadSync.CheckAsync(r, http, CancellationToken.None)).Result);
        r.Url = "file:///etc/passwd";
        Assert.Equal(DownloadSync.Result.Failed, (await DownloadSync.CheckAsync(r, http, CancellationToken.None)).Result);
    }
}

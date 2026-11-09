using System.Net;
using System.Net.Http.Headers;

namespace Nexonic.Core;

/// <summary>What the app needs from any running download.</summary>
public interface IDownloader
{
    DownloadRecord Record { get; }
    SpeedLimiter OwnLimiter { get; }
    event Action<DownloadRecord>? Changed;
    Task StartAsync();
    void Pause();
    void Discard();
}

/// <summary>
/// Downloads streamed video (HLS): picks the best quality, fetches the pieces in parallel, resumes after a pause or
/// restart, joins them and, when ffmpeg is installed, turns the result into one .mp4 with picture and sound.
/// </summary>
public sealed class StreamTransfer : IDownloader
{
    public DownloadRecord Record { get; }
    public SpeedLimiter OwnLimiter { get; } = new();
    public event Action<DownloadRecord>? Changed;

    readonly SpeedLimiter limiter;
    readonly int connections;
    readonly bool sortIntoCategories;
    readonly HttpClient http;
    readonly object gate = new();
    CancellationTokenSource? cts;
    DateTime lastNotify = DateTime.MinValue;

    string PartsFolder => Path.Combine(AppPaths.Parts, Record.Id.ToString("N"));
    string Piece(string track, int i) => Path.Combine(PartsFolder, $"{track}-{i}.seg");

    public StreamTransfer(DownloadRecord record, int connections, SpeedLimiter limiter, bool sortIntoCategories = false, ProxySettings? proxy = null)
    {
        Record = record;
        this.limiter = limiter;
        this.connections = Math.Clamp(connections, 1, 8);
        this.sortIntoCategories = sortIntoCategories;
        OwnLimiter.SetLimit((record.SpeedLimitKBps ?? 0) * 1024);
        var handler = Network.Handler(proxy ?? new ProxySettings());
        // Redirects are followed by hand (Network.SendAsync) so cookies and the login never reach another host.
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    }

    public Task StartAsync()
    {
        cts = new CancellationTokenSource();
        return Task.Run(() => RunAsync(cts.Token));
    }

    public void Pause()
    {
        Record.Status = DownloadStatus.Paused;
        cts?.Cancel();
        Notify(true);
    }

    public void Discard()
    {
        cts?.Cancel();
        try { Directory.Delete(PartsFolder, true); } catch { }
    }

    void Notify(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - lastNotify).TotalMilliseconds < 250) return;
        lastNotify = now;
        Changed?.Invoke(Record);
    }

    sealed class HttpFailure(int code) : Exception(code is 403 or 404 or 410
        ? $"The video link has expired (HTTP {code}). Play the video again and use Refresh Link"
        : $"The server answered HTTP {code}")
    {
        public int Code { get; } = code;
    }

    async Task<byte[]> FetchAsync(Uri url, CancellationToken ct)
    {
        var origin = new Uri(Record.Url);
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var res = await Network.SendAsync(http, url, u =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, u);
                    if (!string.IsNullOrEmpty(Record.Referrer)) req.Headers.TryAddWithoutValidation("Referer", Record.Referrer);
                    if (!string.IsNullOrEmpty(Record.UserAgent)) req.Headers.TryAddWithoutValidation("User-Agent", Record.UserAgent);
                    // Pieces often live on other hosts (CDNs): cookies and the login only go to the playlist's own server.
                    Network.AddPrivateHeaders(req, u, origin, Record.Cookies, Record.Username, Record.Password);
                    return req;
                }, HttpCompletionOption.ResponseContentRead, ct);
                if (!res.IsSuccessStatusCode) throw new HttpFailure((int)res.StatusCode);
                var data = await res.Content.ReadAsByteArrayAsync(ct);
                if (data.Length == 0) throw new IOException("An empty piece was received");
                return data;
            }
            catch (Exception) when (!ct.IsCancellationRequested && attempt < 6)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1))), ct);
            }
        }
    }

    async Task<HLS.Playlist> PlaylistAsync(Uri url, CancellationToken ct) =>
        HLS.Parse(System.Text.Encoding.UTF8.GetString(await FetchAsync(url, ct)), url);

    async Task RunAsync(CancellationToken ct)
    {
        try
        {
            Record.Status = DownloadStatus.Probing;
            Record.Error = null;
            Record.IsStream = true;
            Notify(true);
            var tracks = new List<(string Name, HLS.Media Media)>();
            var top = await PlaylistAsync(new Uri(Record.Url), ct);
            if (!top.IsMaster) tracks.Add(("video", top.Media!));
            else
            {
                var best = HLS.Best(top.Variants)!;
                var video = await PlaylistAsync(best.Url, ct);
                tracks.Add(("video", video.Media ?? throw new HLS.Failure("This link is not a video playlist")));
                if (best.AudioGroup is { } g && top.Audio.TryGetValue(g, out var a) && (await PlaylistAsync(a, ct)).Media is { } sound)
                    tracks.Add(("audio", sound));
            }
            if (tracks.Any(t => t.Media.Encryption != "NONE")) throw new HLS.Failure("This video is copy-protected and can't be saved");
            Directory.CreateDirectory(PartsFolder);
            Record.StreamTotal = tracks.Sum(t => t.Media.Segments.Count + (t.Media.InitSegment == null ? 0 : 1));
            Record.StreamDone = 0;
            Record.StreamBytes = 0;
            Record.Status = DownloadStatus.Downloading;
            Notify(true);
            var joined = new List<(string Name, HLS.Media Media, string File)>();
            foreach (var (name, media) in tracks)
            {
                var pieces = media.Segments.ToList();
                if (media.InitSegment != null) pieces.Insert(0, media.InitSegment);
                await Parallel.ForEachAsync(Enumerable.Range(0, pieces.Count),
                    new ParallelOptions { MaxDegreeOfParallelism = connections, CancellationToken = ct },
                    async (i, token) => await GetAsync(pieces[i], Piece(name, i), token));
                joined.Add((name, media, Join(name, pieces.Count)));
            }
            Finish(joined);
        }
        catch (Exception) when (ct.IsCancellationRequested) { /* paused or removed */ }
        catch (Exception e)
        {
            Record.Status = DownloadStatus.Failed;
            Record.Error = e.Message;
            Notify(true);
        }
    }

    async Task GetAsync(Uri url, string file, CancellationToken ct)
    {
        if (File.Exists(file)) { Count(new FileInfo(file).Length); return; } // already downloaded before a pause
        var data = await FetchAsync(url, ct);
        await File.WriteAllBytesAsync(file + ".tmp", data, ct);
        File.Move(file + ".tmp", file, true);
        Count(data.Length);
        var a = limiter.Consume(data.Length);
        var b = OwnLimiter.Consume(data.Length);
        var wait = a > b ? a : b;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
    }

    void Count(long bytes)
    {
        lock (gate)
        {
            Record.StreamDone = (Record.StreamDone ?? 0) + 1;
            Record.StreamBytes = (Record.StreamBytes ?? 0) + bytes;
        }
        Notify();
    }

    string Join(string track, int n)
    {
        var output = Path.Combine(PartsFolder, track + ".joined");
        using var o = new FileStream(output, FileMode.Create);
        for (var i = 0; i < n; i++)
        {
            using var input = File.OpenRead(Piece(track, i));
            input.CopyTo(o);
        }
        return output;
    }

    static string Ext(string name, HLS.Media m)
    {
        if (m.IsFragmentedMp4) return name == "audio" ? ".m4a" : ".mp4";
        var e = Path.GetExtension(m.Segments[0].AbsolutePath).ToLowerInvariant();
        return name == "audio" && e == ".aac" ? ".aac" : ".ts";
    }

    void Finish(List<(string Name, HLS.Media Media, string File)> joined)
    {
        var folder = FileCategories.TargetFolder(Record.Folder, "video.mp4", sortIntoCategories);
        Directory.CreateDirectory(folder);
        var baseName = Path.GetFileNameWithoutExtension(Record.FileName);
        var mp4 = FileNames.Unique(baseName + ".mp4", folder);
        string saved;
        var first = joined[0];
        if ((joined.Count > 1 || Ext(first.Name, first.Media) != ".mp4") && FFmpeg.Mux(joined.Select(j => j.File).ToList(), mp4))
            saved = mp4;
        else
        {
            saved = FileNames.Unique(baseName + Ext(first.Name, first.Media), folder);
            File.Move(first.File, saved);
            if (joined.Count > 1)
            {
                File.Move(joined[1].File, FileNames.Unique(baseName + " (sound)" + Ext(joined[1].Name, joined[1].Media), folder));
                Record.Error = "Picture and sound were saved as two files.";
            }
        }
        try { Directory.Delete(PartsFolder, true); } catch { }
        Record.SavedPath = saved;
        Record.FileName = Path.GetFileName(saved);
        Record.Size = new FileInfo(saved).Length;
        Record.Status = DownloadStatus.Finished;
        Record.Finished = DateTime.Now;
        Notify(true);
        http.Dispose();
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nexonic.Core;

/// <summary>Playlists and channels: one link that stands for many videos (YouTube, Vimeo, SoundCloud, Bandcamp…).</summary>
public static class Playlists
{
    public sealed record Entry(string Url, string Title, double? Duration);
    public sealed record Result(string Title, List<Entry> Entries);

    /// <summary>The most videos read from one playlist or channel.</summary>
    public const int MaxEntries = 500;

    /// <summary>True for links that are a list of videos rather than one video.</summary>
    public static bool IsPlaylist(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
        var host = u.Host.ToLowerInvariant();
        var path = u.AbsolutePath.TrimEnd('/');
        var query = u.Query;
        bool On(string h) => host == h || host.EndsWith("." + h);
        if (On("youtube.com"))
        {
            if (path == "/playlist" && query.Contains("list=")) return true;
            if (path == "/watch") return false;  // one video, even when it is part of a list
            return path.StartsWith("/@") || path.StartsWith("/channel/") || path.StartsWith("/c/") || path.StartsWith("/user/");
        }
        if (On("vimeo.com")) return path.StartsWith("/showcase/") || path.StartsWith("/channels/") || path.Contains("/albums/");
        if (On("soundcloud.com")) return path.Contains("/sets/");
        if (On("bandcamp.com")) return path.StartsWith("/album/");
        if (On("dailymotion.com")) return path.StartsWith("/playlist/");
        return false;
    }

    /// <summary>A channel's home page lists its tabs; its Videos tab lists the videos.</summary>
    public static string Normalize(string url)
    {
        var t = url.Trim();
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u)) return t;
        var host = u.Host.ToLowerInvariant();
        var path = u.AbsolutePath.TrimEnd('/');
        var isYouTube = host == "youtube.com" || host.EndsWith(".youtube.com");
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var channelHome = parts.Length == 1 && parts[0].StartsWith("@") || parts.Length == 2 && parts[0] is "channel" or "c" or "user";
        return isYouTube && channelHome ? $"{u.Scheme}://{u.Host}{path}/videos" : t;
    }

    /// <summary>The command that lists a playlist's videos without downloading them.</summary>
    public static List<string> Arguments(string url, string? quickJs, int max = MaxEntries)
    {
        var a = new List<string> { "--ignore-config", "--flat-playlist", "-J", "--playlist-end", max.ToString(CultureInfo.InvariantCulture),
            "--no-colors", "--js-runtimes", "node", "--encoding", "utf-8" };
        if (quickJs != null) a.AddRange(new[] { "--js-runtimes", $"quickjs:{quickJs}" });
        a.Add("--");
        a.Add(Normalize(url));
        return a;
    }

    /// <summary>Reads the videos from the JSON printed by Arguments.</summary>
    public static Result? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var o = doc.RootElement;
            if (o.ValueKind != JsonValueKind.Object) return null;
            static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var list = new List<Entry>();
            var seen = new HashSet<string>();
            if (o.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
                foreach (var e in entries.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var url = Str(e, "url") ?? Str(e, "webpage_url");
                    var id = Str(e, "id");
                    if ((url == null || !url.StartsWith("http")) && id != null && (Str(e, "ie_key") ?? "").StartsWith("Youtube"))
                        url = "https://www.youtube.com/watch?v=" + id;
                    if (url == null || !url.StartsWith("http") || !seen.Add(url)) continue;
                    double? duration = e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null;
                    list.Add(new Entry(url, Str(e, "title") ?? url, duration));
                }
            return new Result(Str(o, "title") ?? "", list);
        }
        catch { return null; }
    }

    /// <summary>Runs the video engine to list a playlist's videos (up to two minutes). Throws with a readable reason.</summary>
    public static Result Read(string url, ProxySettings? proxy, CancellationToken cancel = default)
    {
        SiteVideoTools.Prepare();
        if (!File.Exists(SiteVideoTools.YtDlp)) throw new InvalidOperationException("The video engine is missing. Reinstall the app");
        var (args, configFile) = SiteVideo.Prepare(Arguments(url, SiteVideoTools.QuickJs), proxy);
        using var cleanup = new ConfigCleanup(configFile);
        var psi = new ProcessStartInfo(SiteVideoTools.YtDlp)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("The video engine could not start");
        var output = p.StandardOutput.ReadToEndAsync();
        var errors = p.StandardError.ReadToEndAsync();
        using (cancel.Register(() => { try { p.Kill(true); } catch { } }))
            if (!p.WaitForExit(120_000)) { try { p.Kill(true); } catch { } throw new TimeoutException("The site took too long to list the videos"); }
        cancel.ThrowIfCancellationRequested();
        var json = output.Wait(5_000) ? output.Result : "";
        if (p.ExitCode != 0 || Parse(json) is not { } r)
            throw new InvalidOperationException(SiteVideo.Error(errors.Wait(2_000) ? errors.Result : ""));
        if (r.Entries.Count == 0) throw new InvalidOperationException("No videos were found at this link");
        return r;
    }
}

/// <summary>Subtitles saved inside downloaded videos (Downloads menu; off by default).</summary>
public static class VideoSubtitles
{
    /// <summary>Set by the app from its settings.</summary>
    public static bool Enabled { get; set; }

    /// <summary>The subtitles to fetch: the computer's language and English (both as written by people, not machine captions).</summary>
    public static string Languages(CultureInfo? culture = null)
    {
        var l = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName.ToLowerInvariant();
        return l is "en" or "iv" or "" ? "en.*" : $"{l}.*,en.*";
    }

    /// <summary>A failed subtitle download must never fail the video, so errors become warnings.</summary>
    public static string[] Arguments(string? languages = null) =>
        new[] { "--write-subs", "--sub-langs", languages ?? Languages(), "--embed-subs", "--ignore-errors" };
}

/// <summary>Finished videos and music for the Media Library window, with preview pictures made by the bundled ffmpeg.</summary>
public static class MediaLibrary
{
    public sealed record Item(DownloadRecord Record, string Path, FileCategory Kind);

    public static string Thumbnails => System.IO.Path.Combine(AppPaths.Support, "Thumbnails");

    /// <summary>Finished video and music files that are still on disk, newest first.</summary>
    public static List<Item> Items(IEnumerable<DownloadRecord> records, string search = "", FileCategory? only = null) =>
        records.Where(r => r.Status == DownloadStatus.Finished && r.SavedPath is { } p && File.Exists(p))
            .Select(r => new Item(r, r.SavedPath!, FileCategories.Of(System.IO.Path.GetFileName(r.SavedPath!))))
            .Where(i => i.Kind is FileCategory.Video or FileCategory.Music && (only == null || i.Kind == only))
            .Where(i => search.Length == 0 || i.Record.FileName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Record.Finished ?? i.Record.Added)
            .ToList();

    /// <summary>Where a video's preview picture is kept (named after the file and its size, so a new file gets a new one).</summary>
    public static string ThumbnailPath(string file)
    {
        var info = new FileInfo(file);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}")))[..24];
        return System.IO.Path.Combine(Thumbnails, key + ".jpg");
    }

    /// <summary>The ffmpeg command for one preview picture, 4 seconds in (the start is often black).</summary>
    public static List<string> ThumbnailArguments(string file, string output) =>
        new() { "-hide_banner", "-loglevel", "error", "-y", "-ss", "4", "-i", file, "-frames:v", "1", "-vf", "scale=320:-2", output };

    /// <summary>Makes the preview picture once and returns it, or null (no ffmpeg, not a video, or too slow).</summary>
    public static string? Thumbnail(string file)
    {
        try
        {
            var path = ThumbnailPath(file);
            if (File.Exists(path)) return path;
            if (FFmpeg.Path is not { } ffmpeg) return null;
            Directory.CreateDirectory(Thumbnails);
            var psi = new ProcessStartInfo(ffmpeg) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true };
            foreach (var a in ThumbnailArguments(file, path)) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(15_000)) { try { p.Kill(true); } catch { } return null; }
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
            // Shorter than 4 seconds: take the first picture instead.
            psi.ArgumentList.Clear();
            foreach (var a in ThumbnailArguments(file, path).Where((_, i) => i is not (4 or 5))) psi.ArgumentList.Add(a);
            using var q = Process.Start(psi)!;
            _ = q.StandardError.ReadToEndAsync();
            q.WaitForExit(15_000);
            return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
        }
        catch { return null; }
    }
}

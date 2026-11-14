using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexonic.Core;

/// <summary>Options → Extras (added in 0.32.0): what happens around a finished download.</summary>
public sealed class FinishOptions
{
    /// <summary>Rename finished files by this pattern ("" = keep the name). See <see cref="RenameMask"/>.</summary>
    public string RenameMask { get; set; } = "";
    /// <summary>A web address told when a download finishes or fails (for a phone notification), "" = none.</summary>
    public string NotifyUrl { get; set; } = "";
    /// <summary>The small always-on-top window that takes dropped links.</summary>
    public bool DropTarget { get; set; }
    /// <summary>Video downloads: also save the video's picture next to it.</summary>
    public bool SaveThumbnail { get; set; }
    /// <summary>Video downloads: cut sponsor segments, from the community list of marked segments.</summary>
    public bool SkipSponsors { get; set; }

    public List<string> VideoArguments()
    {
        var a = new List<string>();
        if (SaveThumbnail) a.Add("--write-thumbnail");
        if (SkipSponsors) a.AddRange(new[] { "--sponsorblock-remove", "sponsor" });
        return a;
    }
}

/// <summary>File names from a pattern: {name} {ext} {site} {date} {time} {category}. The extension is kept even when the
/// pattern leaves it out; characters a file name can't hold are replaced.</summary>
public static class RenameMask
{
    public static string? Apply(string? mask, string fileName, string url, DateTime date, string category)
    {
        mask = (mask ?? "").Trim();
        if (mask.Length == 0 || !mask.Contains('{')) return null;
        var ext = Path.GetExtension(fileName).TrimStart('.');
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var site = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";
        if (site.StartsWith("www.")) site = site[4..];
        var values = new Dictionary<string, string>
        {
            ["{name}"] = stem, ["{ext}"] = ext, ["{site}"] = site, ["{category}"] = category,
            ["{date}"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["{time}"] = date.ToString("HH-mm", CultureInfo.InvariantCulture),
        };
        var output = mask;
        foreach (var (k, v) in values) output = Regex.Replace(output, Regex.Escape(k), v.Replace("$", "$$"), RegexOptions.IgnoreCase);
        output = Clean(output);
        if (output.Length == 0) return null;
        if (ext.Length > 0 && !mask.Contains("{ext}", StringComparison.OrdinalIgnoreCase)) output += "." + ext;
        return output == fileName ? null : output;
    }

    /// <summary>Renames a finished file by the pattern (a free name in its folder); torrents keep their names.</summary>
    public static bool RenameFinished(DownloadRecord r, string? mask)
    {
        if (r.Torrent != null || r.SavedPath is not { } path || !File.Exists(path)) return false;
        if (Apply(mask, r.FileName, r.Url, r.Finished ?? DateTime.Now, r.Category.ToString()) is not { } name) return false;
        var target = FileNames.Unique(name, Path.GetDirectoryName(path) ?? "");
        try { File.Move(path, target); } catch { return false; }
        r.SavedPath = target;
        r.FileName = Path.GetFileName(target);
        return true;
    }

    static string Clean(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append("/\\:*?\"<>|".Contains(c) || char.IsControl(c) ? '-' : c);
        var r = sb.ToString().Trim(' ', '.');
        return r.Length > 200 ? r[..200] : r;
    }
}

/// <summary>The message sent to <see cref="FinishOptions.NotifyUrl"/>: plain text, so a push service shows it as the
/// notification itself. Only https, or plain http inside the home network.</summary>
public static class FinishNotice
{
    public static HttpRequestMessage? Request(string? address, string fileName, bool ok)
    {
        if (!Uri.TryCreate((address ?? "").Trim(), UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttps && !(u.Scheme == Uri.UriSchemeHttp && IsLocal(u.Host))) return null;
        var r = new HttpRequestMessage(HttpMethod.Post, u) { Content = new StringContent((ok ? "Finished: " : "Failed: ") + fileName, Encoding.UTF8, "text/plain") };
        r.Headers.TryAddWithoutValidation("Title", ok ? "Download finished" : "Download failed");
        return r;
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static void Send(string? address, string fileName, bool ok)
    {
        if (Request(address, fileName, ok) is not { } r) return;
        _ = Task.Run(async () => { try { using var _ = await Http.SendAsync(r); } catch { } finally { r.Dispose(); } });
    }

    static bool IsLocal(string host)
    {
        var h = host.ToLowerInvariant();
        if (h == "localhost" || h.EndsWith(".local") || h.StartsWith("192.168.") || h.StartsWith("10.") || h.StartsWith("127.")) return true;
        var p = h.Split('.');
        return p.Length == 4 && p[0] == "172" && int.TryParse(p[1], out var b) && b is >= 16 and <= 31;
    }
}

/// <summary>Play while downloading: how much of the file is there from its start, without gaps.</summary>
public static class PartialPlay
{
    public const long MinimumBytes = 4L << 20;
    static readonly HashSet<string> Media = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".m4v", ".mkv", ".webm", ".mov", ".avi", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wav", ".ts" };

    public static bool IsMedia(string fileName) => Media.Contains(Path.GetExtension(fileName));

    public static long Prefix(IEnumerable<Segment> segments)
    {
        long end = 0;
        foreach (var s in segments.OrderBy(s => s.Start))
        {
            if (s.Start > end) break;
            end = Math.Max(end, s.Start + s.Written);
            if (s.Written < s.End - s.Start + 1) break;
        }
        return end;
    }

    public static bool Ready(DownloadRecord r) =>
        IsMedia(r.FileName) && r.WorkFile != null && r.Status != DownloadStatus.Finished
        && Prefix(r.Segments) >= Math.Min(MinimumBytes, Math.Max(1, (r.Size ?? MinimumBytes) / 50));
}

/// <summary>Video tools: a finished video as .mp4, or its sound alone, without re-encoding (no quality is lost).</summary>
public static class MediaTools
{
    public static List<string> RemuxArguments(string input, string output) =>
        new() { "-hide_banner", "-loglevel", "error", "-y", "-i", input, "-map", "0:v:0?", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", output };

    public static List<string> AudioArguments(string input, string output) =>
        new() { "-hide_banner", "-loglevel", "error", "-y", "-i", input, "-vn", "-map", "0:a:0", "-c", "copy", output };

    public static string AudioExtension(string codec) => codec.ToLowerInvariant() switch
    {
        "aac" or "alac" => "m4a", "opus" => "opus", "mp3" => "mp3", "vorbis" => "ogg", "flac" => "flac", _ => "mka",
    };

    public static string Output(string input, string ext)
    {
        var dir = Path.GetDirectoryName(input) ?? "";
        var stem = Path.GetFileNameWithoutExtension(input);
        var path = Path.Combine(dir, stem + "." + ext);
        for (var n = 2; File.Exists(path) || path == input; n++) path = Path.Combine(dir, $"{stem} ({n}).{ext}");
        return path;
    }

    /// <summary>Saves the .mp4 ("mp4") or the sound alone ("audio") beside the file; returns the new file, or null.</summary>
    public static async Task<string?> ConvertAsync(string ffmpeg, string input, string kind)
    {
        string output;
        List<string> args;
        if (kind == "mp4") { output = Output(input, "mp4"); args = RemuxArguments(input, output); }
        else { output = Output(input, AudioExtension(await AudioCodecAsync(ffmpeg, input))); args = AudioArguments(input, output); }
        try
        {
            var psi = new ProcessStartInfo(ffmpeg) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode == 0 && File.Exists(output)) return output;
        }
        catch { }
        try { File.Delete(output); } catch { }
        return null;
    }

    static async Task<string> AudioCodecAsync(string ffmpeg, string input)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpeg) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true };
            psi.ArgumentList.Add("-hide_banner"); psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(input);
            using var p = Process.Start(psi)!;
            var text = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            var m = Regex.Match(text, @"Audio: ([a-z0-9]+)");
            return m.Success ? m.Groups[1].Value : "";
        }
        catch { return ""; }
    }
}

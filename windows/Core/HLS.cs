using System.Diagnostics;

namespace Nexonic.Core;

/// <summary>Streamed video sent as a playlist of small pieces (HLS, .m3u8).</summary>
public static class HLS
{
    public sealed record Variant(Uri Url, long Bandwidth, string? Resolution, string? AudioGroup)
    {
        public long Pixels
        {
            get
            {
                var p = (Resolution ?? "").ToLowerInvariant().Split('x');
                return p.Length == 2 && long.TryParse(p[0], out var w) && long.TryParse(p[1], out var h) ? w * h : 0;
            }
        }
    }

    public sealed class Media
    {
        public List<Uri> Segments { get; } = new();
        public Uri? InitSegment { get; set; }
        /// <summary>"NONE" or the encryption method the stream uses (protected streams are not saved).</summary>
        public string Encryption { get; set; } = "NONE";
        public bool IsFragmentedMp4 => InitSegment != null;
    }

    public sealed class Playlist
    {
        public List<Variant> Variants { get; } = new();
        public Dictionary<string, Uri> Audio { get; } = new();
        public Media? Media { get; set; }
        public bool IsMaster => Media == null;
    }

    public sealed class Failure(string message) : Exception(message);

    public static Dictionary<string, string> Attributes(string line)
    {
        var result = new Dictionary<string, string>();
        var colon = line.IndexOf(':');
        if (colon < 0) return result;
        var key = new System.Text.StringBuilder();
        var value = new System.Text.StringBuilder();
        bool inKey = true, quoted = false;
        foreach (var ch in line[(colon + 1)..])
        {
            if (inKey) { if (ch == '=') inKey = false; else key.Append(ch); }
            else if (ch == '"') quoted = !quoted;
            else if (ch == ',' && !quoted)
            {
                result[key.ToString().Trim().ToUpperInvariant()] = value.ToString();
                key.Clear(); value.Clear(); inKey = true;
            }
            else value.Append(ch);
        }
        if (key.Length > 0) result[key.ToString().Trim().ToUpperInvariant()] = value.ToString();
        return result;
    }

    public static Playlist Parse(string text, Uri baseUri)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0 || !lines[0].StartsWith("#EXTM3U")) throw new Failure("This link is not a video playlist");
        Uri? Resolve(string s) => Uri.TryCreate(baseUri, s, out var u) ? u : null;
        var p = new Playlist();
        if (lines.Any(l => l.StartsWith("#EXT-X-STREAM-INF")))
        {
            Dictionary<string, string>? pending = null;
            foreach (var line in lines)
            {
                if (line.StartsWith("#EXT-X-STREAM-INF")) pending = Attributes(line);
                else if (line.StartsWith("#EXT-X-MEDIA"))
                {
                    var a = Attributes(line);
                    if (a.GetValueOrDefault("TYPE") == "AUDIO" && a.TryGetValue("GROUP-ID", out var g) && a.TryGetValue("URI", out var uri)
                        && Resolve(uri) is { } u && (!p.Audio.ContainsKey(g) || a.GetValueOrDefault("DEFAULT") == "YES"))
                        p.Audio[g] = u;
                }
                else if (!line.StartsWith('#') && pending != null && Resolve(line) is { } u)
                {
                    p.Variants.Add(new Variant(u, long.TryParse(pending.GetValueOrDefault("BANDWIDTH"), out var b) ? b : 0,
                        pending.GetValueOrDefault("RESOLUTION"), pending.GetValueOrDefault("AUDIO")));
                    pending = null;
                }
            }
            if (p.Variants.Count == 0) throw new Failure("The video playlist is empty");
            return p;
        }
        var m = new Media();
        foreach (var line in lines)
        {
            if (line.StartsWith("#EXT-X-KEY") || line.StartsWith("#EXT-X-SESSION-KEY"))
            {
                var method = Attributes(line).GetValueOrDefault("METHOD") ?? "NONE";
                if (method != "NONE") m.Encryption = method;
            }
            else if (line.StartsWith("#EXT-X-MAP")) m.InitSegment = Attributes(line).TryGetValue("URI", out var i) ? Resolve(i) : null;
            else if (!line.StartsWith('#') && Resolve(line) is { } u) m.Segments.Add(u);
        }
        if (m.Segments.Count == 0) throw new Failure("The video playlist is empty");
        p.Media = m;
        return p;
    }

    /// <summary>The best picture: most pixels, then the highest bit rate.</summary>
    public static Variant? Best(IEnumerable<Variant> variants) =>
        variants.OrderByDescending(v => v.Pixels).ThenByDescending(v => v.Bandwidth).FirstOrDefault();

    public static bool IsPlaylistUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The ffmpeg that ships next to the app (or one on the PATH), used to turn saved streams into .mp4 files.</summary>
public static class FFmpeg
{
    public static string? Path
    {
        get
        {
            if (EnginePaths.FFmpeg.Length > 0) return EnginePaths.FFmpegFile;
            var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            var dirs = new List<string> { AppContext.BaseDirectory };
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
            return dirs.Select(d => System.IO.Path.Combine(d, name)).FirstOrDefault(File.Exists);
        }
    }

    public static bool Mux(IReadOnlyList<string> inputs, string output)
    {
        if (Path is not { } exe) return false;
        try
        {
            var psi = new ProcessStartInfo(exe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in new[] { "-y", "-loglevel", "error" }) psi.ArgumentList.Add(a);
            foreach (var i in inputs) { psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(i); }
            for (var n = 0; n < inputs.Count; n++) { psi.ArgumentList.Add("-map"); psi.ArgumentList.Add(n.ToString()); }
            foreach (var a in new[] { "-c", "copy", "-movflags", "+faststart", output }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}

/// <summary>
/// Options → Video: the user's own yt-dlp and FFmpeg programs instead of the ones the app downloads and finds. Empty =
/// automatic. A custom yt-dlp is never updated or replaced by the app.
/// </summary>
public static class EnginePaths
{
    public static volatile string YtDlp = "";
    public static volatile string FFmpeg = "";

    public static string? YtDlpFile => Usable(YtDlp);
    public static string? FFmpegFile => Usable(FFmpeg);

    static string? Usable(string path) => path.Length > 0 && System.IO.Path.IsPathFullyQualified(path) && File.Exists(path) ? path : null;

    /// <summary>Null when the path can be used (or is empty), else what is wrong with it.</summary>
    public static string? Problem(string? path, string program)
    {
        var p = (path ?? "").Trim();
        if (p.Length == 0) return null;
        if (!System.IO.Path.IsPathFullyQualified(p)) return $"The {program} path must be a full path to the program.";
        if (!File.Exists(p)) return $"There is no {program} program at {p}.";
        return null;
    }

    /// <summary>The first line the program prints for --version / -version, or null when it does not run.</summary>
    public static string? Version(string path, bool ffmpeg)
    {
        try
        {
            var psi = new ProcessStartInfo(path) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add(ffmpeg ? "-version" : "--version");
            using var p = Process.Start(psi)!;
            var line = p.StandardOutput.ReadLine();
            if (!p.WaitForExit(10_000)) { try { p.Kill(true); } catch { } return null; }
            return p.ExitCode == 0 && !string.IsNullOrWhiteSpace(line) ? line.Trim() : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return null; }
    }
}

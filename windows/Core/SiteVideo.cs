using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexonic.Core;

/// <summary>
/// Videos from YouTube, Instagram, Facebook, X, TikTok and 1,000+ other sites, saved with the bundled yt-dlp
/// in the best quality (picture and sound joined by the bundled ffmpeg).
/// </summary>
public static class SiteVideo
{
    public static readonly string[] Hosts =
    {
        "youtube.com", "youtu.be", "youtube-nocookie.com", "instagram.com", "facebook.com", "fb.watch", "x.com", "twitter.com",
        "tiktok.com", "vimeo.com", "dailymotion.com", "reddit.com", "redd.it", "twitch.tv", "soundcloud.com", "linkedin.com",
        "pinterest.com", "threads.net", "bilibili.com", "rumble.com", "streamable.com", "bandcamp.com", "mixcloud.com",
    };

    public static bool IsSupported(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var host = u.Host.ToLowerInvariant();
        return Hosts.Any(h => host == h || host.EndsWith("." + h));
    }

    public sealed record Progress(long Downloaded, long? Total, double? Speed, string? Part = null, string? Label = null);

    /// <summary>
    /// Adds up the progress of the pieces of one video (picture and sound are downloaded one after the other).
    /// Each piece only ever grows, so the jumps of fragmented downloads can't inflate the total.
    /// </summary>
    public sealed class PartProgress
    {
        readonly Dictionary<string, long> done = new();
        readonly Dictionary<string, long> totals = new();
        readonly Dictionary<string, string> labels = new();
        readonly List<string> order = new();

        /// <summary>Each piece in the order it started, for the progress window.</summary>
        public List<VideoPart> Pieces => order.Select((k, i) => new VideoPart
        {
            Name = labels.GetValueOrDefault(k) ?? (order.Count > 1 ? $"Part {i + 1}" : "Video"),
            Done = done.GetValueOrDefault(k),
            Total = totals.TryGetValue(k, out var t) ? t : null,
        }).ToList();

        public (long Done, long? Total) Add(Progress p)
        {
            var key = p.Part ?? "";
            if (!done.ContainsKey(key)) order.Add(key);
            if (p.Label != null) labels[key] = p.Label;
            done[key] = Math.Max(done.GetValueOrDefault(key), p.Downloaded);
            if (p.Total is > 0) totals[key] = p.Total.Value;
            var sum = done.Values.Sum();
            var known = done.Keys.Sum(k => Math.Max(totals.GetValueOrDefault(k), done[k]));
            return (sum, totals.Count == 0 ? null : Math.Max(known, sum));
        }
    }

    const string ProgressTag = "NDMP";
    const string FileTag = "NDMF";

    static double? Num(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    public static Progress? ParseProgress(string line)
    {
        var p = line.Split(' ');
        if (p.Length < 5 || p[0] != ProgressTag || Num(p[1]) is not { } done) return null;
        var total = Num(p[2]) ?? Num(p[3]);
        string? label = null;
        if (p.Length >= 8) label = p[7] == "none" ? "Sound" : "Picture" + (int.TryParse(p[6], out var h) ? $" {h}p" : "");
        return new Progress((long)done, total is { } t ? (long)t : null, Num(p[4]), p.Length >= 6 && p[5] != "NA" ? p[5] : null, label);
    }

    /// <summary>What yt-dlp is doing after the pieces are downloaded ("[Merger] Merging formats into …").</summary>
    public static string? Step(string line)
    {
        if (line.StartsWith("[Merger]")) return "Joining picture and sound…";
        if (line.StartsWith("[ExtractAudio]")) return "Converting the sound…";
        if (line.StartsWith("[Fixup") || line.StartsWith("[VideoConvertor]") || line.StartsWith("[VideoRemuxer]")) return "Finishing the file…";
        if (line.StartsWith("[EmbedSubtitle]")) return "Adding subtitles…";
        return null;
    }

    /// <summary>The video's real name from "[download] Destination: …\Title.f137.mp4" (the piece number is dropped).</summary>
    public static string? DestinationName(string line)
    {
        const string tag = "[download] Destination: ";
        if (!line.StartsWith(tag)) return null;
        var name = Path.GetFileName(line[tag.Length..]);
        name = Regex.Replace(name, @"\.f[0-9A-Za-z_-]+(\.[^.]+)$", "$1");
        return name.Length > 0 ? name : null;
    }

    public static string? SavedFile(string line) => line.StartsWith(FileTag + " ") ? line[(FileTag.Length + 1)..] : null;

    public static string Error(string text)
    {
        var line = text.Split('\n').LastOrDefault(l => l.Contains("ERROR:")) ?? "";
        var msg = line.Split("ERROR:").Last().Trim();
        msg = Regex.Replace(msg, @"^\[[^\]]+\] [^:]+: ", "");
        if (msg.StartsWith(EnginePlugins.Marker)) return msg[EnginePlugins.Marker.Length..].Trim();  // already written for people
        // The engine's advice to report bugs on its own site and to update it is not for the app's users (the app updates it).
        msg = Regex.Replace(msg, @";?\s*please report this issue on.*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline).Trim();
        msg = Regex.Replace(msg, @"\s*\(caused by .*$", "", RegexOptions.Singleline).Trim();
        if (msg.Length == 0) return "The video could not be downloaded";
        if (msg.Contains("Unable to extract", StringComparison.OrdinalIgnoreCase))
            return "This site changed its pages, so they can't be read right now. Nexonic updates its video engine automatically; try again later";
        if (msg.Contains("Unable to connect to proxy", StringComparison.OrdinalIgnoreCase) || msg.Contains("Unable to download", StringComparison.OrdinalIgnoreCase) && msg.Contains("Errno", StringComparison.OrdinalIgnoreCase))
            return "The site could not be reached. Check the connection (and the proxy, if one is set) and try again";
        if (msg.Contains("DRM", StringComparison.OrdinalIgnoreCase)) return "This video is copy-protected and can't be saved";
        if (msg.Contains("anti-bot", StringComparison.OrdinalIgnoreCase) || msg.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase))
            return "This site checks that a real browser is visiting, so the app can't open the page by itself. Play the video in your browser, then click Download this video: the browser hands the video over with your session";
        string[] signIn = { "sign in", "sign-in", "login", "log in", "logged-in", "logged in", "account credentials", "--cookies" };
        if (signIn.Any(s => msg.Contains(s, StringComparison.OrdinalIgnoreCase)))
            return "The site only lets signed-in users download this video. Open it in your browser while signed in and click Download this video (your sign-in is sent along), or enter your account under Properties → Login";
        return Regex.Replace(msg, @"yt[-_]?dlp", "the video engine", RegexOptions.IgnoreCase);  // the engine's own name stays out of the app
    }

    public sealed record Info(string Title, string Ext, long? Size);

    /// <summary>Video details older than this are looked up again (the links inside them expire).</summary>
    public static readonly TimeSpan InfoMaxAge = TimeSpan.FromHours(2);

    /// <summary>
    /// The command that looks a video up without downloading it (prints its details as JSON). The saved JSON is handed to
    /// Arguments(infoJson), so the download starts right away instead of looking the video up again.
    /// </summary>
    public static List<string> InfoArguments(string url, string? quickJs, bool audioOnly, string? referrer, string? cookies,
        string? userAgent, string? username, string? password, int? maxHeight = null)
    {
        var a = new List<string> { "--ignore-config", "--no-playlist", "--no-colors", "-J", "--js-runtimes", "node", "--encoding", "utf-8" };
        if (quickJs != null) a.AddRange(new[] { "--js-runtimes", $"quickjs:{quickJs}" });
        a.AddRange(Format(audioOnly, maxHeight));
        if (!string.IsNullOrEmpty(referrer)) a.AddRange(new[] { "--referer", referrer });
        if (!string.IsNullOrEmpty(userAgent)) a.AddRange(new[] { "--user-agent", userAgent });
        if (!string.IsNullOrEmpty(cookies)) a.AddRange(new[] { "--add-header", $"Cookie:{cookies}" });
        if (!string.IsNullOrEmpty(username)) a.AddRange(new[] { "--username", username, "--password", password ?? "" });
        a.Add("--");
        a.Add(url);
        return a;
    }

    /// <summary>
    /// Which picture and sound to fetch. Highest resolution and frame rate first (no higher than maxHeight when one was
    /// chosen); at equal quality prefer formats every player opens.
    /// </summary>
    /// <summary>MaxHeight value for "Original": the best picture and sound the site has, in their own formats (no preference
    /// for formats every player opens), kept in an MKV when they can't share an MP4.</summary>
    public const int OriginalQuality = -1;

    public static string[] Format(bool audioOnly, int? maxHeight)
    {
        if (audioOnly) return new[] { "-f", "ba[ext=m4a]/ba/b" };
        if (maxHeight == OriginalQuality) return new[] { "-f", "bv*+ba/b", "-S", "res,fps,hdr:12", "--merge-output-format", "mp4/mkv" };
        var res = maxHeight is int h && h > 0 ? $"res:{h}" : "res";
        // A chosen quality is always saved as MP4 (the container every app opens); "Original" keeps MKV for codecs MP4 can't hold.
        return new[] { "-f", "bv*+ba/b", "-S", $"{res},fps,hdr:12,vcodec:avc,acodec:m4a", "--merge-output-format", "mp4" };
    }

    /// <summary>
    /// Sites whose videos all have the same title ("Video by name"): the file is named after the caption instead, with the
    /// video's id so two videos never share a name (the engine would skip the second as "already downloaded").
    /// </summary>
    public static string[] Naming(string url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";
        string[] captioned = { "instagram.com", "facebook.com", "fb.watch", "x.com", "twitter.com", "snapchat.com", "threads.net", "threads.com" };
        return captioned.Any(d => host == d || host.EndsWith("." + d))
            ? new[] { "--parse-metadata", "%(description,title)s:(?s)(?P<title>.+)", "-o", "%(title).120B [%(id)s].%(ext)s" }
            : new[] { "-o", "%(title).150B.%(ext)s" };
    }

    /// <summary>One picture quality a video is offered in. Height is the picture's shorter side (1080 for 1080p, also for upright videos); Size is picture and sound together, approximate.</summary>
    public sealed record Quality(int Height, long? Size);

    /// <summary>The qualities a video is offered in (best first) and the size of its sound alone.</summary>
    public sealed record Choices(string Title, List<Quality> Video, long? SoundSize);

    /// <summary>Reads the qualities on offer from the JSON printed by InfoArguments (the video's own list of formats).</summary>
    public static Choices? ParseChoices(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var o = doc.RootElement;
            if (o.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            static double? Num(System.Text.Json.JsonElement e, string k) =>
                e.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : null;
            static string? Str(System.Text.Json.JsonElement e, string k) =>
                e.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            var duration = Num(o, "duration") ?? 0;
            long? Size(System.Text.Json.JsonElement f)
            {
                if ((Num(f, "filesize") ?? Num(f, "filesize_approx")) is double s && s > 0) return (long)s;
                if (Num(f, "tbr") is double t && t > 0 && duration > 0) return (long)(t * 125 * duration);  // kbit/s × seconds
                return null;
            }
            var formats = o.TryGetProperty("formats", out var fs) && fs.ValueKind == System.Text.Json.JsonValueKind.Array
                ? fs.EnumerateArray().ToList() : new List<System.Text.Json.JsonElement> { o };
            var best = new Dictionary<int, (double Rank, long? Size, bool WithSound)>();
            double soundRank = -1;
            long? soundSize = null;
            foreach (var f in formats)
            {
                if (f.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                var v = Str(f, "vcodec");
                var a = Str(f, "acodec");
                var hasSound = a != null && a != "none";
                if (v == "none")  // sound only (storyboard pictures have neither and are skipped)
                {
                    if (!hasSound) continue;
                    var r = (a!.StartsWith("mp4a") ? 1e6 : 0) + (Num(f, "abr") ?? Num(f, "tbr") ?? 0);
                    if (r > soundRank) { soundRank = r; soundSize = Size(f); }
                    continue;
                }
                int h = (int)(Num(f, "height") ?? 0), w = (int)(Num(f, "width") ?? 0);
                var side = w > 0 && h > 0 ? Math.Min(w, h) : h;
                if (side < 100) continue;
                // The format the download picks at this height, in the same order as Format: frame rate, HDR, widely
                // playable (H.264), then the highest bitrate.
                var hdr = (Str(f, "dynamic_range") ?? "SDR") != "SDR" ? 1.0 : 0;
                var rank = Math.Round(Num(f, "fps") ?? 0) * 1e9 + hdr * 1e8 + ((v ?? "").StartsWith("avc") ? 1e7 : 0) + Math.Min(Num(f, "tbr") ?? 0, 9e6);
                if (best.TryGetValue(side, out var b) && b.Rank >= rank) continue;
                best[side] = (rank, Size(f), hasSound);
            }
            var video = best.Keys.OrderByDescending(k => k)
                .Select(k => new Quality(k, best[k].Size is long s ? s + (best[k].WithSound ? 0 : soundSize ?? 0) : null)).ToList();
            return new Choices(Str(o, "title") ?? "", video, soundSize);
        }
        catch { return null; }
    }

    /// <summary>"1080p", "2160p (4K)".</summary>
    public static string QualityName(int height) => height switch
    {
        4320 => "4320p (8K)",
        2160 => "2160p (4K)",
        1440 => "1440p (2K)",
        _ => $"{height}p",
    };

    /// <summary>What a video download is set to save, for lists and windows.</summary>
    public static string ChoiceText(bool? audioOnly, int? maxHeight) =>
        audioOnly == true ? "sound only" : maxHeight == OriginalQuality ? "original quality" : maxHeight is int h && h > 0 ? $"up to {QualityName(h)}" : "best quality";

    /// <summary>Title, file type and size from the JSON printed by InfoArguments.</summary>
    public static Info? ParseInfo(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var o = doc.RootElement;
            if (!o.TryGetProperty("title", out var t) || t.ValueKind != System.Text.Json.JsonValueKind.String) return null;
            static long? Size(System.Text.Json.JsonElement e)
            {
                foreach (var k in new[] { "filesize", "filesize_approx" })
                    if (e.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number) return (long)v.GetDouble();
                return null;
            }
            var total = Size(o);
            if (total == null && o.TryGetProperty("requested_formats", out var parts) && parts.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var sizes = parts.EnumerateArray().Select(Size).ToList();
                if (sizes.Count > 0 && sizes.All(s => s != null)) total = sizes.Sum(s => s!.Value);
            }
            var ext = o.TryGetProperty("ext", out var x) && x.ValueKind == System.Text.Json.JsonValueKind.String ? x.GetString()! : "mp4";
            return new Info(t.GetString()!, ext, total);
        }
        catch { return null; }
    }

    /// <summary>
    /// Moves the cookie header, the login and the proxy out of the command line (where any program on the computer can
    /// read them) into a private options file that yt-dlp loads with --config-locations. Returns the remaining arguments
    /// and the file's text ("" when there is nothing private).
    /// </summary>
    public static (List<string> Args, string Config) Private(IReadOnlyList<string> args, string? proxy)
    {
        var rest = new List<string>();
        var config = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a == "--") { rest.AddRange(args.Skip(i)); break; }
            if (i + 1 < args.Count && (a is "--username" or "--password" || (a == "--add-header" && args[i + 1].StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))))
            {
                config.Add($"{a} {Quote(args[i + 1])}");
                i++;
                continue;
            }
            rest.Add(a);
        }
        if (proxy != null) config.Add($"--proxy {Quote(proxy)}");
        return (rest, config.Count == 0 ? "" : string.Join("\n", config) + "\n");
    }

    /// <summary>Quoting for yt-dlp option files (read like a POSIX shell): 'text' with ' written as '"'"'.</summary>
    public static string Quote(string s) => "'" + s.Replace("'", "'\"'\"'") + "'";

    /// <summary>The proxy for yt-dlp: "" = connect directly, null = system settings (yt-dlp then uses the environment).</summary>
    public static string? ProxyUrl(ProxySettings p, string? forUrl = null)
    {
        var host = p.Host.Trim();
        if (p.Mode == ProxyMode.None) return "";
        if (p.Mode == ProxyMode.Pac)
        {
            if (host.Length == 0 || !Uri.TryCreate(forUrl, UriKind.Absolute, out var target)) return null;
            if (Pac.ProxyFor(host, target) is not { } found) return "";
            var auth = p.Username.Length > 0 ? $"{Uri.EscapeDataString(p.Username)}:{Uri.EscapeDataString(p.Password)}@" : "";
            return $"{found.Scheme}://{auth}{found.Host}:{found.Port}";
        }
        if (p.Mode is not (ProxyMode.Http or ProxyMode.Socks) || host.Length == 0) return null;
        var login = p.Username.Length > 0 ? $"{Uri.EscapeDataString(p.Username)}:{Uri.EscapeDataString(p.Password)}@" : "";
        return $"{(p.Mode == ProxyMode.Socks ? "socks5" : "http")}://{login}{host}:{p.Port}";
    }

    /// <summary>Writes the private options next to the download's parts (deleted when yt-dlp is done); null if none.</summary>
    public static string? WriteConfig(string config)
    {
        if (config.Length == 0) return null;
        Directory.CreateDirectory(AppPaths.Parts);
        var path = Path.Combine(AppPaths.Parts, $"engine-{Guid.NewGuid():N}.conf");
        File.WriteAllText(path, config);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return path;
    }

    /// <summary>The arguments to run: private options in a file (see Private), the rest on the command line.</summary>
    public static (List<string> Args, string? ConfigFile) Prepare(List<string> args, ProxySettings? proxy)
    {
        // The video engine keeps to the chosen network adapter too; it stops if the adapter is down.
        var source = Network.Options.BindAdapter.Length > 0 ? Adapters.SourceAddress(Network.Options.BindAdapter) : null;
        var (rest, config) = Private(args, proxy == null ? null : ProxyUrl(proxy, args.LastOrDefault(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase))));
        var file = WriteConfig(config);
        if (source != null) rest.InsertRange(0, new[] { "--source-address", source });
        if (file != null) rest.InsertRange(0, new[] { "--config-locations", file });
        return (rest, file);
    }

    /// <summary>Kept inside the saved file (needs ffmpeg): title, artist, date and description, and the video's chapters.</summary>
    public static readonly string[] MediaDetails = { "--embed-metadata", "--embed-chapters" };

    /// <summary>The command for one video. folder gets the finished file, temp the partial one (so it can resume).</summary>
    public static List<string> Arguments(string url, string folder, string temp, string? ffmpeg, string? quickJs, bool audioOnly,
        string? referrer, string? cookies, string? userAgent, string? username, string? password, int speedLimitKBps, string? infoJson = null,
        int? maxHeight = null)
    {
        var a = new List<string>
        {
            "--ignore-config", "--no-playlist", "--progress", "--newline", "--no-colors", "--no-mtime", "--continue",
            "--progress-template", $"download:{ProgressTag} %(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s %(progress.speed)s %(info.format_id)s %(info.height)s %(info.vcodec)s",
            "--print", $"after_move:{FileTag} %(filepath)s",
            "-P", $"home:{folder}", "-P", $"temp:{temp}",
            "--concurrent-fragments", "4",
            "--js-runtimes", "node",
            "--encoding", "utf-8",
        };
        a.AddRange(Naming(url));
        if (quickJs != null) a.AddRange(new[] { "--js-runtimes", $"quickjs:{quickJs}" });
        if (ffmpeg != null) a.AddRange(new[] { "--ffmpeg-location", Path.GetDirectoryName(ffmpeg)! }); // folder: ffmpeg and ffprobe
        a.AddRange(Format(audioOnly, maxHeight));
        if (ffmpeg != null) a.AddRange(MediaDetails);
        if (ffmpeg != null && audioOnly) a.AddRange(AudioConversion.Arguments(AudioConversion.Format));
        if (ffmpeg != null && !audioOnly && VideoSubtitles.Enabled) a.AddRange(VideoSubtitles.Arguments());
        if (!string.IsNullOrEmpty(referrer)) a.AddRange(new[] { "--referer", referrer });
        if (!string.IsNullOrEmpty(userAgent)) a.AddRange(new[] { "--user-agent", userAgent });
        if (!string.IsNullOrEmpty(cookies)) a.AddRange(new[] { "--add-header", $"Cookie:{cookies}" });
        if (!string.IsNullOrEmpty(username)) a.AddRange(new[] { "--username", username, "--password", password ?? "" });
        if (speedLimitKBps > 0) a.AddRange(new[] { "--limit-rate", $"{speedLimitKBps}K" });
        if (infoJson != null && File.Exists(infoJson) && DateTime.UtcNow - File.GetLastWriteTimeUtc(infoJson) < InfoMaxAge)
        {
            a.Add("--load-info-json"); // already looked up while the download window was open
            a.Add(infoJson);
        }
        else
        {
            a.Add("--");
            a.Add(url);
        }
        return a;
    }
}

/// <summary>Deletes yt-dlp's private options file once the command is done.</summary>
public sealed class ConfigCleanup(string? path) : IDisposable
{
    public void Dispose()
    {
        if (path != null) try { File.Delete(path); } catch { }
    }
}

/// <summary>Where the video tools live: shipped next to the app, copied to a folder the app can keep up to date.</summary>
public static class SiteVideoTools
{
    static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;
    public static string Folder => Path.Combine(AppPaths.Support, "Tools");
    public static string? QuickJs => File.Exists(Path.Combine(AppContext.BaseDirectory, Exe("qjs"))) ? Path.Combine(AppContext.BaseDirectory, Exe("qjs")) : null;

    // Windows uses the unpacked edition of the video engine (a folder with yt-dlp.exe): the one-file edition unpacked
    // itself into a new temporary folder before every video, and Windows scanned it each time. The app's copy in
    // yt-dlp\ is copied to Tools\yt-dlp-engine-<version> (writable, so it can be updated) and kept current daily.
    const string EnginePrefix = "video-engine-";
    const string EngineName = "nexonic-video-engine";      // the engine program, renamed from its upstream name
    const string EngineExe = EngineName + ".exe";
    const string UpstreamExe = "yt-dlp.exe";                // the name in the official release, used only for updates
    const string EngineZip = "yt-dlp_win.zip";
    static string BundledEngine => Path.Combine(AppContext.BaseDirectory, "video-engine");
    static readonly object gate = new();
    static bool updating;

    /// <summary>The video engine to run: the newest ready engine folder on Windows; the self-updating copy elsewhere.</summary>
    public static string YtDlp
    {
        get
        {
            if (EnginePaths.YtDlpFile is { } own) return own;
            if (!OperatingSystem.IsWindows()) return Path.Combine(Folder, Exe(EngineName));
            var best = Engines().FirstOrDefault();
            if (best.Dir != null) return Path.Combine(best.Dir, EngineExe);
            var bundled = Path.Combine(BundledEngine, EngineExe);
            return File.Exists(bundled) ? bundled : Path.Combine(Folder, EngineExe);
        }
    }

    static readonly SemaphoreSlim installing = new(1, 1);

    /// <summary>
    /// True when the video engine is ready. The apps don't ship it: the first time a video is saved (or a video link is
    /// looked up), the official release is downloaded from the engine's GitHub page, checked against the release's
    /// SHA2-256SUMS and only then put in place; afterwards it keeps itself current. <paramref name="status"/> hears about
    /// the one-time download. False when it could not be had (no internet, for example).
    /// </summary>
    public static bool EnsureInstalled(Action<string>? status = null)
    {
        if (EnginePaths.YtDlp.Length > 0) return EnginePaths.YtDlpFile != null;  // the user's own copy: never replaced
        if (File.Exists(YtDlp)) return true;
        installing.Wait();
        try
        {
            if (File.Exists(YtDlp)) return true;
            status?.Invoke("Getting the video engine (only the first time)…");
            Directory.CreateDirectory(Folder);
            if (OperatingSystem.IsWindows()) UpdateAsync("0").GetAwaiter().GetResult();
            else InstallOneFileAsync().GetAwaiter().GetResult();
            return File.Exists(YtDlp);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
        finally { installing.Release(); }
    }

    /// <summary>Why <see cref="EnsureInstalled"/> failed, for the user.</summary>
    public const string Missing = "The video engine could not be downloaded. Check the internet connection and try again.";

    /// <summary>The engine's one-file program for Linux (x64 or ARM), checked against the release's SHA2-256SUMS.</summary>
    static async Task InstallOneFileAsync()
    {
        const string releases = "https://github.com/yt-dlp/yt-dlp/releases";
        var asset = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
            ? "yt-dlp_linux_aarch64" : "yt-dlp_linux";
        using var http = Network.Client(TimeSpan.FromMinutes(10));
        var sums = await http.GetStringAsync($"{releases}/latest/download/SHA2-256SUMS");
        var want = ChecksumFor(asset, sums) ?? throw new InvalidOperationException("The engine's checksum list has no " + asset);
        var bytes = await http.GetByteArrayAsync($"{releases}/latest/download/{asset}");
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() != want)
            throw new InvalidOperationException("The downloaded video engine did not match its checksum.");
        var tmp = YtDlp + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                      UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.Move(tmp, YtDlp, true);
        File.WriteAllText(Path.Combine(Folder, "last-update"), "");
    }

    /// <summary>"2026.10.26" or "2026.10.26.1" compared number by number.</summary>
    public static bool Newer(string a, string b)
    {
        int[] P(string v) => v.Trim().Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        var x = P(a); var y = P(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var l = i < x.Length ? x[i] : 0; var r = i < y.Length ? y[i] : 0;
            if (l != r) return l > r;
        }
        return false;
    }

    static string? VersionOf(string dir)
    {
        try { var f = Path.Combine(dir, "VERSION"); return File.Exists(f) ? File.ReadAllText(f).Trim() : null; } catch { return null; }
    }

    /// <summary>Ready engine folders, newest first. A folder counts once its VERSION file exists (written last).</summary>
    static List<(string Dir, string Version)> Engines()
    {
        try
        {
            if (!Directory.Exists(Folder)) return new();
            return Directory.GetDirectories(Folder, EnginePrefix + "*")
                .Where(d => !d.EndsWith(".tmp", StringComparison.Ordinal) && File.Exists(Path.Combine(d, EngineExe)))
                .Select(d => (Dir: d, Version: VersionOf(d) ?? ""))
                .Where(e => e.Version.Length > 0)
                .OrderByDescending(e => e.Version, Comparer<string>.Create((a, b) => Newer(a, b) ? 1 : Newer(b, a) ? -1 : 0))
                .ToList();
        }
        catch { return new(); }
    }

    /// <summary>Makes the engine ready (first run, or after an app update) and keeps it current once a day.</summary>
    public static void Prepare()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (OperatingSystem.IsWindows()) PrepareWindows();
            else PrepareOneFile();
        }
        catch { }
    }

    static void PrepareOneFile()
    {
        try { File.Delete(Path.Combine(Folder, Exe("yt-dlp"))); } catch { } // the copy under its earlier name
        // Builds before 0.26 shipped the engine next to the app; newer ones get it the first time a video is saved.
        var src = Path.Combine(AppContext.BaseDirectory, Exe(EngineName));
        if (File.Exists(src))
        {
            var stamp = Path.Combine(Folder, "bundled-date");
            var key = File.GetLastWriteTimeUtc(src).Ticks.ToString();
            if (!File.Exists(YtDlp) || (File.Exists(stamp) ? File.ReadAllText(stamp) : "") != key)
            {
                File.Copy(src, YtDlp, true);
                File.WriteAllText(stamp, key);
            }
        }
        if (!File.Exists(YtDlp) || !DueForUpdate()) return;
        Process.Start(new ProcessStartInfo(YtDlp, "-U") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
    }

    static bool DueForUpdate()
    {
        if (EnginePaths.YtDlp.Length > 0) return false;  // the user's own copy is updated by them
        var checkedFile = Path.Combine(Folder, "last-update");
        if (File.Exists(checkedFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(checkedFile) < TimeSpan.FromDays(1)) return false;
        File.WriteAllText(checkedFile, "");
        return true;
    }

    static void PrepareWindows()
    {
        List<(string Dir, string Version)> list;
        lock (gate)
        {
            var bv = VersionOf(BundledEngine);
            if (bv != null && File.Exists(Path.Combine(BundledEngine, EngineExe)) && !Engines().Any(e => !Newer(bv, e.Version)))
                Install(bv, dir => { CopyDirectory(BundledEngine, dir); return true; });
            foreach (var old in new[] { "yt-dlp.exe", "bundled-date" })
                try { File.Delete(Path.Combine(Folder, old)); } catch { } // the one-file edition from earlier versions
            foreach (var old in Directory.GetDirectories(Folder, "yt-dlp-engine-*"))
                try { Directory.Delete(old, true); } catch { }            // engine folders under their earlier name
            list = Engines();
            foreach (var extra in list.Skip(2)) try { Directory.Delete(extra.Dir, true); } catch { } // may still be running
            if (list.Count > 0) Warm(list[0].Dir);
            if (updating || !DueForUpdate()) return;
            updating = true;
        }
        var current = list.FirstOrDefault().Version ?? "0";
        Task.Run(async () =>
        {
            try { await UpdateAsync(current); } catch { }
            lock (gate) updating = false;
        });
    }

    /// <summary>Adds an engine folder to Tools: `fill` puts the files in; the VERSION file is written last.</summary>
    static bool Install(string version, Func<string, bool> fill)
    {
        var tmp = Path.Combine(Folder, EnginePrefix + version + ".tmp");
        var dest = Path.Combine(Folder, EnginePrefix + version);
        try
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            if (!fill(tmp) || !File.Exists(Path.Combine(tmp, EngineExe))) { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); return false; }
            File.Delete(Path.Combine(tmp, "VERSION"));
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.Move(tmp, dest);
            File.WriteAllText(Path.Combine(dest, "VERSION"), version);
            return true;
        }
        catch
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            return false;
        }
    }

    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
    }

    /// <summary>The first run of a new engine is slow while it is scanned; do it now instead of during a download.</summary>
    static void Warm(string dir)
    {
        var mark = Path.Combine(dir, ".checked");
        if (File.Exists(mark)) return;
        if (RunVersion(Path.Combine(dir, EngineExe)) != null) try { File.WriteAllText(mark, ""); } catch { }
    }

    static string? RunVersion(string exe)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, "--version")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(120_000)) { try { p.Kill(true); } catch { } return null; }
            return p.ExitCode == 0 ? output.Trim() : null;
        }
        catch { return null; }
    }

    /// <summary>The SHA-256 for one file in a SHA2-256SUMS list ("hash  name" per line).</summary>
    public static string? ChecksumFor(string name, string sums)
    {
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1] == name) return parts[0].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Fetches a newer official engine (checked against the release's SHA2-256SUMS), unpacks and checks it,
    /// then adds it beside the current one. Any failure leaves the current engine as it is.</summary>
    static async Task UpdateAsync(string current)
    {
        const string releases = "https://github.com/yt-dlp/yt-dlp/releases";
        using var http = Network.Client(TimeSpan.FromMinutes(5));
        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, releases + "/latest"));
        var tag = head.RequestMessage?.RequestUri?.Segments.LastOrDefault()?.Trim('/') ?? "";
        if (tag.Split('.').Length < 3 || !tag.All(c => char.IsDigit(c) || c == '.') || !Newer(tag, current)) return;
        var sums = await http.GetStringAsync($"{releases}/download/{tag}/SHA2-256SUMS");
        var want = ChecksumFor(EngineZip, sums);
        if (want == null) return;
        var zip = await http.GetByteArrayAsync($"{releases}/download/{tag}/{EngineZip}");
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zip)).ToLowerInvariant() != want) return;
        var work = Path.Combine(Folder, "update-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var file = Path.Combine(work, EngineZip);
            await File.WriteAllBytesAsync(file, zip);
            var unpacked = Path.Combine(work, "x");
            System.IO.Compression.ZipFile.ExtractToDirectory(file, unpacked);
            var found = Directory.GetFiles(unpacked, UpstreamExe, SearchOption.AllDirectories).FirstOrDefault();
            if (found == null) return;
            var exe = Path.Combine(Path.GetDirectoryName(found)!, EngineExe);
            File.Move(found, exe);
            if (RunVersion(exe) != tag) return;
            var src = Path.GetDirectoryName(exe)!;
            bool ok;
            lock (gate) ok = Install(tag, dir => { CopyDirectory(src, dir); return true; });
            if (ok) { var e = Engines().FirstOrDefault(x => x.Version == tag); if (e.Dir != null) Warm(e.Dir); }
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }
}

/// <summary>Runs yt-dlp for one video and reports progress; pausing stops it and resuming continues the partial file.</summary>
public sealed class SiteVideoTransfer : IDownloader
{
    public DownloadRecord Record { get; }
    public SpeedLimiter OwnLimiter { get; } = new();
    public event Action<DownloadRecord>? Changed;
    readonly bool sortIntoCategories;
    readonly int speedLimitKBps;
    readonly StringBuilder errors = new();
    Process? process;
    volatile bool stopping;
    DateTime lastNotify = DateTime.MinValue;

    string Temp => Path.Combine(AppPaths.Parts, Record.Id.ToString("N"));

    readonly ProxySettings? proxy;

    public SiteVideoTransfer(DownloadRecord record, bool sortIntoCategories = false, int speedLimitKBps = 0, ProxySettings? proxy = null)
    {
        Record = record;
        this.proxy = proxy;
        this.sortIntoCategories = sortIntoCategories;
        this.speedLimitKBps = new[] { record.SpeedLimitKBps ?? 0, speedLimitKBps }.Where(x => x > 0).DefaultIfEmpty(0).Min();
    }

    void Notify(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - lastNotify).TotalMilliseconds < 250) return;
        lastNotify = now;
        Changed?.Invoke(Record);
    }

    public Task StartAsync() => Task.Run(Run);

    public void Pause()
    {
        stopping = true;
        Record.Status = DownloadStatus.Paused;
        try { process?.Kill(true); } catch { }
        Notify(true);
    }

    public void Discard()
    {
        stopping = true;
        try { process?.Kill(true); } catch { }
        try { Directory.Delete(Temp, true); } catch { }
    }

    void Fail(string message)
    {
        Record.Status = DownloadStatus.Failed;
        Record.Error = message;
        Notify(true);
    }

    void Run()
    {
        SiteVideoTools.Prepare();
        Record.Status = DownloadStatus.Probing;
        Record.Error = null;
        Record.SiteVideo = true;
        Record.VideoStep = null;
        Notify(true);
        if (Network.StrictDnsBlocks("the video engine", proxy ?? new ProxySettings()) is { } why) { Fail(why); return; }
        if (!SiteVideoTools.EnsureInstalled(step => { Record.VideoStep = step; Notify(true); })) { Fail(SiteVideoTools.Missing); return; }
        Record.VideoStep = null;
        var folder = FileCategories.TargetFolder(Record.Folder, Record.AudioOnly == true ? "audio.m4a" : "video.mp4", sortIntoCategories, Record.Url);
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Temp);
        var psi = new ProcessStartInfo(SiteVideoTools.YtDlp)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        List<string> args;
        string? configFile;
        try
        {
            (args, configFile) = SiteVideo.Prepare(SiteVideo.Arguments(Record.Url, folder, Temp, FFmpeg.Path, SiteVideoTools.QuickJs,
                Record.AudioOnly == true, Record.Referrer, Record.Cookies, Record.UserAgent, Record.Username, Record.Password,
                speedLimitKBps, Record.InfoJson, Record.AudioOnly == true ? null : Record.MaxHeight), proxy);
        }
        catch (Exception e)  // never fall back to cookies and passwords on the command line
        {
            Fail("The video engine's private settings could not be written: " + e.Message);
            return;
        }
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        try { RunProcess(psi, folder); }
        finally { if (configFile != null) try { File.Delete(configFile); } catch { } }
    }

    void RunProcess(ProcessStartInfo psi, string folder)
    {
        try
        {
            if (stopping) return;
            process = Process.Start(psi)!;
        }
        catch (Exception e) { Fail("The video engine could not start: " + e.Message); return; }
        Record.Status = DownloadStatus.Downloading;
        Notify(true);
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errors) { errors.AppendLine(e.Data); if (errors.Length > 4000) errors.Remove(0, errors.Length - 4000); } };
        process.BeginErrorReadLine();
        string? line;
        var parts = new SiteVideo.PartProgress(); // picture and sound pieces added up
        while ((line = process.StandardOutput.ReadLine()) != null)
        {
            if (SiteVideo.ParseProgress(line) is { } p)
            {
                var sum = parts.Add(p);
                Record.StreamBytes = sum.Done;
                if (sum.Total is long t) Record.Size = t;
                Record.VideoParts = parts.Pieces;
                Notify();
            }
            else if (SiteVideo.Step(line) is { } step) { Record.VideoStep = step; Notify(true); }
            else if (SiteVideo.DestinationName(line.Trim()) is { } name) { Record.FileName = name; Notify(true); }
            else if (SiteVideo.SavedFile(line.Trim()) is { } path) Record.SavedPath = path;
        }
        process.WaitForExit();
        if (stopping) return;
        if (process.ExitCode != 0 || Record.SavedPath is not { } saved || !File.Exists(saved))
        {
            string text;
            lock (errors) text = errors.ToString();
            Fail(SiteVideo.Error(text));
            return;
        }
        try { Directory.Delete(Temp, true); } catch { }
        Record.FileName = Path.GetFileName(saved);
        Record.Size = new FileInfo(saved).Length;
        Record.StreamBytes = Record.Size;
        Record.Status = DownloadStatus.Finished;
        Record.VideoStep = null;
        Record.Finished = DateTime.Now;
        Notify(true);
    }
}

/// <summary>
/// Looks one video link up in the background while the Add Download window is open (title, qualities, sizes), like
/// the browser download window does. The saved details are handed to the download, which then starts at once.
/// </summary>
public sealed class VideoPrefetch : IDisposable
{
    readonly Func<ProxySettings?> proxy;
    readonly Action changed;  // called on a background thread: the window moves it to its own thread
    readonly object gate = new();
    CancellationTokenSource? cts;
    string? path;
    bool done;

    public string? Link { get; private set; }
    public bool Busy { get; private set; }
    public SiteVideo.Info? Info { get; private set; }
    public SiteVideo.Choices? Choices { get; private set; }

    public VideoPrefetch(Func<ProxySettings?> proxy, Action changed)
    {
        this.proxy = proxy;
        this.changed = changed;
    }

    /// <summary>The one video-site link in the window, or null; looked up shortly after it stops changing.</summary>
    public void Want(string? link)
    {
        CancellationToken token;
        lock (gate)
        {
            if (done || link == Link) return;
            cts?.Cancel();
            Drop();
            Link = link; Info = null; Choices = null; Busy = link != null;
            if (link == null) { changed(); return; }
            cts = new CancellationTokenSource();
            token = cts.Token;
        }
        changed();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(600, token);
                var file = Path.Combine(AppPaths.Parts, $"info-{Guid.NewGuid():N}.json");
                var (info, json) = Run(link!, token);
                lock (gate)
                {
                    if (token.IsCancellationRequested || done || Link != link) return;
                    Busy = false;
                    if (info != null)
                    {
                        try { File.WriteAllText(file, json); path = file; } catch { }
                        Info = info;
                        Choices = SiteVideo.ParseChoices(json);
                    }
                }
                changed();
            }
            catch (OperationCanceledException) { }
            catch { lock (gate) { if (Link == link) Busy = false; } changed(); }
        });
    }

    (SiteVideo.Info?, string) Run(string link, CancellationToken token)
    {
        SiteVideoTools.Prepare();
        if (!SiteVideoTools.EnsureInstalled()) return (null, "");
        Directory.CreateDirectory(AppPaths.Parts);
        var psi = new ProcessStartInfo(SiteVideoTools.YtDlp)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        var (args, configFile) = SiteVideo.Prepare(
            SiteVideo.InfoArguments(link, SiteVideoTools.QuickJs, false, null, null, null, null, null), proxy());
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var cleanup = new ConfigCleanup(configFile);
        using var p = Process.Start(psi)!;
        using var stop = token.Register(() => { try { p.Kill(true); } catch { } });
        p.ErrorDataReceived += (_, _) => { };
        p.BeginErrorReadLine();
        var json = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        token.ThrowIfCancellationRequested();
        return (p.ExitCode == 0 ? SiteVideo.ParseInfo(json) : null, json);
    }

    /// <summary>The saved details for this link, handed to the download (which deletes them when it is removed).</summary>
    public string? Take(string link)
    {
        lock (gate)
        {
            done = true;
            cts?.Cancel();
            if (link != Link || path == null) { Drop(); return null; }
            var p = path;
            path = null;
            return p;
        }
    }

    /// <summary>The window closed without using them.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            done = true;
            cts?.Cancel();
            Drop();
        }
    }

    void Drop()
    {
        if (path != null) try { File.Delete(path); } catch { }
        path = null;
    }

    /// <summary>The quality menu: 0 = best, -1 = sound only, otherwise the highest picture height.</summary>
    public static List<(int Tag, string Label)> Options(SiteVideo.Choices? choices)
    {
        var first = choices?.Video.FirstOrDefault();
        var list = new List<(int, string)> { (0, first != null ? $"Best ({SiteVideo.QualityName(first.Height)})" : "Best available") };
        foreach (var q in choices?.Video.Skip(1) ?? Enumerable.Empty<SiteVideo.Quality>()) list.Add((q.Height, SiteVideo.QualityName(q.Height)));
        list.Add((-1, "Sound only"));
        return list;
    }

    /// <summary>The size of the chosen quality, when the video's details tell it.</summary>
    public static long? SizeFor(SiteVideo.Info? info, SiteVideo.Choices? choices, int tag) => tag switch
    {
        0 => info?.Size ?? choices?.Video.FirstOrDefault()?.Size,
        -1 => choices?.SoundSize,
        _ => choices?.Video.FirstOrDefault(v => v.Height == tag)?.Size,
    };
}

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexonic.Core;

/// <summary>One video (or sound, or picture post) found on a profile, channel, playlist or page.</summary>
/// <summary>Kind: video, shorts, live or photo. Direct (added in 0.31.0): the file itself, from a gallery engine listing.</summary>
public sealed record BatchEntry(string Url, string Title, double? Duration = null, DateTime? Date = null, string Kind = "video",
    bool Available = true, string Id = "", bool Direct = false);

/// <summary>Which of the found videos to keep. Values a site does not report (a date, a length) never exclude a video.</summary>
public sealed class BatchFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    /// <summary>Words the title must contain (any of them); a leading ! leaves titles with that word out.</summary>
    public string Keywords { get; set; } = "";
    public int? MinSeconds { get; set; }
    public int? MaxSeconds { get; set; }
    /// <summary>"all", "videos" (no shorts or live streams), "shorts" or "live".</summary>
    public string Kind { get; set; } = "all";
    /// <summary>The most videos to take (newest first, as the site lists them).</summary>
    public int Max { get; set; } = ProfileBatch.DefaultMax;
}

/// <summary>A profile the user saved: checked again on a schedule for videos that were not there before.</summary>
public sealed class SavedProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Quality { get; set; } = "best";
    public BatchFilter Filter { get; set; } = new();
    /// <summary>Check for new videos every this many hours (0 = only when asked).</summary>
    public int CheckHours { get; set; }
    public DateTime? LastChecked { get; set; }
    public string LastResult { get; set; } = "";
    /// <summary>Every video already found on this profile (normalized addresses), so New Only takes the rest.</summary>
    public List<string> Seen { get; set; } = new();

    public bool Due(DateTime now) => CheckHours > 0 && (LastChecked is not { } t || now - t >= TimeSpan.FromHours(CheckHours));
}

/// <summary>What happened to the videos of one batch, shown in the window and kept with saved profiles.</summary>
public sealed record BatchCounts(int Discovered, int Queued, int Duplicates, int Filtered, int Inaccessible)
{
    public string Text => string.Format(CultureInfo.CurrentCulture, L.T("{0} found · {1} added · {2} already downloaded · {3} left out by filters · {4} not accessible"),
        Discovered, Queued, Duplicates, Filtered, Inaccessible);
}

/// <summary>
/// Profile Batch: one link to a profile, channel, playlist, gallery or page → every video the video engine can list there
/// (it follows the site's pages itself) → filtered, without repeats → added to a queue of its own. Works for every site the
/// video engine supports, plus RSS/podcast feeds and pages with embedded videos. It reads only what the site shows to anyone
/// without an account: no login, cookies or keys are used, and nothing behind a login, paywall or CAPTCHA is reached.
/// </summary>
public static class ProfileBatch
{
    public const int DefaultMax = 500, Limit = 5000;

    public static readonly (string Key, string Title, int? Height, bool Audio)[] Qualities =
    {
        ("best", "Best", null, false), ("8k", "8K", 4320, false), ("4k", "4K", 2160, false), ("1080", "1080p", 1080, false),
        ("720", "720p", 720, false), ("480", "480p", 480, false), ("original", "Original (no conversion)", SiteVideo.OriginalQuality, false),
        ("audio", "Sound only", null, true),
    };

    public static (int? Height, bool Audio) Quality(string key) =>
        Qualities.FirstOrDefault(q => q.Key == key) is { Key: not null } q ? (q.Height, q.Audio) : (null, false);

    /// <summary>The fields printed for each entry; one JSON object per line, so progress shows while the site is read.</summary>
    const string Fields = "%(.{url,webpage_url,original_url,title,fulltitle,duration,timestamp,upload_date,release_timestamp,live_status,was_live,availability,ie_key,id,playlist_title,playlist_uploader,uploader,channel})j";

    /// <summary>The command that lists the videos without downloading them. start (1-based) continues an interrupted scan.</summary>
    public static List<string> Arguments(string url, string? quickJs, int start = 1, int max = DefaultMax)
    {
        max = Math.Clamp(max, 1, Limit);
        var a = new List<string>
        {
            "--ignore-config", "--flat-playlist", "--ignore-errors", "--no-colors", "--encoding", "utf-8", "--js-runtimes", "node",
            "--extractor-retries", "3", "--retry-sleep", "extractor:exp=2:30", "--print", Fields,
            "--playlist-start", start.ToString(CultureInfo.InvariantCulture),
            "--playlist-end", (start + max - 1).ToString(CultureInfo.InvariantCulture),
        };
        if (quickJs != null) a.AddRange(new[] { "--js-runtimes", $"quickjs:{quickJs}" });
        if (EnginePlugins.Folder() is { } plugins) a.AddRange(new[] { "--plugin-dirs", plugins });
        a.Add("--");
        a.Add(Playlists.Normalize(url));
        return a;
    }

    /// <summary>What a site's lister said about the list itself (for example that visitors see only the latest posts).</summary>
    public static string Note(string errors)
    {
        var notes = errors.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("WARNING:") && l.Contains(EnginePlugins.Marker))
            .Select(l => l[(l.IndexOf(EnginePlugins.Marker, StringComparison.Ordinal) + EnginePlugins.Marker.Length)..].Trim()).Distinct();
        return string.Join(" ", notes);
    }

    /// <summary>One printed line → an entry and the profile's title (when the site gives one). Null for anything else.</summary>
    public static (BatchEntry Entry, string? Owner)? ParseLine(string line)
    {
        line = line.Trim();
        if (!line.StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var e = doc.RootElement;
            string? S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
            double? N(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            var id = S("id") ?? "";
            var url = S("webpage_url") ?? S("url") ?? S("original_url");
            if ((url == null || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) && id.Length > 0 && (S("ie_key") ?? "").StartsWith("Youtube"))
                url = "https://www.youtube.com/watch?v=" + id;
            if (url == null || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
            DateTime? date = (N("timestamp") ?? N("release_timestamp")) is double ts ? DateTimeOffset.FromUnixTimeSeconds((long)ts).LocalDateTime
                : S("upload_date") is { Length: 8 } d && DateTime.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd) ? dd : null;
            var live = S("live_status");
            var kind = live is "is_live" or "was_live" or "is_upcoming" || e.TryGetProperty("was_live", out var wl) && wl.ValueKind == JsonValueKind.True ? "live"
                : url.Contains("/shorts/", StringComparison.OrdinalIgnoreCase) || url.Contains("/reel/", StringComparison.OrdinalIgnoreCase) ? "shorts" : "video";
            var availability = S("availability");
            var title = S("title") ?? S("fulltitle") ?? url;
            // Lists show removed and private videos as placeholders; they are counted, never downloaded.
            var available = availability is not ("private" or "premium_only" or "subscriber_only" or "needs_auth")
                && !title.StartsWith("[Private video]") && !title.StartsWith("[Deleted video]");
            return (new BatchEntry(url, title, N("duration"), date, kind, available, id),
                S("playlist_title") ?? S("playlist_uploader") ?? S("channel") ?? S("uploader"));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The address without the engine's internal additions and fragments, to recognise the same video.</summary>
    static readonly string[] CdnHosts = { "cdninstagram.com", "fbcdn.net", "tiktokcdn.com", "tiktokcdn-us.com", "twimg.com", "pinimg.com" };

    /// <summary>The profile's name (its folder): Instagram's or Facebook's owner, else the address's first part ("@name" → "name").</summary>
    public static string OwnerOf(string url) =>
        SocialPages.Find(url) is { } s ? s.Owner
        : Uri.TryCreate(url, UriKind.Absolute, out var u) && u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts
            ? parts[0].TrimStart('@') : "";

    public static string Key(string url)
    {
        var u = url.Split('#')[0].Trim();
        // The sites' picture and video servers sign every address anew; the file is the same without the signature.
        if (Uri.TryCreate(u, UriKind.Absolute, out var a) && CdnHosts.Any(h => a.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase)))
            u = a.GetLeftPart(UriPartial.Path);
        return Duplicates.Normalize(u);
    }

    public static bool Matches(BatchEntry e, BatchFilter f)
    {
        if (e.Date is { } d && (f.From is { } from && d.Date < from.Date || f.To is { } to && d.Date > to.Date)) return false;
        if (e.Duration is { } s && (f.MinSeconds is { } min && s < min || f.MaxSeconds is { } max && s > max)) return false;
        if (f.Kind == "videos" && e.Kind != "video" || f.Kind is "shorts" or "live" && e.Kind != f.Kind) return false;
        var words = f.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var include = words.Where(w => !w.StartsWith('!')).ToList();
        var exclude = words.Where(w => w.StartsWith('!') && w.Length > 1).Select(w => w[1..]).ToList();
        if (include.Count > 0 && !include.Any(w => e.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase))) return false;
        return !exclude.Any(w => e.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// Picks the videos to download: available, passing the filter, not a download already in the list (or, for New Only,
    /// not seen on this profile before), each once, at most Max.
    /// </summary>
    public static (List<BatchEntry> Take, BatchCounts Counts) Choose(IEnumerable<BatchEntry> found, BatchFilter filter,
        Func<string, bool> alreadyDownloaded, ISet<string>? seen = null)
    {
        int discovered = 0, duplicates = 0, filtered = 0, inaccessible = 0;
        var keys = new HashSet<string>();
        var take = new List<BatchEntry>();
        foreach (var e in found)
        {
            if (!keys.Add(Key(e.Url))) continue;
            discovered++;
            if (!e.Available) { inaccessible++; continue; }
            if (alreadyDownloaded(e.Url) || seen != null && seen.Contains(Key(e.Url))) { duplicates++; continue; }
            if (!Matches(e, filter) || take.Count >= Math.Clamp(filter.Max, 1, Limit)) { filtered++; continue; }
            take.Add(e);
        }
        return (take, new BatchCounts(discovered, take.Count, duplicates, filtered, inaccessible));
    }

    /// <summary>The profile's own folder inside the chosen folder ("Channel name"), safe for every system.</summary>
    public static string FolderFor(string baseFolder, string owner, string url)
    {
        var name = owner.Trim().Length > 0 ? FileNames.Sanitize(owner.Trim()) : "";
        if (name.Length == 0) name = Uri.TryCreate(url, UriKind.Absolute, out var u) ? FileNames.Sanitize(u.Host + u.AbsolutePath.Replace('/', ' ').TrimEnd()) : "Profile";
        if (name.Length > 80) name = name[..80].Trim();
        return Path.Combine(baseFolder, name);
    }

    /// <summary>The found videos as a text list (one link per line), CSV or JSON.</summary>
    public static string Export(IEnumerable<BatchEntry> entries, string format)
    {
        var list = entries.ToList();
        switch (format)
        {
            case "csv":
                static string C(string? v) => v == null ? "" : v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
                var sb = new StringBuilder("url,title,date,duration_seconds,type,accessible\r\n");
                foreach (var e in list)
                    sb.Append(C(e.Url)).Append(',').Append(C(e.Title)).Append(',').Append(e.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                      .Append(',').Append(e.Duration?.ToString("0", CultureInfo.InvariantCulture)).Append(',').Append(e.Kind)
                      .Append(',').Append(e.Available ? "yes" : "no").Append("\r\n");
                return sb.ToString();
            case "json":
                return JsonSerializer.Serialize(list.Select(e => new
                {
                    url = e.Url, title = e.Title, date = e.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    duration_seconds = e.Duration, type = e.Kind, accessible = e.Available,
                }), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            default:
                return string.Join("\n", list.Select(e => e.Url)) + (list.Count > 0 ? "\n" : "");
        }
    }

    // ---- interrupted scans ----

    /// <summary>What a scan found so far, kept while it runs so an interrupted scan continues where it stopped.</summary>
    public sealed class Scan
    {
        public string Url { get; set; } = "";
        public string Owner { get; set; } = "";
        public List<BatchEntry> Entries { get; set; } = new();
        public bool Complete { get; set; }
        /// <summary>A limit the site puts on what visitors see, shown with the result (not saved).</summary>
        [System.Text.Json.Serialization.JsonIgnore] public string Note { get; set; } = "";
    }

    static string ScanPath(string url) => Path.Combine(AppPaths.Parts, "profile-scans",
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Key(url))))[..24] + ".json");

    public static Scan? LoadScan(string url)
    {
        try { return File.Exists(ScanPath(url)) ? JsonSerializer.Deserialize<Scan>(File.ReadAllText(ScanPath(url))) : null; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void SaveScan(Scan s)
    {
        try
        {
            var p = ScanPath(s.Url);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(s));
            File.Move(p + ".tmp", p, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static void ForgetScan(string url) { try { File.Delete(ScanPath(url)); } catch (IOException) { } }

    /// <summary>
    /// Lists the videos at <paramref name="url"/> with the video engine, calling <paramref name="found"/> for each as it comes.
    /// An interrupted earlier scan of the same link continues after the videos it already found (unless fresh). Returns the
    /// whole scan; throws with a readable reason when the site can't be read (unsupported, private, rate limited…).
    /// </summary>
    public static Scan Discover(string url, ProxySettings? proxy, int max, Action<BatchEntry, int>? found = null, bool fresh = false,
        CancellationToken cancel = default)
    {
        var scan = fresh ? null : LoadScan(url);
        if (scan is { Complete: true }) scan = null;  // a finished scan is read again: the profile may have new videos
        scan ??= new Scan { Url = url };
        for (var i = 0; i < scan.Entries.Count; i++) found?.Invoke(scan.Entries[i], i + 1);
        if (scan.Entries.Count >= max) { scan.Complete = true; return scan; }
        SiteVideoTools.Prepare();
        if (!SiteVideoTools.EnsureInstalled()) throw new InvalidOperationException(SiteVideoTools.Missing);
        var args = Arguments(url, SiteVideoTools.QuickJs, scan.Entries.Count + 1, max - scan.Entries.Count);
        var (prepared, configFile) = SiteVideo.Prepare(args, proxy);
        using var cleanup = new ConfigCleanup(configFile);
        var psi = new ProcessStartInfo(SiteVideoTools.YtDlp)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var a in prepared) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("The video engine could not start");
        var errors = p.StandardError.ReadToEndAsync();
        using var kill = cancel.Register(() => { try { p.Kill(true); } catch { } });
        var lastSave = DateTime.UtcNow;
        string? line;
        while (!cancel.IsCancellationRequested && (line = p.StandardOutput.ReadLine()) != null)
        {
            if (ParseLine(line) is not { } parsed) continue;
            if (parsed.Owner is { Length: > 0 } o && scan.Owner.Length == 0) scan.Owner = o;
            scan.Entries.Add(parsed.Entry);
            found?.Invoke(parsed.Entry, scan.Entries.Count);
            if (DateTime.UtcNow - lastSave > TimeSpan.FromSeconds(2)) { SaveScan(scan); lastSave = DateTime.UtcNow; }
        }
        p.WaitForExit();
        if (cancel.IsCancellationRequested) { SaveScan(scan); cancel.ThrowIfCancellationRequested(); }
        var err = errors.Wait(5_000) ? errors.Result : "";
        if (scan.Entries.Count == 0)
        {
            ForgetScan(url);
            throw new InvalidOperationException(p.ExitCode != 0 || err.Contains("ERROR") ? SiteVideo.Error(err) : L.T("No videos were found at this link"));
        }
        scan.Complete = true;
        scan.Note = Note(err);
        ForgetScan(url);
        return scan;
    }
}

/// <summary>Saved profiles (profiles.json in the data folder), checked on their schedule for new videos.</summary>
public static class SavedProfiles
{
    static string File => Path.Combine(AppPaths.Support, "profiles.json");

    public static List<SavedProfile> Load()
    {
        try { return System.IO.File.Exists(File) ? JsonSerializer.Deserialize<List<SavedProfile>>(System.IO.File.ReadAllText(File)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public static void Save(List<SavedProfile> list)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Support);
            System.IO.File.WriteAllText(File + ".tmp", JsonSerializer.Serialize(list));
            System.IO.File.Move(File + ".tmp", File, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// The profile listers Nexonic adds to the video engine (engine/nexonic_profiles.py, a plugin for sites whose own lister needs an
/// account or stopped working). They only read what the site shows to visitors who are not signed in. The file is
/// written next to the video tools when missing or changed, and handed to the engine with --plugin-dirs.
/// </summary>
public static class EnginePlugins
{
    /// <summary>Messages from the listers start with this, so the app shows them as they are.</summary>
    public const string Marker = "Nexonic:";
    static string? folder;

    public static string? Folder()
    {
        if (folder != null) return folder;
        try
        {
            using var s = typeof(EnginePlugins).Assembly.GetManifestResourceStream("nexonic_profiles.py");
            if (s == null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();
            var root = Path.Combine(SiteVideoTools.Folder, "plugins");
            var file = Path.Combine(root, "nexonic", "yt_dlp_plugins", "extractor", "nexonic_profiles.py");
            if (!File.Exists(file) || !File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, bytes);
            }
            return folder = root;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}

/// <summary>
/// Instagram shows visitors who are not signed in the latest posts of a profile, but only through its own page scripts.
/// The apps open the profile page in a private web view (no cookies, no sign-in), like a visitor's browser, run Script
/// and read the video links the page shows. Nothing else of Instagram's is called.
/// </summary>
public static class InstagramPage
{
    static readonly string[] Reserved = { "p", "reel", "reels", "tv", "stories", "explore", "accounts", "direct", "about", "legal",
        "developer", "api", "graphql", "web", "challenge", "emails", "session", "privacy", "terms" };

    /// <summary>The user name of an Instagram profile link (instagram.com/name, /name/reels), or null.</summary>
    public static string? User(string link)
    {
        var m = Regex.Match(link.Trim(), @"^https?://(?:www\.)?instagram\.com/([A-Za-z0-9._]{1,30})/?(?:reels/?|videos/?)?(?:[?#].*)?$", RegexOptions.IgnoreCase);
        return m.Success && !Reserved.Contains(m.Groups[1].Value.ToLowerInvariant()) ? m.Groups[1].Value : null;
    }

    /// <summary>Run in the loaded page: {"posts": links to posts shown, "videos": [{"u": link, "t": picture text}]}.</summary>
    public const string Script = """
        (() => {
          const all = [...document.querySelectorAll('a[href*="/reel/"], a[href*="/p/"]')], seen = new Set(), videos = [];
          for (const a of all) {
            const link = new URL(a.getAttribute('href'), location.href);
            const m = link.pathname.match(/\/(p|reel)\/([A-Za-z0-9_-]+)/);
            if (!m) continue;
            const alt = (a.querySelector('img') || {}).alt || '';
            const video = m[1] === 'reel' || /^Video by /.test(alt) || !!a.querySelector('svg[aria-label="Clip"], svg[aria-label="Video"], svg[aria-label="Reel"]');
            const u = 'https://www.instagram.com/' + m[1] + '/' + m[2] + '/';
            if (video && !seen.has(u)) { seen.add(u); videos.push({ u, t: alt }); }
          }
          return JSON.stringify({ posts: all.length, videos });
        })()
        """;

    /// <summary>What Script returned: the number of posts the page shows and its videos.</summary>
    public static (int Posts, List<BatchEntry> Videos) Read(string? json)
    {
        var list = new List<BatchEntry>();
        if (string.IsNullOrEmpty(json)) return (0, list);
        try
        {
            // The web views hand the script's string back either as it is or JSON-quoted.
            if (json.StartsWith('"')) json = JsonSerializer.Deserialize<string>(json) ?? "";
            using var doc = JsonDocument.Parse(json);
            var posts = doc.RootElement.TryGetProperty("posts", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
            if (doc.RootElement.TryGetProperty("videos", out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var e in v.EnumerateArray())
                {
                    var url = e.TryGetProperty("u", out var u) ? u.GetString() ?? "" : "";
                    if (!url.StartsWith("https://www.instagram.com/", StringComparison.Ordinal) && !url.StartsWith("https://www.facebook.com/", StringComparison.Ordinal)) continue;
                    var text = e.TryGetProperty("t", out var t) ? t.GetString() ?? "" : "";
                    DateTime? date = Regex.Match(text, @" on ([A-Z][a-z]+ \d{1,2}, \d{4})") is { Success: true } dm
                        && DateTime.TryParseExact(dm.Groups[1].Value, "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
                    var title = Regex.Replace(text, @"\s+", " ").Trim();
                    if (title.Length > 120) title = title[..120] + "…";
                    list.Add(new BatchEntry(url, title.Length > 0 ? title : url, null, date, url.Contains("/reel/") ? "shorts" : "video", true, url.Contains("?v=") ? url.Split("?v=")[^1] : url.Split('/')[^2]));
                }
            return (posts, list);
        }
        catch (JsonException) { return (0, list); }
    }

    /// <summary>Shown with the result: Instagram shows visitors only the latest posts.</summary>
    public static string Note(int posts) =>
        $"Instagram shows visitors who are not signed in only the latest {posts} posts of a profile; Nexonic took the videos among them.";

    public const string Nothing = "Instagram did not show any posts of this profile to a visitor who is not signed in (it may be private or not exist).";

    public static string NoVideos(int posts) => $"None of the latest {posts} posts Instagram shows visitors of this profile is a video.";
}

/// <summary>
/// Instagram and Facebook profiles in Profile Batch. Their profile pages show visitors who are not signed in only the
/// latest posts; the rest come through the user's own signed-in browser and the Nexonic extension (see More).
/// </summary>
public sealed record SocialSite(string Name, string LoginUrl, string Domain, string SessionCookie, string Script);

public static class SocialPages
{
    /// <summary>
    /// Added under what a visitor sees: how to get the rest. The Nexonic extension's "Download all" on the profile, in a
    /// browser signed in to the site, hands the whole profile to the gallery engine with the browser's cookies (read-only, a person's pace) and sends every
    /// video and photo here. Nexonic itself never signs in.
    /// </summary>
    public static string More(SocialSite site) =>
        $"To get every video and photo: open this profile in Chrome, Edge or Firefox where you are signed in to {site.Name}, click the Nexonic extension and choose Download all. Nexonic then saves the whole profile with that sign-in, a request every 2-4 seconds like a person, and only reads; it never sees your password.";

    public static readonly SocialSite Instagram = new("Instagram", "https://www.instagram.com/accounts/login/", "instagram.com", "sessionid", InstagramPage.Script);
    public static readonly SocialSite Facebook = new("Facebook", "https://www.facebook.com/login/", "facebook.com", "c_user", FacebookScript);

    /// <summary>The site, the profile's name and the pages that list its videos; null for other links.</summary>
    public static (SocialSite Site, string Owner, string[] Pages)? Find(string link)
    {
        if (InstagramPage.User(link) is { } user) return (Instagram, user, new[] { $"https://www.instagram.com/{user}/" });
        var m = Regex.Match(link.Trim(), @"^https?://(?:www\.|m\.|web\.)?facebook\.com/(?:profile\.php\?id=(\d+)|(?!(?:watch|reel|videos|groups|events|share|story\.php|photo|permalink\.php|login|marketplace|gaming|people|pages)\b)([A-Za-z0-9.\-]{2,80}))", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        if (m.Groups[1].Success)
        {
            var id = m.Groups[1].Value;
            return (Facebook, id, new[] { $"https://www.facebook.com/profile.php?id={id}&sk=reels_tab", $"https://www.facebook.com/profile.php?id={id}&sk=videos" });
        }
        var name = m.Groups[2].Value;
        return (Facebook, name, new[] { $"https://www.facebook.com/{name}/reels/", $"https://www.facebook.com/{name}/videos/" });
    }

    /// <summary>Run in a loaded Facebook page: its reels and videos, as InstagramPage.Script reports them.</summary>
    public const string FacebookScript = """
        (() => {
          const seen = new Set(), videos = [];
          for (const a of document.querySelectorAll('a[href*="/reel/"], a[href*="/videos/"], a[href*="watch/?v="], a[href*="watch?v="]')) {
            const link = new URL(a.getAttribute('href'), location.href);
            let id = null, reel = false, m;
            if ((m = link.pathname.match(/\/reel\/(\d+)/))) { id = m[1]; reel = true; }
            else if ((m = link.pathname.match(/\/videos\/(?:[^/]+\/)?(\d{6,})/))) id = m[1];
            else if (/^\d{6,}$/.test(link.searchParams.get('v') || '')) id = link.searchParams.get('v');
            if (!id || seen.has(id)) continue;
            seen.add(id);
            const u = reel ? 'https://www.facebook.com/reel/' + id + '/' : 'https://www.facebook.com/watch/?v=' + id;
            videos.push({ u, t: (a.getAttribute('aria-label') || a.innerText || '').trim().slice(0, 200) });
          }
          return JSON.stringify({ posts: videos.length, videos });
        })()
        """;


    /// <summary>
    /// Adds what a page shows now to what was found before (pages drop posts that scrolled far away) and says whether
    /// anything new came. Stops at max.
    /// </summary>
    public static bool Merge(Dictionary<string, BatchEntry> found, List<BatchEntry> now, int max)
    {
        var before = found.Count;
        foreach (var e in now) if (found.Count < max) found.TryAdd(e.Url, e);
        return found.Count > before;
    }
}

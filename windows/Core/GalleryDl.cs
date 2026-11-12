using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Nexonic.Core;

/// <summary>
/// The gallery engine (gallery-dl, GPL-2.0, run as a separate program): saves every picture and clip of a gallery, profile
/// or album on sites it knows. It is not shipped with the app: the official build of the gallery-dl project is downloaded
/// the first time it is used, checked against the SHA-256 GitHub lists for it, and refreshed after two weeks.
/// </summary>
public static class GalleryDl
{
    const string Api = "https://api.github.com/repos/gdl-org/builds/releases/latest";
    static readonly SemaphoreSlim installing = new(1, 1);

    public static string Program => Path.Combine(SiteVideoTools.Folder, OperatingSystem.IsWindows() ? "gallery-dl.exe" : "gallery-dl");

    /// <summary>The build for this computer, or null (Linux on ARM has none).</summary>
    public static string? Asset =>
        OperatingSystem.IsWindows() ? "gallery-dl_windows.exe"
        : OperatingSystem.IsMacOS() ? "gallery-dl_macos"
        : System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64 ? "gallery-dl_linux" : null;

    public const string Missing = "The gallery engine could not be downloaded. Check the internet connection and try again.";
    public const string NotAvailable = "The gallery engine has no build for this kind of computer.";

    /// <summary>The download address and SHA-256 of <paramref name="asset"/> in a GitHub release (API answer).</summary>
    public static (string Url, string Sha256)? Pick(string releaseJson, string asset)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        if (!doc.RootElement.TryGetProperty("assets", out var assets)) return null;
        foreach (var a in assets.EnumerateArray())
            if (a.GetProperty("name").GetString() == asset && a.TryGetProperty("digest", out var d) && d.GetString() is { } digest
                && digest.StartsWith("sha256:", StringComparison.Ordinal) && a.GetProperty("browser_download_url").GetString() is { } url
                && url.StartsWith("https://github.com/", StringComparison.Ordinal))
                return (url, digest[7..].ToLowerInvariant());
        return null;
    }

    /// <summary>Makes sure the engine is there (downloading it the first time). False when it could not be had.</summary>
    public static bool EnsureInstalled(Action<string>? status = null)
    {
        var fresh = File.Exists(Program) && DateTime.UtcNow - File.GetLastWriteTimeUtc(Program) < TimeSpan.FromDays(14);
        if (fresh) return true;
        installing.Wait();
        try
        {
            if (File.Exists(Program) && DateTime.UtcNow - File.GetLastWriteTimeUtc(Program) < TimeSpan.FromDays(14)) return true;
            if (Asset is not { } asset) return false;
            status?.Invoke(File.Exists(Program) ? "Updating the gallery engine…" : "Getting the gallery engine (only the first time)…");
            try { InstallAsync(asset).GetAwaiter().GetResult(); }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            {
                if (File.Exists(Program)) File.SetLastWriteTimeUtc(Program, DateTime.UtcNow.AddDays(-13)); // try again tomorrow
            }
            return File.Exists(Program);
        }
        finally { installing.Release(); }
    }

    static async Task InstallAsync(string asset)
    {
        using var http = Network.Client(TimeSpan.FromMinutes(10));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NexonicDownloadManager");
        var (url, want) = Pick(await http.GetStringAsync(Api), asset) ?? throw new InvalidOperationException("The gallery engine's release has no " + asset);
        var bytes = await http.GetByteArrayAsync(url);
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() != want)
            throw new InvalidOperationException("The downloaded gallery engine did not match its checksum.");
        Directory.CreateDirectory(SiteVideoTools.Folder);
        var tmp = Program + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                      UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.Move(tmp, Program, true);
    }

    /// <summary>The engine's arguments: everything goes straight into <paramref name="folder"/>; files already there are skipped.
    /// The site login, cookies and proxy are never here: they go in the private settings file (see <see cref="Settings"/>).</summary>
    public const string Pace = "extractor.sleep-request=1.0-2.0";

    public static List<string> Arguments(string url, string folder, int speedLimitKBps, string? referrer, string? settingsFile = null)
    {
        // A person's pace on every site: 1-2 seconds between the requests that list a profile or gallery.
        var args = new List<string> { "--directory", folder, "-o", Pace };
        if (settingsFile != null) args.AddRange(new[] { "--config", settingsFile });
        if (speedLimitKBps > 0) args.AddRange(new[] { "--limit-rate", speedLimitKBps + "k" });
        if (referrer is { Length: > 0 } && Uri.IsWellFormedUriString(referrer, UriKind.Absolute)) args.AddRange(new[] { "-o", "extractor.referer=" + referrer });
        args.Add("--");
        args.Add(url);
        return args;
    }

    /// <summary>
    /// The private settings for one gallery (the engine's JSON settings file): the browser's cookies for the site, its
    /// browser name, the site login and the proxy. "" when there is nothing private. Lets galleries that need you signed in work.
    /// </summary>
    public static string Settings(string? cookies, string? userAgent, string? username, string? password, string? proxyUrl)
    {
        var extractor = new Dictionary<string, object>();
        var jar = Cookies(cookies);
        if (jar.Count > 0) extractor["cookies"] = jar;
        if (userAgent is { Length: > 0 }) extractor["user-agent"] = userAgent;
        if (username is { Length: > 0 }) { extractor["username"] = username; extractor["password"] = password ?? ""; }
        if (proxyUrl is { Length: > 0 }) extractor["proxy"] = proxyUrl;
        return extractor.Count == 0 ? "" : JsonSerializer.Serialize(new Dictionary<string, object> { ["extractor"] = extractor });
    }

    /// <summary>"a=1; b=2" (a Cookie header) as name → value.</summary>
    public static Dictionary<string, string> Cookies(string? header)
    {
        var jar = new Dictionary<string, string>();
        foreach (var part in (header ?? "").Split(';'))
        {
            var eq = part.IndexOf('=');
            var name = eq > 0 ? part[..eq].Trim() : "";
            if (name.Length > 0) jar[name] = part[(eq + 1)..].Trim();
        }
        return jar;
    }

    /// <summary>Writes the private settings, readable only by this user, next to the download parts (deleted when the
    /// engine is done); null when there are none.</summary>
    public static string? WriteSettings(string settings)
    {
        if (settings.Length == 0) return null;
        Directory.CreateDirectory(AppPaths.Parts);
        var path = Path.Combine(AppPaths.Parts, $"gallery-{Guid.NewGuid():N}.json");
        using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            f.Write(Encoding.UTF8.GetBytes(settings));
        }
        return path;
    }

    /// <summary>A line of the engine's output: a saved file (true), a file it already had ("# path", false), or neither.</summary>
    public static bool? SavedFile(string line, out string path)
    {
        path = line.Trim();
        if (path.StartsWith("# ", StringComparison.Ordinal)) { path = path[2..]; return Path.IsPathRooted(path) ? false : null; }
        return Path.IsPathRooted(path) ? true : null;
    }
}

/// <summary>
/// Profile Batch with the gallery engine: the photos and videos of a profile, listed (not downloaded) with the browser's
/// cookies for the site, at a person's pace. The user then picks Download Now or Add to Queue.
/// </summary>
public static class GalleryList
{
    static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { "mp4", "webm", "mov", "m4v", "mkv" };

    /// <summary>Sites whose profiles the engine lists completely with a browser's sign-in (Facebook pages are not among them).</summary>
    static readonly string[] ProfileSites = { "instagram.com", "tiktok.com", "x.com", "twitter.com", "threads.net", "threads.com", "pinterest.com",
        "reddit.com", "tumblr.com", "bsky.app", "vk.com", "flickr.com", "deviantart.com" };

    /// <summary>A profile (not a single post) on one of those sites.</summary>
    public static bool IsProfile(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var host = u.Host.ToLowerInvariant();
        if (!ProfileSites.Any(s => host == s || host.EndsWith("." + s))) return false;
        var first = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";
        return first.Length > 0 && !new[] { "p", "reel", "tv", "stories", "explore", "accounts", "direct", "video", "status", "i", "home", "search",
            "settings", "messages", "notifications", "login", "share", "hashtag", "tag", "watch" }.Contains(first);
    }

    /// <summary>
    /// One line per file while listing: its address and the details used for its name, as a JSON array (the engine writes
    /// them as it finds them).
    /// </summary>
    public const string Format = "[{_url!j},{extension!j},{post_shortcode!j},{shortcode!j},{post_id!j},{id!j},{filename!j},{description!j},{content!j},{title!j},{desc!j},{num!j},{count!j},{date!j},{duration!j},{username!j}]";

    /// <summary>The engine's arguments for listing: a line per file (nothing downloaded), at most <paramref name="max"/> of them.</summary>
    public static List<string> Arguments(string url, int max, string? settingsFile)
    {
        var args = new List<string> { "-N", Format, "--range", "1-" + Math.Max(1, max), "-o", GalleryDl.Pace };
        if (settingsFile != null) args.AddRange(new[] { "--config", settingsFile });
        // TikTok: its own listing first through the video engine, then the gallery engine's own way.
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith("tiktok.com", StringComparison.OrdinalIgnoreCase))
            args.AddRange(new[] { "-o", "extractor.tiktok.ytdl=true" });
        // Instagram videos as one file each (not as separate picture and sound streams), so they are listed and saved
        // like the photos.
        if (u != null && u.Host.EndsWith("instagram.com", StringComparison.OrdinalIgnoreCase))
            args.AddRange(new[] { "-o", "extractor.instagram.videos=merged" });
        args.Add("--");
        args.Add(url);
        return args;
    }

    /// <summary>
    /// The files in the engine's listing (its lines): the picture or video address, named "description [post id].ext";
    /// and the account they belong to (their folder), "" when it doesn't say.
    /// </summary>
    public static (List<BatchEntry> Entries, string Owner) Entries(string lines, string fallback = "")
    {
        var list = new List<BatchEntry>();
        var seen = new HashSet<string>();
        var owner = "";
        foreach (var line in lines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }
            using (doc)
            {
                var f = doc.RootElement;
                if (f.ValueKind != JsonValueKind.Array || f.GetArrayLength() < 16 || f[0].ValueKind != JsonValueKind.String) continue;
                var url = f[0].GetString()!;
                if (!url.StartsWith("https://", StringComparison.Ordinal) || !seen.Add(url)) continue;
                string Text(int i) => f[i].ValueKind switch { JsonValueKind.String => f[i].GetString() ?? "", JsonValueKind.Number => f[i].GetRawText(), _ => "" };
                int Int(int i) => f[i].ValueKind == JsonValueKind.Number && f[i].TryGetInt32(out var n) ? n : 0;
                if (owner.Length == 0) owner = Text(15);
                var ext = Text(1).ToLowerInvariant();
                if (ext.Length == 0) ext = "jpg";
                var post = new[] { 2, 3, 4, 5, 6 }.Select(Text).FirstOrDefault(x => x.Length > 0) ?? "";
                var caption = (new[] { 7, 8, 9, 10 }.Select(Text).FirstOrDefault(x => x.Length > 0) ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
                if (caption.Length > 100) caption = caption[..100].Trim();
                var part = Int(12) > 1 && Int(11) > 0 ? "-" + Int(11) : "";
                var label = caption.Length > 0 ? caption : owner.Length > 0 ? owner : fallback;
                var name = FileNames.Sanitize($"{label} [{post}{part}].{ext}");
                DateTime? date = DateTime.TryParse(Text(13), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
                double? seconds = f[14].ValueKind == JsonValueKind.Number ? f[14].GetDouble() : null;
                list.Add(new BatchEntry(url, name, seconds, date, VideoExtensions.Contains(ext) ? "video" : "photo",
                    Id: Text(2).Length > 0 ? Text(2) : Text(3), Direct: true));
            }
        }
        return (list, owner);
    }

    /// <summary>Lists a profile with the engine; null when it can't (no build here, not installed, or a site or address it doesn't know).</summary>
    public static async Task<(List<BatchEntry> Entries, string Owner)?> ListAsync(string url, string? cookies, string? userAgent, ProxySettings? proxy, int max,
        CancellationToken cancel, Action<int>? waiting = null)
    {
        if (GalleryDl.Asset == null || !await Task.Run(() => GalleryDl.EnsureInstalled(), cancel)) return null;
        var settingsFile = GalleryDl.WriteSettings(GalleryDl.Settings(cookies, userAgent, null, null, proxy == null ? null : SiteVideo.ProxyUrl(proxy, url)));
        try
        {
            var all = new List<BatchEntry>();
            var seen = new HashSet<string>();
            var owner = "";
            foreach (var part in Parts(url))
            {
                string? cursor = null;
                for (var tries = 0; ; tries++)
                {
                    var args = Arguments(part, max, settingsFile);
                    if (cursor != null) args.InsertRange(args.Count - 2, new[] { "-o", "cursor=" + cursor });
                    var (output, errors) = await RunAsync(args, cancel);
                    var listed = Entries(output, owner);
                    if (owner.Length == 0) owner = listed.Owner;
                    all.AddRange(listed.Entries.Where(e => seen.Add(ProfileBatch.Key(e.Url))));
                    Note(part, cursor != null, all.Count, errors);
                    // The site stopped answering partway (it limits how much one account may read in a row): the engine
                    // says where it got to; after a pause the search continues from there, a few times, longer each time.
                    cursor = ResumeCursor(errors);
                    if (cursor == null || tries >= 6 || all.Count >= max) break;
                    var pause = 60 << tries;
                    waiting?.Invoke(pause);
                    await Task.Delay(TimeSpan.FromSeconds(pause), cancel);
                }
            }
            return all.Count == 0 ? null : (all, owner);
        }
        finally { if (settingsFile != null) try { File.Delete(settingsFile); } catch { } }
    }

    static async Task<(string Output, string Errors)> RunAsync(List<string> args, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo(GalleryDl.Program)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        using var kill = cancel.Register(() => { try { p.Kill(true); } catch { } });
        var errors = p.StandardError.ReadToEndAsync(cancel);
        var output = await p.StandardOutput.ReadToEndAsync(cancel);
        await p.WaitForExitAsync(cancel);
        return (output, await errors);
    }

    /// <summary>
    /// The parts of a profile that are listed one after the other: an Instagram profile's posts, then its reels (videos
    /// that are not on the posts grid); anything else as it is.
    /// </summary>
    public static List<string> Parts(string url)
    {
        if (!IsProfile(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u) || !u.Host.EndsWith("instagram.com", StringComparison.OrdinalIgnoreCase))
            return new() { url };
        var user = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
        return new() { $"https://www.instagram.com/{user}/posts/", $"https://www.instagram.com/{user}/reels/" };
    }

    /// <summary>
    /// What each search said, kept in the data folder (the last 200 KB) so a search that finds less than expected can be
    /// looked into. No cookies or passwords are in it: those go to the engine in a private settings file.
    /// </summary>
    public static string SearchLog => Path.Combine(AppPaths.Support, "profile-search.log");

    static void Note(string url, bool continued, int found, string errors)
    {
        try
        {
            var lines = errors.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(40).Select(l => "    " + l.TrimEnd());
            var text = $"{DateTime.UtcNow:O}  {url}{(continued ? " (continued)" : "")}  {found} found so far\n" + string.Join("\n", lines) + "\n";
            var old = File.Exists(SearchLog) ? File.ReadAllText(SearchLog) : "";
            var all = old + text;
            File.WriteAllText(SearchLog, all.Length > 200_000 ? all[^200_000..] : all);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Where an interrupted listing got to, as the engine says it ("Use '-o cursor=…' to continue").</summary>
    public static string? ResumeCursor(string errors) =>
        System.Text.RegularExpressions.Regex.Match(errors, @"-o cursor=([^'""\s]+)") is { Success: true } m ? m.Groups[1].Value : null;
}

/// <summary>Runs the gallery engine for one gallery: the list row counts files and bytes; pausing stops it and resuming
/// continues (files already saved are skipped).</summary>
public sealed class GalleryTransfer(DownloadRecord record, bool sortIntoCategories, int speedLimitKBps, ProxySettings? proxy) : IDownloader
{
    public DownloadRecord Record { get; } = record;
    public SpeedLimiter OwnLimiter { get; } = new();
    public event Action<DownloadRecord>? Changed;
    readonly StringBuilder errors = new();
    Process? process;
    volatile bool stopping;

    public Task StartAsync() => Task.Run(Run);

    public void Pause()
    {
        stopping = true;
        Record.Status = DownloadStatus.Paused;
        try { process?.Kill(true); } catch { }
        Changed?.Invoke(Record);
    }

    public void Discard()
    {
        stopping = true;
        try { process?.Kill(true); } catch { }
    }

    void Fail(string message)
    {
        Record.Status = DownloadStatus.Failed;
        Record.Error = message;
        Changed?.Invoke(Record);
    }

    void Run()
    {
        Record.Status = DownloadStatus.Probing;
        Record.Error = null;
        Record.VideoStep = null;
        Changed?.Invoke(Record);
        if (GalleryDl.Asset == null) { Fail(GalleryDl.NotAvailable); return; }
        if (Network.StrictDnsBlocks("the gallery engine", proxy ?? new ProxySettings()) is { } why) { Fail(why); return; }
        if (!GalleryDl.EnsureInstalled(step => { Record.VideoStep = step; Changed?.Invoke(Record); })) { Fail(GalleryDl.Missing); return; }
        Record.VideoStep = null;
        var folder = FileCategories.TargetFolder(Record.Folder, "picture.jpg", sortIntoCategories, Record.Url);
        if (Record.SavedPath is { Length: > 0 } earlier && Directory.Exists(earlier)) folder = earlier;
        else folder = Path.Combine(folder, FileNames.Sanitize(Record.FileName.Length > 0 ? Record.FileName : "Gallery"));
        Directory.CreateDirectory(folder);
        Record.SavedPath = folder;
        var psi = new ProcessStartInfo(GalleryDl.Program)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        var limit = new[] { Record.SpeedLimitKBps ?? 0, speedLimitKBps }.Where(x => x > 0).DefaultIfEmpty(0).Min();
        string? settingsFile;
        try
        {
            settingsFile = GalleryDl.WriteSettings(GalleryDl.Settings(Record.Cookies, Record.UserAgent, Record.Username, Record.Password,
                proxy == null ? null : SiteVideo.ProxyUrl(proxy, Record.Url)));
        }
        catch (Exception e)  // never fall back to cookies and passwords on the command line
        {
            Fail("The gallery engine's private settings could not be written: " + e.Message);
            return;
        }
        try { Run(psi, folder, limit, settingsFile); }
        finally { if (settingsFile != null) try { File.Delete(settingsFile); } catch { } }
    }

    void Run(ProcessStartInfo psi, string folder, int limit, string? settingsFile)
    {
        foreach (var a in GalleryDl.Arguments(Record.Url, folder, limit, Record.Referrer, settingsFile))
            psi.ArgumentList.Add(a);
        try
        {
            if (stopping) return;
            process = Process.Start(psi)!;
        }
        catch (Exception e) { Fail("The gallery engine could not start: " + e.Message); return; }
        Record.Status = DownloadStatus.Downloading;
        Changed?.Invoke(Record);
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errors) { errors.AppendLine(e.Data); if (errors.Length > 4000) errors.Remove(0, errors.Length - 4000); } };
        process.BeginErrorReadLine();
        string? line;
        while ((line = process.StandardOutput.ReadLine()) != null)
        {
            if (GalleryDl.SavedFile(line, out var path) == null) continue;
            Record.StreamDone = (Record.StreamDone ?? 0) + 1;
            try { Record.StreamBytes = (Record.StreamBytes ?? 0) + new FileInfo(path).Length; } catch { }
            Changed?.Invoke(Record);
        }
        process.WaitForExit();
        if (stopping) return;
        if (process.ExitCode == 0 || Record.StreamDone > 0 && process.ExitCode is 4 or 8)
        {
            Record.Status = DownloadStatus.Finished;
            Record.Finished = DateTime.Now;
            Record.Size = Record.StreamBytes;
            Changed?.Invoke(Record);
            return;
        }
        string last;
        lock (errors) last = errors.ToString().Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? "";
        last = System.Text.RegularExpressions.Regex.Replace(last, @"^\[[^\]]*\]\[error\]\s*", "");
        Fail(last.Length > 0 ? last : process.ExitCode == 64 ? "The gallery engine does not know this site." : $"The gallery engine stopped (code {process.ExitCode}).");
    }
}

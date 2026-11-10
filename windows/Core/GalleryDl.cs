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
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
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
    public static List<string> Arguments(string url, string folder, int speedLimitKBps, string? referrer, string? settingsFile = null)
    {
        var args = new List<string> { "--directory", folder };
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

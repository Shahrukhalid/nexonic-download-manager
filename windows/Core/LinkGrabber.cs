using System.Net;
using System.Text.RegularExpressions;

namespace Nexonic.Core;

/// <summary>
/// Tasks → Link Grabber: finds the links in pasted text or a web page and groups them into packages. The parts of a split
/// archive (name.part1.rar, name.7z.001, name.r00…) form one package, saved together and unpacked once every part is done.
/// </summary>
public static class LinkGrabber
{
    public sealed record Package(string Name, List<string> Links);

    public const string OtherFiles = "Other files";
    static readonly Regex Href = new(@"\b(?:href|src|data-url|data-href)\s*=\s*(?:""([^""]+)""|'([^']+)')", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Plain = new(@"(?:https?://|magnet:\?)[^\s""'<>\]\[)(]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly HashSet<string> WebPages = new(StringComparer.OrdinalIgnoreCase) { "", ".html", ".htm", ".php", ".asp", ".aspx", ".jsp", ".cgi", ".shtml", ".xhtml" };
    static readonly Regex SetPart = new(@"^(?<base>.+?)(?:\.part\d+\.rar|\.r\d{2,3}|\.z\d{2}|\.(?:7z|zip|rar|tar|gz)\.\d{3}|\.\d{3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Archive = new(@"^(?<base>.+?)\.(?:rar|zip|7z|tar|tar\.gz|tgz|tar\.xz|tar\.bz2)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public const int MaxLinks = 5000;

    /// <summary>The links in <paramref name="text"/> (HTML or plain text). From a web page (<paramref name="page"/> set), only links
    /// to files are kept, not links to other pages.</summary>
    public static List<string> Find(string text, Uri? page = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        void Add(string raw)
        {
            if (result.Count >= MaxLinks) return;
            raw = WebUtility.HtmlDecode(raw.Trim()).TrimEnd('.', ',', ';');
            if (raw.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) { if (seen.Add(raw)) result.Add(raw); return; }
            if (!Uri.TryCreate(page, raw, out var u) && !Uri.TryCreate(raw, UriKind.Absolute, out u)) return;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return;
            var link = Links.Unwrap(new UriBuilder(u) { Fragment = "" }.Uri.AbsoluteUri);
            if (page != null && WebPages.Contains(Path.GetExtension(new Uri(link).AbsolutePath))) return;
            if (seen.Add(link)) result.Add(link);
        }
        foreach (Match m in Href.Matches(text)) Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        foreach (Match m in Plain.Matches(text)) Add(m.Value);
        return result;
    }

    /// <summary>The text is one address of a web page (not of a file): the grabber reads that page.</summary>
    public static Uri? PageAddress(string text)
    {
        text = text.Trim();
        return text.Length > 0 && !text.Any(char.IsWhiteSpace) && Uri.TryCreate(text, UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && WebPages.Contains(Path.GetExtension(u.AbsolutePath)) ? u : null;
    }

    /// <summary>The name shared by the parts of a split archive ("Movie" for Movie.part2.rar), or null for other files.</summary>
    public static string? SetName(string fileName)
    {
        var m = SetPart.Match(fileName);
        return m.Success ? m.Groups["base"].Value : null;
    }

    /// <summary>Packages in the order the links came: split archives and archives with the same name together, everything
    /// else in "Other files".</summary>
    public static List<Package> Group(IEnumerable<string> links)
    {
        var packages = new List<Package>();
        var byName = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);
        var other = new Package(OtherFiles, new List<string>());
        foreach (var link in links)
        {
            var file = link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ? "" : FileNames.FromUrl(link);
            var name = SetName(file) ?? (Archive.Match(file) is { Success: true } a ? a.Groups["base"].Value : null);
            if (name == null) { other.Links.Add(link); continue; }
            name = FileNames.Sanitize(name);
            if (!byName.TryGetValue(name, out var p)) packages.Add(byName[name] = p = new Package(name, new List<string>()));
            p.Links.Add(link);
        }
        if (other.Links.Count > 0) packages.Add(other);
        return packages;
    }

    /// <summary>
    /// Archives to unpack now that <paramref name="done"/> finished: a package's archives once every download in it is
    /// done, the first part of a split archive once every part in the folder is done, or a single archive.
    /// </summary>
    public static List<string> ToUnpack(DownloadRecord done, IEnumerable<DownloadRecord> all, bool unpackAll)
    {
        if (done.Status != DownloadStatus.Finished) return new();
        var list = all.ToList();
        if (done.Package is { Length: > 0 } package)
        {
            if (!unpackAll && done.UnpackWhenDone != true) return new();
            var members = list.Where(r => r.Package == package && r.Folder == done.Folder).ToList();
            return members.All(r => r.Status == DownloadStatus.Finished)
                ? members.Select(r => r.SavedPath).Where(Archives.IsArchive).Select(p => p!).ToList() : new();
        }
        if (!unpackAll) return new();
        if (SetName(done.FileName) is { } set)
        {
            var parts = list.Where(r => r.Folder == done.Folder && string.Equals(SetName(r.FileName), set, StringComparison.OrdinalIgnoreCase)).ToList();
            return parts.All(r => r.Status == DownloadStatus.Finished)
                ? parts.Select(r => r.SavedPath).Where(Archives.IsArchive).Select(p => p!).Take(1).ToList() : new();
        }
        return Archives.IsArchive(done.SavedPath) ? new() { done.SavedPath! } : new();
    }
}

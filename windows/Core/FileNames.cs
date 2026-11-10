using System.Text;

namespace Nexonic.Core;

public static class FileNames
{
    public static string? FromContentDisposition(string? header)
    {
        if (string.IsNullOrEmpty(header)) return null;
        var parts = header.Split(';').Select(p => p.Trim()).ToList();
        foreach (var p in parts.Where(p => p.StartsWith("filename*=", StringComparison.OrdinalIgnoreCase)))
        {
            var v = p["filename*=".Length..].Trim('"');
            var i = v.IndexOf("''", StringComparison.Ordinal);
            if (i >= 0) v = v[(i + 2)..];
            var decoded = Uri.UnescapeDataString(v);
            if (decoded.Length > 0) return Sanitize(decoded);
        }
        foreach (var p in parts.Where(p => p.StartsWith("filename=", StringComparison.OrdinalIgnoreCase)))
        {
            var v = p["filename=".Length..].Trim('"', ' ');
            if (v.Length > 0) return Sanitize(v);
        }
        return null;
    }

    public static string FromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return "download";
        var last = Uri.UnescapeDataString(u.AbsolutePath.TrimEnd('/').Split('/').Last());
        return Sanitize(last.Length == 0 ? u.Host : last);
    }

    static readonly HashSet<char> Bad = new("<>:\"/\\|?*");
    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "LPT1", "LPT2", "LPT3" };

    /// <summary>Safe on Windows and macOS: no separators, control or reserved characters, no reserved device names.</summary>
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name) sb.Append(Bad.Contains(c) || char.IsControl(c) ? '_' : c);
        var s = sb.ToString().Trim().TrimStart('.').TrimEnd('.', ' ');
        if (s.Length == 0) s = "download";
        if (Reserved.Contains(Path.GetFileNameWithoutExtension(s))) s = "_" + s;
        if (s.Length > 200)
        {
            var ext = Path.GetExtension(s);
            s = s[..(190 - Math.Min(ext.Length, 10))] + ext;
        }
        return s;
    }

    /// <summary>"file.zip" → "file (1).zip" … until the name is free.</summary>
    public static string Unique(string name, string folder)
    {
        var path = Path.Combine(folder, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var n = 1; File.Exists(path); n++) path = Path.Combine(folder, $"{stem} ({n}){ext}");
        return path;
    }
}

public enum FileCategory { Compressed, Documents, Music, Video, Programs, Images, Other }

public static class FileCategories
{
    /// <summary>The subfolder of the download folder for this type, like IDM. Files of no particular type stay in the folder.</summary>
    public static string? Folder(FileCategory c) => c == FileCategory.Other ? null : c.ToString();

    static readonly Dictionary<FileCategory, string[]> Map = new()
    {
        [FileCategory.Compressed] = new[] { "zip", "rar", "7z", "gz", "tgz", "bz2", "xz", "tar", "zst" },
        [FileCategory.Documents] = new[] { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "epub", "csv", "odt" },
        [FileCategory.Music] = new[] { "mp3", "m4a", "aac", "flac", "wav", "ogg", "opus" },
        [FileCategory.Video] = new[] { "mp4", "mkv", "mov", "avi", "webm", "m4v", "wmv", "flv", "ts" },
        [FileCategory.Programs] = new[] { "exe", "msi", "dmg", "pkg", "deb", "rpm", "apk", "iso", "img", "appx", "msix" },
        [FileCategory.Images] = new[] { "jpg", "jpeg", "png", "gif", "webp", "heic", "svg", "tiff", "bmp" },
    };

    /// <summary>The user's own categories (Options → Categories), checked before the built-in ones. Set by the app.</summary>
    public static volatile IReadOnlyList<CustomCategory> Custom = Array.Empty<CustomCategory>();

    /// <summary>The user's first category (in their order) that takes this file: its type and, when the category names
    /// sites, the address it comes from must match.</summary>
    public static CustomCategory? CustomFor(string fileName, string? url = null)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return Custom.FirstOrDefault(c => c.Takes(ext, url));
    }

    /// <summary>The category name shown in the app: the user's own, or the built-in one.</summary>
    public static string NameOf(string fileName, string? url = null) => CustomFor(fileName, url)?.Name is { Length: > 0 } n ? n : Of(fileName).ToString();

    /// <summary>
    /// Where a finished file goes: a matching category of the user's (its full path, or a subfolder of
    /// <paramref name="folder"/>), else the built-in type folder when sorting is on, else <paramref name="folder"/>.
    /// </summary>
    public static string TargetFolder(string folder, string fileName, bool sortBuiltIn, string? url = null)
    {
        if (CustomFor(fileName, url) is { } c && c.FolderPath(folder) is { } custom) return custom;
        return sortBuiltIn && Folder(Of(fileName)) is { } sub ? Path.Combine(folder, sub) : folder;
    }

    public static FileCategory Of(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        foreach (var (cat, exts) in Map) if (exts.Contains(ext)) return cat;
        return FileCategory.Other;
    }
}

/// <summary>A category of the user's own: a name, file types ("cbz cbr") and a folder (a full path, or a subfolder of the
/// download folder; empty = the category's name).</summary>
public sealed class CustomCategory
{
    public string Name { get; set; } = "";
    public string Extensions { get; set; } = "";
    public string Folder { get; set; } = "";
    /// <summary>A symbol shown before the name in the sidebar (one emoji or character), or "".</summary>
    public string Icon { get; set; } = "";
    /// <summary>Sites whose files belong here, space separated: "example.com" (and its subdomains), "*.cdn.test",
    /// "github.com/*/releases/*". Empty = any site.</summary>
    public string Sites { get; set; } = "";

    public HashSet<string> Types() => FileTypes.Parse(Extensions);

    /// <summary>True when a file of this type (lowercase, no dot) from this address belongs here. A category with sites
    /// and no types takes every file from those sites.</summary>
    public bool Takes(string ext, string? url)
    {
        var types = Types();
        if (types.Count > 0 && (ext.Length == 0 || !types.Contains(ext))) return false;
        if (Sites.Trim().Length == 0) return types.Count > 0;
        return url != null && SiteMatches(Sites, url);
    }

    /// <summary>True when the address matches one of the site patterns (no pattern ever matches a non-web address).</summary>
    public static bool SiteMatches(string patterns, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https" or "ftp")) return false;
        var host = u.IdnHost.ToLowerInvariant().TrimEnd('.');
        var path = Uri.UnescapeDataString(u.AbsolutePath).ToLowerInvariant();
        foreach (var raw in patterns.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = SitePattern(raw);
            if (p.Length == 0) continue;
            var slash = p.IndexOf('/');
            var hp = slash < 0 ? p : p[..slash];
            var pp = slash < 0 ? null : p[slash..];
            var hostOk = hp.Contains('*') ? Glob(hp, host) : host == hp || host.EndsWith("." + hp, StringComparison.Ordinal);
            if (hostOk && (pp == null || Glob(pp, path) || Glob(pp.TrimEnd('/') + "/*", path))) return true;
        }
        return false;
    }

    /// <summary>A pattern as kept: lowercase, without "https://" and without a query.</summary>
    public static string SitePattern(string raw)
    {
        var p = raw.Trim().ToLowerInvariant();
        var scheme = p.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) p = p[(scheme + 3)..];
        var q = p.IndexOfAny(new[] { '?', '#' });
        if (q >= 0) p = p[..q];
        return p.Length <= 200 && p.All(ch => ch > ' ' && ch != '"' && ch != '\\') ? p : "";
    }

    static bool Glob(string pattern, string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>The folder for this category, or null when the name and folder are both unusable.</summary>
    public string? FolderPath(string downloadFolder)
    {
        var f = Folder.Trim();
        if (f.Length > 0 && Path.IsPathFullyQualified(f)) return f;
        var sub = FileNames.Sanitize(f.Length > 0 ? f : Name.Trim());
        if (sub.Length == 0 || sub is "." or ".." || f.Split('/', '\\').Contains("..")) return null;
        return Path.Combine(downloadFolder, sub);
    }

    /// <summary>The Options text: one category per line, in order: "[icon] Name: types @ sites > folder" (the icon, the
    /// sites and the folder may be left out). Renaming and reordering is editing these lines.</summary>
    public static string ToLines(IEnumerable<CustomCategory> list) =>
        string.Join("\n", list.Select(c => (c.Icon.Length > 0 ? c.Icon + " " : "") + $"{c.Name}: {c.Extensions}"
            + (c.Sites.Length > 0 ? (c.Extensions.Length > 0 ? " @ " : "@ ") + c.Sites : "") + (c.Folder.Length > 0 ? " > " + c.Folder : "")));

    /// <summary>The category lines of the Options text (comments and blank lines left out), for the editor's list.</summary>
    public static List<string> Lines(string? text) =>
        (text ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    /// <summary>The text with category <paramref name="index"/> moved to <paramref name="to"/> (comments are kept at the top).</summary>
    public static string Move(string? text, int index, int to)
    {
        var all = (text ?? "").Replace("\r", "").Split('\n').ToList();
        var comments = all.Where(l => l.Trim().StartsWith('#')).ToList();
        var lines = Lines(text);
        if (index < 0 || index >= lines.Count) return text ?? "";
        to = Math.Clamp(to, 0, lines.Count - 1);
        var item = lines[index];
        lines.RemoveAt(index);
        lines.Insert(to, item);
        return string.Join("\n", comments.Concat(lines));
    }

    /// <summary>The text without category <paramref name="index"/>.</summary>
    public static string Remove(string? text, int index)
    {
        var comments = (text ?? "").Replace("\r", "").Split('\n').Where(l => l.Trim().StartsWith('#'));
        var lines = Lines(text);
        if (index >= 0 && index < lines.Count) lines.RemoveAt(index);
        return string.Join("\n", comments.Concat(lines));
    }

    /// <summary>A leading symbol of a name ("📚 Books" → "📚", "Books"): one text element that is not a letter or digit.</summary>
    public static (string Icon, string Name) SplitIcon(string name)
    {
        name = name.Trim();
        var e = System.Globalization.StringInfo.GetNextTextElementLength(name);
        if (e == 0 || e >= name.Length || !char.IsWhiteSpace(name[e])) return ("", name);
        var first = name[..e];
        return first.Any(char.IsLetterOrDigit) ? ("", name) : (first, name[e..].Trim());
    }

    /// <summary>Reads the Options text; returns the categories, or a message naming the first line that cannot be read.</summary>
    public static (List<CustomCategory> List, string? Problem) ParseLines(string? text)
    {
        var list = new List<CustomCategory>();
        var n = 0;
        foreach (var raw in (text ?? "").Split('\n'))
        {
            n++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            // "C:\Comics" has a colon too: the name ends at the first colon followed by a space or the end.
            while (colon >= 0 && colon + 1 < line.Length && !char.IsWhiteSpace(line[colon + 1])) colon = line.IndexOf(':', colon + 1);
            if (colon <= 0) return (list, $"Line {n}: write it as Name: types > folder (for example Comics: cbz cbr > Comics).");
            var rest = line[(colon + 1)..];
            var arrow = rest.IndexOf('>');
            var match = arrow >= 0 ? rest[..arrow] : rest;
            var at = match.IndexOf('@');
            var (icon, name) = SplitIcon(line[..colon]);
            var c = new CustomCategory
            {
                Name = name,
                Icon = icon,
                Extensions = (at >= 0 ? match[..at] : match).Trim(),
                Sites = at >= 0 ? match[(at + 1)..].Trim() : "",
                Folder = arrow >= 0 ? rest[(arrow + 1)..].Trim() : "",
            };
            if (c.Types().Count == 0 && c.Sites.Length == 0) return (list, $"Line {n}: list at least one file type or site for {c.Name}.");
            if (c.Sites.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(x => CustomCategory.SitePattern(x).Length == 0))
                return (list, $"Line {n}: a site of {c.Name} cannot be read (write it like example.com or github.com/*/releases).");
            list.Add(c);
        }
        return (Clean(list), null);
    }

    /// <summary>Categories with a name and at least one type, at most 50, each type in the first category that lists it.</summary>
    public static List<CustomCategory> Clean(IEnumerable<CustomCategory>? list)
    {
        var seen = new HashSet<string>();
        var result = new List<CustomCategory>();
        foreach (var c in list ?? Enumerable.Empty<CustomCategory>())
        {
            var sites = (c.Sites ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(SitePattern)
                .Where(x => x.Length > 0).Distinct().Take(20).ToList();
            // A type belongs to the first category that lists it, unless the category is limited to some sites.
            var types = c.Types().Where(t => t.Length is > 0 and <= 16 && t.All(char.IsLetterOrDigit) && (sites.Count > 0 || seen.Add(t))).ToList();
            if (string.IsNullOrWhiteSpace(c.Name) || types.Count == 0 && sites.Count == 0) continue;
            var (icon, _) = SplitIcon((c.Icon ?? "") + " x");
            result.Add(new CustomCategory
            {
                Name = c.Name.Trim(), Extensions = string.Join(' ', types), Folder = (c.Folder ?? "").Trim(),
                Icon = icon, Sites = string.Join(' ', sites),
            });
            if (result.Count == 50) break;
        }
        return result;
    }
}

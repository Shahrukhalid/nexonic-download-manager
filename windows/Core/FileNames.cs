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

    /// <summary>The user's category for this file name, if one lists its type.</summary>
    public static CustomCategory? CustomFor(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext.Length == 0 ? null : Custom.FirstOrDefault(c => c.Types().Contains(ext));
    }

    /// <summary>The category name shown in the app: the user's own, or the built-in one.</summary>
    public static string NameOf(string fileName) => CustomFor(fileName)?.Name is { Length: > 0 } n ? n : Of(fileName).ToString();

    /// <summary>
    /// Where a finished file goes: a matching category of the user's (its full path, or a subfolder of
    /// <paramref name="folder"/>), else the built-in type folder when sorting is on, else <paramref name="folder"/>.
    /// </summary>
    public static string TargetFolder(string folder, string fileName, bool sortBuiltIn)
    {
        if (CustomFor(fileName) is { } c && c.FolderPath(folder) is { } custom) return custom;
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

    public HashSet<string> Types() => FileTypes.Parse(Extensions);

    /// <summary>The folder for this category, or null when the name and folder are both unusable.</summary>
    public string? FolderPath(string downloadFolder)
    {
        var f = Folder.Trim();
        if (f.Length > 0 && Path.IsPathFullyQualified(f)) return f;
        var sub = FileNames.Sanitize(f.Length > 0 ? f : Name.Trim());
        if (sub.Length == 0 || sub is "." or ".." || f.Split('/', '\\').Contains("..")) return null;
        return Path.Combine(downloadFolder, sub);
    }

    /// <summary>The Options text: one category per line, "Name: types" or "Name: types > folder".</summary>
    public static string ToLines(IEnumerable<CustomCategory> list) =>
        string.Join("\n", list.Select(c => $"{c.Name}: {c.Extensions}{(c.Folder.Length > 0 ? " > " + c.Folder : "")}"));

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
            var c = new CustomCategory
            {
                Name = line[..colon].Trim(),
                Extensions = (arrow >= 0 ? rest[..arrow] : rest).Trim(),
                Folder = arrow >= 0 ? rest[(arrow + 1)..].Trim() : "",
            };
            if (c.Types().Count == 0) return (list, $"Line {n}: list at least one file type for {c.Name}.");
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
            var types = c.Types().Where(t => t.Length is > 0 and <= 16 && t.All(char.IsLetterOrDigit) && seen.Add(t)).ToList();
            if (string.IsNullOrWhiteSpace(c.Name) || types.Count == 0) continue;
            result.Add(new CustomCategory { Name = c.Name.Trim(), Extensions = string.Join(' ', types), Folder = c.Folder.Trim() });
            if (result.Count == 50) break;
        }
        return result;
    }
}

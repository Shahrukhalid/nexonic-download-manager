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

    public static FileCategory Of(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        foreach (var (cat, exts) in Map) if (exts.Contains(ext)) return cat;
        return FileCategory.Other;
    }
}

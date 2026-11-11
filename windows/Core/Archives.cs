using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Nexonic.Core;

/// <summary>"Unpack when finished": extracts a downloaded .zip, .7z, .rar or .tar (.gz, .bz2, .xz) into a folder next to it.</summary>
public static class Archives
{
    static readonly string[] Kinds = { ".zip", ".7z", ".rar", ".tar", ".tar.gz", ".tgz", ".tar.bz2", ".tbz2", ".tar.xz", ".txz" };

    /// <summary>A finished file that can be unpacked: one of the kinds above, and for split RAR sets only the first part.</summary>
    public static bool IsArchive(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (!Kinds.Any(name.EndsWith)) return false;
        var part = System.Text.RegularExpressions.Regex.Match(name, @"\.part(\d+)\.rar$");
        return !part.Success || int.Parse(part.Groups[1].Value) == 1;
    }

    static string BaseName(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var k in Kinds.OrderByDescending(k => k.Length))
            if (name.EndsWith(k, StringComparison.OrdinalIgnoreCase)) { name = name[..^k.Length]; break; }
        return System.Text.RegularExpressions.Regex.Replace(name, @"\.part0*1$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>Unpacks any supported archive (see IsArchive) into a new folder; returns the folder.</summary>
    public static string Extract(string path, string? password = null) =>
        string.IsNullOrEmpty(password) && string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)
            ? ExtractZip(path)
            : ExtractOther(path, password);

    static string ExtractOther(string path, string? password = null)
    {
        var folder = FolderFor(path);
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        var name = Path.GetFileName(path).ToLowerInvariant();
        var tar = name.EndsWith(".7z") || name.EndsWith(".rar") ? null : File.OpenRead(path);
        var readerOptions = string.IsNullOrEmpty(password) ? new ReaderOptions() : new ReaderOptions { Password = password };
        // .tar (.gz, .bz2, .xz) is read in one pass; 7z and RAR (also split RAR sets) through their archive index.
        using var archive = tar == null ? ArchiveFactory.OpenArchive(new FileInfo(path), readerOptions) : null;
        if (archive != null && string.IsNullOrEmpty(password) && archive.Entries.Any(e => e.IsEncrypted)) throw new InvalidDataException("The archive needs a password.");
        if (archive != null && archive.Entries.Sum(e => Math.Max(0, e.Size)) > MaxUnpackedBytes) throw new InvalidDataException("The archive unpacks to more than 64 GB.");
        using var reader = archive?.ExtractAllEntries() ?? ReaderFactory.OpenReader(tar!, readerOptions);
        Directory.CreateDirectory(folder);
        long written = 0;
        try
        {
            while (reader.MoveToNextEntry())
            {
                var e = reader.Entry;
                if (e.Key is not { Length: > 0 } key) continue;
                if (e.IsEncrypted && string.IsNullOrEmpty(password)) throw new InvalidDataException("The archive needs a password.");
                var target = Path.GetFullPath(Path.Combine(folder, key.Replace('\\', '/')));
                if (!target.StartsWith(root, StringComparison.Ordinal)) continue; // "../" tricks
                if (e.IsDirectory) { Directory.CreateDirectory(target); continue; }
                if (e.LinkTarget != null || File.Exists(target)) continue;   // no links, never overwrite
                if ((written += Math.Max(0, e.Size)) > MaxUnpackedBytes) throw new InvalidDataException("The archive unpacks to more than 64 GB.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                reader.WriteEntryToFile(target, new ExtractionOptions { Overwrite = false, PreserveFileTime = true });
            }
        }
        finally { tar?.Dispose(); }
        return folder;
    }

    /// <summary>Stops at this much unpacked data, so a "zip bomb" cannot fill the disk.</summary>
    public const long MaxUnpackedBytes = 64L * 1024 * 1024 * 1024;

    /// <summary>A folder named after the archive that does not exist yet: "Photos", then "Photos (2)"…</summary>
    public static string FolderFor(string zipPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(zipPath)) ?? ".";
        var name = BaseName(zipPath).Trim();
        if (name.Length == 0) name = "Archive";
        var folder = Path.Combine(dir, name);
        for (var i = 2; Directory.Exists(folder) || File.Exists(folder); i++) folder = Path.Combine(dir, $"{name} ({i})");
        return folder;
    }

    /// <summary>Extracts every entry inside the new folder; entries that would land outside it are skipped.
    /// Returns the folder, or throws with a short reason.</summary>
    public static string ExtractZip(string zipPath)
    {
        var folder = FolderFor(zipPath);
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        long total = 0;
        foreach (var e in zip.Entries) total += Math.Max(0, e.Length);
        if (total > MaxUnpackedBytes) throw new InvalidDataException("The archive unpacks to more than 64 GB.");
        Directory.CreateDirectory(folder);
        foreach (var e in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(folder, e.FullName.Replace('\\', '/')));
            if (!target.StartsWith(root, StringComparison.Ordinal)) continue; // "../" tricks
            if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: false);
        }
        return folder;
    }
}

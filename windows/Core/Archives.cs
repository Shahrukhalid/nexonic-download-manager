using System.IO.Compression;

namespace Nexonic.Core;

/// <summary>"Unzip when finished": extracts a downloaded .zip into a folder next to it.</summary>
public static class Archives
{
    /// <summary>Stops at this much unpacked data, so a "zip bomb" cannot fill the disk.</summary>
    public const long MaxUnpackedBytes = 64L * 1024 * 1024 * 1024;

    public static bool IsZip(string? path) =>
        !string.IsNullOrEmpty(path) && File.Exists(path) && string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>A folder named after the archive that does not exist yet: "Photos", then "Photos (2)"…</summary>
    public static string FolderFor(string zipPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(zipPath)) ?? ".";
        var name = Path.GetFileNameWithoutExtension(zipPath).Trim();
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

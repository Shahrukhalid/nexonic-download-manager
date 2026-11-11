namespace Nexonic.Core;

/// <summary>
/// The app's data folder (settings, the download list, unfinished parts) can live somewhere else, such as a bigger drive.
/// The usual folder keeps only a pointer to it. A move is asked for while the app runs and done at the next start, before
/// anything is open, by copying, checking every file and only then switching and removing the old copies.
/// </summary>
public static class DataLocation
{
    public const string PointerName = "data-location.txt";
    public const string PendingName = "data-move.txt";
    static string? cached;

    public static string Current(string usual)
    {
        if (cached != null) return cached;
        try
        {
            var pointer = Path.Combine(usual, PointerName);
            if (File.Exists(pointer) && File.ReadAllText(pointer).Trim() is { Length: > 0 } p && Path.IsPathFullyQualified(p) && Directory.Exists(p))
                return cached = p;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return cached = usual;
    }

    public static void Forget() => cached = null;

    /// <summary>Why a folder can't take the data, or null when it can.</summary>
    public static string? Problem(string target, string current)
    {
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathFullyQualified(target)) return "Choose a full folder path.";
        var to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
        var cmp = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (string.Equals(to, from, cmp)) return "The data is already in this folder.";
        if (to.StartsWith(from + Path.DirectorySeparatorChar, cmp) || from.StartsWith(to + Path.DirectorySeparatorChar, cmp))
            return "Choose a folder that is not inside the current data folder (or the other way round).";
        if (Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any(e => Path.GetFileName(e) is not (PointerName or PendingName)))
            return "Choose an empty folder.";
        return null;
    }

    /// <summary>Asks for the move at the next start (null = done), or says why it can't be made.</summary>
    public static string? Request(string usual, string target)
    {
        if (Problem(target, Current(usual)) is { } why) return why;
        Directory.CreateDirectory(usual);
        File.WriteAllText(Path.Combine(usual, PendingName), Path.GetFullPath(target));
        return null;
    }

    public static string? Pending(string usual)
    {
        var f = Path.Combine(usual, PendingName);
        try { return File.Exists(f) ? File.ReadAllText(f).Trim() : null; } catch (IOException) { return null; }
    }

    public static void CancelRequest(string usual) { try { File.Delete(Path.Combine(usual, PendingName)); } catch (IOException) { } }

    /// <summary>Does a requested move (call first thing at start-up). Returns null when nothing was asked or the move worked,
    /// else the reason it failed; then the data stays where it was and nothing is lost.</summary>
    public static string? ApplyPending(string usual)
    {
        var target = Pending(usual);
        if (target == null) return null;
        CancelRequest(usual);
        Forget();
        var from = Current(usual);
        if (Problem(target, from) is { } why) return why;
        var copied = new List<string>();
        try
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(from, file);
                if (rel is PointerName or PendingName) continue;
                var dest = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest);
                copied.Add(dest);
                if (new FileInfo(dest).Length != new FileInfo(file).Length) throw new IOException($"{rel} was not copied completely.");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            foreach (var c in copied) try { File.Delete(c); } catch (IOException) { }
            return "The data could not be moved: " + e.Message;
        }
        // Switch first, then remove the old copies (the pointer and an empty usual folder stay).
        var samePlace = string.Equals(Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(usual).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        if (samePlace) File.Delete(Path.Combine(usual, PointerName));
        else File.WriteAllText(Path.Combine(usual, PointerName), Path.GetFullPath(target));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).ToList())
        {
            var rel = Path.GetRelativePath(from, file);
            if (rel is PointerName or PendingName) continue;
            try { File.Delete(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (IOException) { }
        if (!string.Equals(from, usual, StringComparison.OrdinalIgnoreCase))
            try { if (!Directory.EnumerateFileSystemEntries(from).Any()) Directory.Delete(from); } catch (IOException) { }
        Forget();
        return null;
    }
}

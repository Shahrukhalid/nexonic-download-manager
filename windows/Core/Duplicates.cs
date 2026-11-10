namespace Nexonic.Core;

/// <summary>
/// Downloads that would repeat one you have: the same link in the list (still to do, or finished with its file still
/// there), or a file of the same name already in the folder. A repeat is never written over an existing file: "download
/// again" always saves under a new name ("file (1).zip").
/// </summary>
public static class Duplicates
{
    public enum Kind { None, InList, Downloaded, FileExists }

    public static (Kind Kind, DownloadRecord? Record) Find(IEnumerable<DownloadRecord> list, string url, string fileName, string folder)
    {
        foreach (var r in list)
        {
            if (!string.Equals(Normalize(r.Url), Normalize(url), StringComparison.Ordinal)) continue;
            if (r.Status != DownloadStatus.Finished) return (Kind.InList, r);
            if (r.SavedPath is { } p && (File.Exists(p) || Directory.Exists(p))) return (Kind.Downloaded, r);
        }
        if (fileName.Length > 0 && folder.Length > 0 && File.Exists(Path.Combine(folder, FileNames.Sanitize(fileName)))) return (Kind.FileExists, null);
        return (Kind.None, null);
    }

    /// <summary>The same address written differently (case of the scheme and host, a trailing #fragment) counts as the same.</summary>
    public static string Normalize(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) ? u.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped) : url.Trim();

    public static string Describe(Kind k, string name) => k switch
    {
        Kind.InList => $"{name} is already in the list.",
        Kind.Downloaded => $"{name} was downloaded before and the file is still there.",
        Kind.FileExists => $"A file named {name} is already in the folder.",
        _ => "",
    };
}

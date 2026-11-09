using System.Text.Json;

namespace Nexonic.Core;

/// <summary>
/// Download limit (Options → Connection): at most N MB every H hours. When the amount is reached, downloads stop and wait
/// for the next period; they start again by themselves. The amount used so far is kept across restarts.
/// </summary>
public sealed class DataCap
{
    public DateTime PeriodStart { get; set; }
    public long Used { get; set; }

    /// <summary>Starts a new period when the old one is over. Returns true when it did.</summary>
    public bool Roll(DateTime now, int hours)
    {
        var length = TimeSpan.FromHours(Math.Clamp(hours, 1, 24 * 31));
        if (PeriodStart != default && now >= PeriodStart && now - PeriodStart < length) return false;
        PeriodStart = now;
        Used = 0;
        return true;
    }

    /// <summary>Counts received bytes; true when the limit is reached.</summary>
    public bool Add(long bytes, long limitMB)
    {
        if (bytes > 0) Used += bytes;
        return Reached(limitMB);
    }

    public bool Reached(long limitMB) => limitMB > 0 && Used >= limitMB * 1024 * 1024;

    public DateTime NextPeriod(int hours) => PeriodStart + TimeSpan.FromHours(Math.Clamp(hours, 1, 24 * 31));

    public static string File => Path.Combine(AppPaths.Support, "data-limit.json");

    public static DataCap Load()
    {
        try { return JsonSerializer.Deserialize<DataCap>(System.IO.File.ReadAllText(File)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try { Directory.CreateDirectory(AppPaths.Support); System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this)); }
        catch { /* the amount is counted again from zero next time */ }
    }

    /// <summary>"512 MB of 2 GB used, resets 14:00".</summary>
    public string Describe(long limitMB, int hours)
    {
        static string Size(double mb) => mb >= 1024 ? $"{mb / 1024:0.##} GB" : $"{mb:0} MB";
        return $"{Size(Used / 1048576.0)} of {Size(limitMB)} used, resets {NextPeriod(hours):ddd HH:mm}";
    }
}

/// <summary>The download list's extra columns (View → Columns), shared by Windows and Linux.</summary>
public static class ListColumns
{
    public static readonly (string Key, string Title)[] Optional =
        { ("queue", "Queue"), ("added", "Added"), ("finished", "Finished"), ("folder", "Folder"), ("resume", "Can Resume") };

    public static readonly List<string> HiddenByDefault = new() { "finished", "folder", "resume" };

    public static string When(DateTime? t) => t is { } d ? d.ToString("d MMM yyyy, HH:mm") : "";

    /// <summary>Where the file is (or will be) saved.</summary>
    public static string Folder(DownloadRecord r) =>
        r.SavedPath is { Length: > 0 } p && Path.GetDirectoryName(p) is { Length: > 0 } d ? d : r.Folder;

    /// <summary>"Yes" when a stopped download continues where it left off, "No" when it starts again, "" before that is known.</summary>
    public static string Resume(DownloadRecord r)
    {
        if (r.Status == DownloadStatus.Finished) return "";
        if (r.Torrent != null || r.SiteVideo == true || r.IsStream == true || r.AcceptsRanges) return "Yes";
        return r.Size is long || r.Received > 0 ? "No" : "";
    }
}

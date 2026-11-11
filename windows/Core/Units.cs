using System.Globalization;

namespace Nexonic.Core;

/// <summary>Sizes, speeds and dates the way the user chose to see them (Options → Appearance). Set by the app from the
/// settings; the defaults match earlier versions.</summary>
public static class Units
{
    /// <summary>"binary" (1 KB = 1024 bytes, the default), "iec" (1024, written KiB/MiB) or "decimal" (1 KB = 1000 bytes).</summary>
    public static volatile string SizeStyle = "binary";
    /// <summary>"bytes" (MB/s, the default) or "bits" (Mbit/s, like internet plans).</summary>
    public static volatile string SpeedStyle = "bytes";
    /// <summary>"absolute" (the computer's date format, the default) or "relative" ("5 min ago").</summary>
    public static volatile string DateStyle = "absolute";

    public static readonly string[] SizeStyles = { "binary", "iec", "decimal" };
    public static readonly string[] SpeedStyles = { "bytes", "bits" };
    public static readonly string[] DateStyles = { "absolute", "relative" };

    public static void Apply(AppSettings s)
    {
        SizeStyle = SizeStyles.Contains(s.SizeUnits) ? s.SizeUnits : "binary";
        SpeedStyle = SpeedStyles.Contains(s.SpeedUnits) ? s.SpeedUnits : "bytes";
        DateStyle = DateStyles.Contains(s.DateStyle) ? s.DateStyle : "absolute";
    }

    public static string Size(long bytes) => Size(bytes, SizeStyle);

    public static string Size(long bytes, string style)
    {
        var step = style == "decimal" ? 1000.0 : 1024.0;
        string[] u = style == "iec" ? new[] { "B", "KiB", "MiB", "GiB", "TiB", "PiB" } : new[] { "B", "KB", "MB", "GB", "TB", "PB" };
        double v = Math.Max(0, bytes);
        var i = 0;
        while (v >= step && i < u.Length - 1) { v /= step; i++; }
        return string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", v, u[i]);
    }

    /// <summary>A speed with its unit ("2.5 MB/s" or "20 Mbit/s"); bits always count in thousands.</summary>
    public static string Speed(double bytesPerSecond) => Speed(bytesPerSecond, SpeedStyle, SizeStyle);

    public static string Speed(double bytesPerSecond, string speedStyle, string sizeStyle)
    {
        if (speedStyle != "bits") return Size((long)bytesPerSecond, sizeStyle) + "/s";
        string[] u = { "bit/s", "kbit/s", "Mbit/s", "Gbit/s", "Tbit/s" };
        var v = Math.Max(0, bytesPerSecond) * 8;
        var i = 0;
        while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
        return string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", v, u[i]);
    }

    public static string Date(DateTime? t) => Date(t, DateStyle, DateTime.Now);

    /// <summary>Relative: "Just now", "5 min ago", "3 h ago", "Yesterday 14:05", "4 days ago", then the date itself.
    /// Future times (a clock that moved back) and anything older than a week show the date.</summary>
    public static string Date(DateTime? t, string style, DateTime now)
    {
        if (t is not { } d) return "";
        var absolute = d.ToString("g", CultureInfo.CurrentCulture);
        if (style != "relative") return absolute;
        var ago = now - d;
        if (ago < TimeSpan.Zero) return absolute;
        if (ago.TotalMinutes < 1) return L.T("Just now");
        if (ago.TotalMinutes < 60) return string.Format(L.T("{0} min ago"), (int)ago.TotalMinutes);
        if (d.Date == now.Date) return string.Format(L.T("{0} h ago"), (int)ago.TotalHours);
        if (d.Date == now.Date.AddDays(-1)) return L.T("Yesterday") + " " + d.ToString("t", CultureInfo.CurrentCulture);
        var days = (now.Date - d.Date).Days;
        return days < 7 ? string.Format(L.T("{0} days ago"), days) : absolute;
    }
}

/// <summary>Finished downloads whose file was moved or deleted outside the app.</summary>
public static class MissingFiles
{
    /// <summary>True when a finished download's file (or folder, for torrents and galleries) is no longer on disk.
    /// Downloads without a saved path are never treated as missing.</summary>
    public static bool IsMissing(DownloadRecord r) =>
        r.Status == DownloadStatus.Finished && r.SavedPath is { Length: > 0 } p && !File.Exists(p) && !Directory.Exists(p);

    public static List<DownloadRecord> Find(IEnumerable<DownloadRecord> records) => records.Where(IsMissing).ToList();
}

/// <summary>Filters a list of links: words find links that contain them, * and ? are wildcards, and a leading ! leaves
/// matching links out. "*.jpg !*thumb*" keeps the jpg links that are not thumbnails. Case does not matter.</summary>
public static class LinkFilter
{
    public static List<string> Apply(IEnumerable<string> links, string? filter)
    {
        var terms = (filter ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var include = terms.Where(t => !t.StartsWith('!')).ToList();
        var exclude = terms.Where(t => t.StartsWith('!') && t.Length > 1).Select(t => t[1..]).ToList();
        return links.Where(l => (include.Count == 0 || include.Any(t => Matches(l, t))) && !exclude.Any(t => Matches(l, t))).ToList();
    }

    public static bool Matches(string link, string term)
    {
        if (term.IndexOfAny(new[] { '*', '?' }) < 0) return link.Contains(term, StringComparison.OrdinalIgnoreCase);
        // Wildcards match the whole link, or its file name (so "*.jpg" works on links with a query).
        var rx = "^" + System.Text.RegularExpressions.Regex.Escape(term).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        var options = System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        if (System.Text.RegularExpressions.Regex.IsMatch(link, rx, options)) return true;
        var path = Uri.TryCreate(link, UriKind.Absolute, out var u) ? u.AbsolutePath : link;
        return System.Text.RegularExpressions.Regex.IsMatch(path, rx, options)
            || System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), rx, options);
    }
}

using System.Text.RegularExpressions;

namespace Nexonic.Core;

/// <summary>Pasted links: several at once, and the real file behind a search engine's or site's redirect link.</summary>
public static class Links
{
    /// <summary>Links pasted together: one per line, or separated by spaces or commas.</summary>
    public static List<string> Split(string text) =>
        Regex.Replace(text ?? "", @",\s*(?=https?://)", "\n", RegexOptions.IgnoreCase)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim(',', ';'))
            .Where(s => s.Length > 0)
            .ToList();

    /// <summary>The real address behind a redirect link: Google Images ("imgres?imgurl="), Google results ("url?q="),
    /// Facebook ("l.php?u=") and Reddit ("out.reddit.com?url="). Other links are returned unchanged.</summary>
    public static string Unwrap(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u)) return url ?? "";
        var host = u.Host.ToLowerInvariant();
        var query = System.Web.HttpUtility.ParseQueryString(u.Query);
        string? Param(params string[] names)
        {
            foreach (var n in names)
                if (query[n] is { } v && Uri.TryCreate(v, UriKind.Absolute, out var t) && (t.Scheme == "http" || t.Scheme == "https"))
                    return v;
            return null;
        }
        var google = host.StartsWith("google.") || host.StartsWith("www.google.") || host.StartsWith("images.google.");
        if (google && u.AbsolutePath == "/imgres" && Param("imgurl") is { } img) return img;
        if (google && u.AbsolutePath == "/url" && Param("q", "url") is { } q) return q;
        if ((host == "l.facebook.com" || host == "lm.facebook.com") && u.AbsolutePath == "/l.php" && Param("u") is { } fb) return fb;
        if (host == "out.reddit.com" && Param("url") is { } r) return r;
        return url!.Trim();
    }
}

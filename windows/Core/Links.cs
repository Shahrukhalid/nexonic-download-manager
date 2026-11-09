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
        return Direct(url!.Trim());
    }

    static readonly Regex DriveId = new(@"^[A-Za-z0-9_-]{10,}$", RegexOptions.Compiled);

    /// <summary>
    /// The file itself behind a share page: Google Drive files (and Docs, Sheets and Slides as Office files), Dropbox
    /// share links and GitHub file pages. Other links are returned unchanged.
    /// </summary>
    public static string Direct(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return url ?? "";
        var host = u.Host.ToLowerInvariant();
        var path = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var query = System.Web.HttpUtility.ParseQueryString(u.Query);
        string? Id(string? v) => v != null && DriveId.IsMatch(v) ? v : null;
        if (host is "drive.google.com" or "docs.google.com" or "drive.usercontent.google.com")
        {
            // Docs, Sheets and Slides: the document exported as an Office file.
            if (host == "docs.google.com" && path.Length >= 3 && path[1] == "d" && Id(path[2]) is { } doc)
            {
                var format = path[0] switch { "document" => "docx", "spreadsheets" => "xlsx", "presentation" => "pptx", _ => null };
                if (format != null) return $"https://docs.google.com/{path[0]}/d/{doc}/export?format={format}";
            }
            var id = path.Length >= 3 && path[0] == "file" && path[1] == "d" ? Id(path[2])
                : path.Length >= 1 && path[0] is "open" or "uc" or "download" ? Id(query["id"]) : null;
            if (id != null)
            {
                var key = query["resourcekey"];
                return $"https://drive.usercontent.google.com/download?id={id}&export=download&confirm=t"
                       + (key is { Length: > 0 } ? "&resourcekey=" + Uri.EscapeDataString(key) : "");
            }
            return url!.Trim();
        }
        if (host is "dropbox.com" or "www.dropbox.com" && path.Length >= 2 && path[0] is "s" or "scl" or "sh")
        {
            query["dl"] = "1";
            return new UriBuilder(u) { Host = "www.dropbox.com", Query = query.ToString() }.Uri.AbsoluteUri;
        }
        if (host == "github.com" && path.Length >= 5 && path[2] == "blob")
            return "https://raw.githubusercontent.com/" + string.Join('/', path.Take(2).Concat(path.Skip(3)));
        return url!.Trim();
    }
}

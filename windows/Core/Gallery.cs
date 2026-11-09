using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexonic.Core;

/// <summary>
/// Image galleries (Tasks → Download Images from Page): the pictures of one web page, in page order. Full-size links
/// around thumbnails (a href to a picture) come before the thumbnails themselves, the largest srcset candidate is used,
/// lazy-loading attributes are read, and icons and tracking pixels smaller than the minimum are left out.
/// </summary>
public static class ImageGallery
{
    public sealed record Image(Uri Url, string Name);

    static readonly HashSet<string> Types = new() { "jpg", "jpeg", "png", "gif", "webp", "avif", "bmp", "tif", "tiff", "heic", "jxl", "svg" };
    static readonly Regex Tag = new(@"<(img|a|source|meta|link)\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Attribute = new(@"([\w:-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>""']+))", RegexOptions.Compiled);
    static readonly Regex TitleTag = new(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    const int MaxPage = 8 * 1024 * 1024;
    public const int MaxImages = 2000;

    public static bool LooksLikeImage(Uri u) => Types.Contains(Path.GetExtension(u.AbsolutePath).TrimStart('.').ToLowerInvariant());

    /// <summary>The page's pictures. <paramref name="minSide"/> leaves out images whose width or height attribute is smaller.</summary>
    public static List<Image> Find(string html, Uri page, int minSide = 100, bool includeSvg = false)
    {
        var seen = new HashSet<string>();
        var result = new List<Image>();
        void Add(string? raw, bool mustLookLikeImage)
        {
            if (string.IsNullOrWhiteSpace(raw) || result.Count >= MaxImages) return;
            raw = WebUtility.HtmlDecode(raw.Trim());
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(page, raw, out var u)) return;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return;
            if (mustLookLikeImage && !LooksLikeImage(u)) return;
            if (!includeSvg && u.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return;
            var clean = new UriBuilder(u) { Fragment = "" }.Uri;
            if (seen.Add(clean.AbsoluteUri)) result.Add(new Image(clean, FileNames.FromUrl(clean.AbsoluteUri)));
        }

        foreach (Match m in Tag.Matches(html))
        {
            var name = m.Groups[1].Value.ToLowerInvariant();
            var a = Attributes(m.Groups[2].Value);
            switch (name)
            {
                case "a":
                    // Gallery thumbnails usually link to the full-size picture.
                    Add(a.GetValueOrDefault("href"), mustLookLikeImage: true);
                    Add(a.GetValueOrDefault("data-full") ?? a.GetValueOrDefault("data-large"), mustLookLikeImage: false);
                    break;
                case "img":
                    if (TooSmall(a, minSide)) break;
                    var best = Largest(a.GetValueOrDefault("srcset")) ?? Largest(a.GetValueOrDefault("data-srcset"));
                    Add(best ?? a.GetValueOrDefault("data-src") ?? a.GetValueOrDefault("data-original") ?? a.GetValueOrDefault("data-lazy-src")
                        ?? a.GetValueOrDefault("src"), mustLookLikeImage: false);
                    break;
                case "source":
                    if (a.GetValueOrDefault("type") is { } t && !t.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) break;
                    Add(Largest(a.GetValueOrDefault("srcset")), mustLookLikeImage: false);
                    break;
                case "meta":
                    if ((a.GetValueOrDefault("property") ?? a.GetValueOrDefault("name")) is "og:image" or "twitter:image")
                        Add(a.GetValueOrDefault("content"), mustLookLikeImage: false);
                    break;
                case "link":
                    if (a.GetValueOrDefault("rel")?.Contains("image_src") == true) Add(a.GetValueOrDefault("href"), mustLookLikeImage: false);
                    break;
            }
        }
        return result;
    }

    static Dictionary<string, string> Attributes(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Attribute.Matches(text))
            d.TryAdd(m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value);
        return d;
    }

    static bool TooSmall(Dictionary<string, string> a, int minSide)
    {
        static int? Size(string? v) => v != null && int.TryParse(v.Trim().TrimEnd('x', 'p', 'X', 'P'), out var n) ? n : null;
        return minSide > 0 && (Size(a.GetValueOrDefault("width")) < minSide || Size(a.GetValueOrDefault("height")) < minSide);
    }

    /// <summary>The largest candidate of a srcset ("a.jpg 480w, b.jpg 1080w" → b.jpg; "a.jpg 1x, b.jpg 2x" → b.jpg).</summary>
    public static string? Largest(string? srcset)
    {
        if (string.IsNullOrWhiteSpace(srcset)) return null;
        string? best = null;
        var bestScore = -1.0;
        // Candidates are separated by commas followed by whitespace (addresses may contain commas themselves).
        foreach (var candidate in Regex.Split(srcset.Trim(), @",\s+"))
        {
            var parts = candidate.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var score = 1.0;
            if (parts.Length > 1 && parts[1].Length > 1 && double.TryParse(parts[1][..^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                score = parts[1][^1] is 'w' or 'W' ? v : v * 1000;
            if (score > bestScore) { bestScore = score; best = parts[0].TrimEnd(','); }
        }
        return best;
    }

    /// <summary>The page title, for the folder the pictures are saved in ("Gallery from example.com" without one).</summary>
    public static string FolderName(string html, Uri page)
    {
        var title = TitleTag.Match(html) is { Success: true } t ? WebUtility.HtmlDecode(t.Groups[1].Value).Trim() : "";
        title = Regex.Replace(title, @"\s+", " ");
        if (title.Length > 80) title = title[..80].TrimEnd();
        var name = FileNames.Sanitize(title);
        return name.Length > 0 && name != "download" ? name : FileNames.Sanitize($"Gallery from {page.Host}");
    }

    /// <summary>Downloads the page (at most 8 MB of HTML) with the app's connection settings.</summary>
    public static async Task<string> LoadAsync(Uri page, ProxySettings proxy, CancellationToken ct = default)
    {
        using var http = new HttpClient(Network.Handler(proxy)) { Timeout = TimeSpan.FromSeconds(30) };
        using var res = await Network.SendAsync(http, page, u =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, u);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; NexonicDownloadManager)");
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            return req;
        }, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        await using var s = await res.Content.ReadAsStreamAsync(ct);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(chunk, ct)) > 0 && buffer.Length < MaxPage) buffer.Write(chunk, 0, n);
        var charset = res.Content.Headers.ContentType?.CharSet;
        Encoding enc;
        try { enc = string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); } catch { enc = Encoding.UTF8; }
        return enc.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}

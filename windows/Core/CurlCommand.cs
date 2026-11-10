using System.Text;

namespace Nexonic.Core;

/// <summary>
/// cURL commands in and out: a browser's "Copy as cURL" (bash or Windows cmd) becomes a download with the same address,
/// referrer, cookies, browser name and login; a download can be copied as a cURL command to fetch it elsewhere.
/// </summary>
public static class CurlCommand
{
    /// <summary>What a cURL command asks for. <see cref="Ignored"/> names the parts a download cannot repeat.</summary>
    public sealed record Request(string Url, string? Referrer, string? Cookies, string? UserAgent, string? Username, string? Password,
        IReadOnlyList<string> Ignored);

    public static bool Looks(string? text) =>
        text is { } t && (t.TrimStart().StartsWith("curl ", StringComparison.OrdinalIgnoreCase)
                          || t.TrimStart().StartsWith("curl.exe ", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads a cURL command; null with a reason when it is not one a download can repeat.</summary>
    public static (Request? Request, string? Problem) Parse(string text)
    {
        List<string> args;
        try { args = Split(text); }
        catch (FormatException e) { return (null, e.Message); }
        if (args.Count == 0 || !(args[0].Equals("curl", StringComparison.OrdinalIgnoreCase) || args[0].Equals("curl.exe", StringComparison.OrdinalIgnoreCase)))
            return (null, "This is not a cURL command.");
        string? url = null, referrer = null, agent = null, user = null, pass = null;
        var cookies = new List<string>();
        var ignored = new List<string>();
        for (var i = 1; i < args.Count; i++)
        {
            var a = args[i];
            string Next() => ++i < args.Count ? args[i] : throw new FormatException($"{a} needs a value.");
            try
            {
                switch (a)
                {
                    case "-H" or "--header":
                        var h = Next();
                        var colon = h.IndexOf(':');
                        if (colon <= 0) break;
                        var name = h[..colon].Trim();
                        var value = h[(colon + 1)..].Trim();
                        switch (name.ToLowerInvariant())
                        {
                            case "referer": referrer = value; break;
                            case "cookie": cookies.Add(value); break;
                            case "user-agent": agent = value; break;
                            case "authorization" when value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase):
                                try
                                {
                                    var pair = Encoding.UTF8.GetString(Convert.FromBase64String(value[6..].Trim()));
                                    var c = pair.IndexOf(':');
                                    if (c > 0) { user = pair[..c]; pass = pair[(c + 1)..]; }
                                }
                                catch (FormatException) { ignored.Add(name); }
                                break;
                            // Sent by every request anyway, or describing the browser rather than the file.
                            case "accept" or "accept-language" or "accept-encoding" or "connection" or "cache-control" or "pragma"
                                or "upgrade-insecure-requests" or "dnt" or "priority" or "te" or "range":
                                break;
                            default:
                                if (!name.StartsWith("sec-", StringComparison.OrdinalIgnoreCase)) ignored.Add(name);
                                break;
                        }
                        break;
                    case "-b" or "--cookie":
                        var cookie = Next();
                        if (cookie.Contains('=')) cookies.Add(cookie); else ignored.Add("cookie file");
                        break;
                    case "-A" or "--user-agent": agent = Next(); break;
                    case "-e" or "--referer": referrer = Next().Split(";auto")[0]; break;
                    case "-u" or "--user":
                        var up = Next();
                        var k = up.IndexOf(':');
                        (user, pass) = k >= 0 ? (up[..k], up[(k + 1)..]) : (up, "");
                        break;
                    case "--url": url = Next(); break;
                    case "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-urlencode" or "-F" or "--form" or "--json":
                        return (null, "This command sends data (a form or POST request); downloads can only repeat plain requests.");
                    case "-X" or "--request":
                        var method = Next();
                        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                            return (null, $"This command uses {method.ToUpperInvariant()}; downloads can only repeat GET requests.");
                        break;
                    // Options that take a value but do not change what is downloaded.
                    case "-o" or "--output" or "--max-time" or "-m" or "--connect-timeout" or "--retry" or "-w" or "--write-out"
                        or "--limit-rate" or "-x" or "--proxy" or "-r" or "--range" or "-c" or "--cookie-jar":
                        Next();
                        break;
                    default:
                        if (a.StartsWith('-')) break;  // switches such as --compressed, -L, -s, --insecure
                        url ??= a;
                        break;
                }
            }
            catch (FormatException e) { return (null, e.Message); }
        }
        if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https" or "ftp"))
            return (null, "The cURL command has no web address.");
        return (new Request(u.AbsoluteUri, referrer, cookies.Count > 0 ? string.Join("; ", cookies) : null, agent, user, pass,
            ignored.Distinct(StringComparer.OrdinalIgnoreCase).ToList()), null);
    }

    /// <summary>A bash cURL command that fetches the same file (with its cookies and login: share it with care).</summary>
    public static string ToCommand(DownloadRecord r)
    {
        var sb = new StringBuilder("curl -L -C - -o ").Append(Quote(r.FileName.Length > 0 ? r.FileName : "download"));
        if (!string.IsNullOrEmpty(r.Referrer)) sb.Append(" -e ").Append(Quote(r.Referrer));
        if (!string.IsNullOrEmpty(r.UserAgent)) sb.Append(" -A ").Append(Quote(r.UserAgent));
        if (!string.IsNullOrEmpty(r.Cookies)) sb.Append(" -b ").Append(Quote(r.Cookies));
        if (!string.IsNullOrEmpty(r.Username)) sb.Append(" -u ").Append(Quote(r.Username + ":" + (r.Password ?? "")));
        return sb.Append(' ').Append(Quote(r.Url)).ToString();
    }

    static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Words of a command as a shell reads them: bash quoting ('…', "…", $'…', \ and line continuations) and
    /// Windows cmd quoting ("…", ^ escapes and ^ line continuations, as browsers copy it).</summary>
    public static List<string> Split(string text)
    {
        var words = new List<string>();
        var cur = new StringBuilder();
        var inWord = false;
        var cmd = text.Contains("^\n") || text.Contains("^\r\n") || text.Contains("^\"");
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (cmd && ch == '^')
            {
                if (i + 1 < text.Length && text[i + 1] is '\r' or '\n') { i++; if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; continue; }
                // ^" is how browsers quote for cmd: it reaches curl as a quote, so it opens a quoted part.
                if (i + 1 < text.Length && text[i + 1] == '"') { i++; ch = '"'; }
                else
                {
                    if (i + 1 < text.Length) { cur.Append(text[++i]); inWord = true; }
                    continue;
                }
            }
            if (!cmd && ch == '\\')
            {
                if (i + 1 < text.Length && text[i + 1] is '\r' or '\n') { i++; if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; continue; }
                if (i + 1 < text.Length) { cur.Append(text[++i]); inWord = true; }
                continue;
            }
            if (char.IsWhiteSpace(ch))
            {
                if (inWord) { words.Add(cur.ToString()); cur.Clear(); inWord = false; }
                continue;
            }
            inWord = true;
            if (!cmd && ch == '$' && i + 1 < text.Length && text[i + 1] == '\'')
            {
                i += 2;
                for (; i < text.Length && text[i] != '\''; i++)
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        var e = text[++i];
                        cur.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => e });
                    }
                    else cur.Append(text[i]);
                }
                if (i >= text.Length) throw new FormatException("A quote is not closed.");
                continue;
            }
            if (!cmd && ch == '\'')
            {
                var end = text.IndexOf('\'', i + 1);
                if (end < 0) throw new FormatException("A quote is not closed.");
                cur.Append(text, i + 1, end - i - 1);
                i = end;
                continue;
            }
            if (ch == '"')
            {
                i++;
                for (; i < text.Length && text[i] != '"'; i++)
                {
                    if (cmd && text[i] == '^' && i + 1 < text.Length)
                    {
                        if (text[i + 1] == '"') { i++; break; }  // ^" closes what ^" opened
                        cur.Append(text[++i]);
                        continue;
                    }
                    if (!cmd && text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\' or '$' or '`') { cur.Append(text[++i]); continue; }
                    cur.Append(text[i]);
                }
                if (i >= text.Length) throw new FormatException("A quote is not closed.");
                continue;
            }
            cur.Append(ch);
        }
        if (inWord) words.Add(cur.ToString());
        return words;
    }
}

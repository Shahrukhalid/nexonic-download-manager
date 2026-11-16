using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexonic.Core;

/// <summary>
/// Reads download lists saved by other download managers, so switching keeps the downloads you had. Recognises:
/// <list type="bullet">
/// <item>angle-bracket lists: "&lt;", the address, "name: value" lines (referer, cookie, user-agent, username, password),
/// "&gt;" (the export format of several Windows download managers);</item>
/// <item>input files: an address line (tab-separated mirrors) followed by indented "option=value" lines
/// (out, referer, user-agent, header=Cookie: …, http-user, http-passwd, checksum=sha-256=…);</item>
/// <item>JSON: an array (or an object holding one) of objects with url/link/href and an optional name/filename;</item>
/// <item>web pages and bookmark files: every link;</item>
/// <item>anything else: every address in the text.</item>
/// </list>
/// Folders from the other computer are not used: the downloads go where you choose. Only web addresses are kept.
/// </summary>
public static class DownloadLists
{
    public const int MaxEntries = 20000;
    public const int MaxBytes = 32 * 1024 * 1024;

    public enum Format { Blocks, InputFile, Json, Html, Text }

    public static (List<DownloadJob> Jobs, Format Format) Read(string text)
    {
        text ??= "";
        if (text.Length > MaxBytes) text = text[..MaxBytes];
        var t = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        var (jobs, format) =
            t.StartsWith('<') && Regex.IsMatch(t, @"^<\s*\r?\n\s*\S+://", RegexOptions.None, Timeout) ? (Blocks(t), Format.Blocks)
            : t.StartsWith('[') || t.StartsWith('{') ? (Json(t), Format.Json)
            : Regex.IsMatch(t, @"<a\s[^>]*href\s*=", RegexOptions.IgnoreCase, Timeout) ? (Html(t), Format.Html)
            : Regex.IsMatch(t, @"^[ \t]+[a-z][a-z0-9-]*=", RegexOptions.Multiline, Timeout) ? (InputFile(t), Format.InputFile)
            : (Links.Split(t).Select(u => new DownloadJob(u)).ToList(), Format.Text);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = jobs.Where(j => Web(j.Url) && seen.Add(j.Url)).Take(MaxEntries).ToList();
        return (kept, format);
    }

    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    static bool Web(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.Host.Length > 0;

    static string? Name(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = FileNames.Sanitize(Path.GetFileName(name.Replace('\\', '/').TrimEnd('/')));
        return string.IsNullOrWhiteSpace(n) || n is "." or ".." ? null : n;
    }

    static string? Value(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    static List<DownloadJob> Blocks(string text)
    {
        var jobs = new List<DownloadJob>();
        List<string>? block = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim() == "<") { block = new List<string>(); continue; }
            if (line.Trim() == ">") { if (block is { Count: > 0 }) jobs.Add(FromBlock(block)); block = null; continue; }
            block?.Add(line);
        }
        return jobs;
    }

    static DownloadJob FromBlock(List<string> lines)
    {
        string? referrer = null, cookie = null, agent = null, user = null, pass = null, name = null;
        foreach (var line in lines.Skip(1))
        {
            var c = line.IndexOf(':');
            if (c <= 0) continue;
            var value = Value(line[(c + 1)..]);
            switch (line[..c].Trim().ToLowerInvariant())
            {
                case "referer" or "referrer": referrer = value; break;
                case "cookie": cookie = value; break;
                case "user-agent": agent = value; break;
                case "username": user = value; break;
                case "password": pass = value; break;
                case "filename" or "name": name = value; break;
            }
        }
        var job = new DownloadJob(lines[0].Trim(), Name(name), referrer, cookie, agent);
        return user != null ? job with { Username = user, Password = pass ?? "" } : job;
    }

    static List<DownloadJob> InputFile(string text)
    {
        var jobs = new List<DownloadJob>();
        List<string>? urls = null;
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cookies = new List<string>();
        void Flush()
        {
            if (urls is { Count: > 0 })
            {
                opts.TryGetValue("out", out var name);
                opts.TryGetValue("referer", out var referrer);
                opts.TryGetValue("user-agent", out var agent);
                var job = new DownloadJob(urls[0], Name(name), Value(referrer), cookies.Count > 0 ? string.Join("; ", cookies) : null, Value(agent))
                    { Mirrors = urls.Skip(1).ToList() };
                if (opts.TryGetValue("http-user", out var u) && Value(u) is { } user)
                    job = job with { Username = user, Password = opts.TryGetValue("http-passwd", out var p) ? p : "" };
                if (opts.TryGetValue("checksum", out var sum) && Regex.Match(sum, @"^sha-?256=([0-9a-fA-F]{64})$", RegexOptions.None, Timeout) is { Success: true } m)
                    job = job with { Checksum = m.Groups[1].Value.ToLowerInvariant() };
                jobs.Add(job);
            }
            urls = null; opts.Clear(); cookies.Clear();
        }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;
            if (char.IsWhiteSpace(line[0]))
            {
                if (urls == null) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                if (key.Equals("header", StringComparison.OrdinalIgnoreCase))
                {
                    var c = value.IndexOf(':');
                    if (c > 0 && value[..c].Trim().Equals("cookie", StringComparison.OrdinalIgnoreCase)) cookies.Add(value[(c + 1)..].Trim());
                    else if (c > 0 && value[..c].Trim().Equals("referer", StringComparison.OrdinalIgnoreCase)) opts["referer"] = value[(c + 1)..].Trim();
                }
                else opts[key] = value;
                continue;
            }
            Flush();
            urls = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        Flush();
        return jobs;
    }

    static List<DownloadJob> Json(string text)
    {
        var jobs = new List<DownloadJob>();
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            Walk(doc.RootElement, jobs, 0);
        }
        catch (JsonException) { return Links.Split(text).Select(u => new DownloadJob(u)).ToList(); }
        return jobs;
    }

    static void Walk(JsonElement e, List<DownloadJob> jobs, int depth)
    {
        if (depth > 8 || jobs.Count >= MaxEntries) return;
        if (e.ValueKind == JsonValueKind.Array) { foreach (var x in e.EnumerateArray()) Walk(x, jobs, depth + 1); return; }
        if (e.ValueKind != JsonValueKind.Object) return;
        string? Str(params string[] names)
        {
            foreach (var p in e.EnumerateObject())
                if (names.Any(n => p.NameEquals(n) || p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) && p.Value.ValueKind == JsonValueKind.String)
                    return Value(p.Value.GetString());
            return null;
        }
        if (Str("url", "link", "href", "uri", "address", "source") is { } url)
        {
            jobs.Add(new DownloadJob(url, Name(Str("filename", "fileName", "file", "name", "title")), Str("referrer", "referer"), null, null));
            return;
        }
        foreach (var p in e.EnumerateObject())
            if (p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) Walk(p.Value, jobs, depth + 1);
    }

    static List<DownloadJob> Html(string text) =>
        Regex.Matches(text, @"<a\s[^>]*?href\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase, Timeout)
            .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).Trim())
            .Select(u => new DownloadJob(u)).ToList();
}

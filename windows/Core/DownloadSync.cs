using System.Net;
using System.Net.Http.Headers;

namespace Nexonic.Core;

/// <summary>
/// Keeps finished files in sync with the server. A file with a sync interval is checked when it is due, with one small
/// request (the first byte, asking "only if changed"). It is downloaded again only when the server reports a new version
/// (a different ETag or Last-Modified, or a different size when the server names no version), and the new download
/// replaces the old file only once it is complete and checked, so a failed update never loses the copy you have.
/// </summary>
public static class DownloadSync
{
    /// <summary>The intervals offered in the menus, in hours.</summary>
    public static readonly int[] Choices = { 1, 6, 24, 168 };

    public enum Result { Unchanged, Changed, Failed }

    public static string Describe(int hours) => hours switch
    {
        1 => "Every Hour",
        24 => "Every Day",
        168 => "Every Week",
        _ => $"Every {hours} Hours",
    };

    /// <summary>A finished, synced file whose interval has passed (a file whose check failed waits for the next one).</summary>
    public static bool Due(DownloadRecord r, DateTime now) =>
        r.SyncHours is > 0 and var hours && r.Status == DownloadStatus.Finished && r.Torrent == null && r.SiteVideo != true
        && r.IsStream != true && (r.SyncChecked is not { } at || now - at >= TimeSpan.FromHours(hours) || at > now);

    /// <summary>What the server's answer says about the file we have.</summary>
    public static Result Compare(DownloadRecord r, HttpStatusCode status, string? validator, long? size)
    {
        if (status == HttpStatusCode.NotModified) return Result.Unchanged;
        var code = (int)status;
        if (code is < 200 or >= 300) return Result.Failed;
        if (r.Validator != null && validator != null) return r.Validator == validator ? Result.Unchanged : Result.Changed;
        if (r.Size is > 0 && size is > 0) return r.Size == size ? Result.Unchanged : Result.Changed;
        return Result.Unchanged;  // the server names neither a version nor a size: nothing shows a change
    }

    /// <summary>The whole size from a ranged answer ("bytes 0-0/12345"), else the body's length.</summary>
    public static long? SizeOf(HttpResponseMessage res) =>
        res.Content.Headers.ContentRange?.Length ?? (res.StatusCode == HttpStatusCode.OK ? res.Content.Headers.ContentLength : null);

    /// <summary>Asks the server whether the file changed. Never throws: problems count as Failed (checked again later).</summary>
    public static async Task<(Result Result, string? Validator, long? Size)> CheckAsync(DownloadRecord r, HttpClient http, CancellationToken ct)
    {
        if (!Uri.TryCreate(r.Url, UriKind.Absolute, out var start) || start.Scheme is not ("http" or "https")) return (Result.Failed, null, null);
        try
        {
            using var res = await Network.SendAsync(http, start, url =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(0, 0);
                if (r.Validator is { } v)
                {
                    if (v.StartsWith('"') && EntityTagHeaderValue.TryParse(v, out var tag)) req.Headers.IfNoneMatch.Add(tag);
                    else if (DateTimeOffset.TryParse(v, out var when)) req.Headers.IfModifiedSince = when;
                }
                if (!string.IsNullOrEmpty(r.UserAgent)) req.Headers.TryAddWithoutValidation("User-Agent", r.UserAgent);
                // Private details only go to the download's own host, never to another one after a redirect.
                if (url.Host.Equals(start.Host, StringComparison.OrdinalIgnoreCase) && url.Scheme == start.Scheme)
                {
                    if (!string.IsNullOrEmpty(r.Referrer)) req.Headers.TryAddWithoutValidation("Referer", r.Referrer);
                    if (!string.IsNullOrEmpty(r.Cookies)) req.Headers.TryAddWithoutValidation("Cookie", r.Cookies);
                    if (!string.IsNullOrEmpty(r.Username))
                        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{r.Username}:{r.Password}")));
                }
                return req;
            }, HttpCompletionOption.ResponseHeadersRead, ct);
            var validator = Network.Validator(res);
            var size = SizeOf(res);
            return (Compare(r, res.StatusCode, validator, size), validator, size);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException)
        {
            return (Result.Failed, null, null);
        }
    }

    /// <summary>Gets a changed file ready to download again: the old copy stays where it is until the new one is complete.</summary>
    public static void PrepareRedownload(DownloadRecord r)
    {
        r.SyncReplace = r.SavedPath;
        r.Status = DownloadStatus.Queued;
        r.Segments = new();
        r.Size = null;
        r.Validator = null;
        r.WorkFile = null;
        r.Finished = null;
        r.Error = null;
        r.ChecksumResult = null;
    }

    /// <summary>
    /// After a download finishes: an updated synced file takes the old copy's place (true when it did). The check time is
    /// recorded either way so the next check waits a full interval.
    /// </summary>
    public static bool Finished(DownloadRecord r, DateTime now)
    {
        if (r.SyncHours is > 0) r.SyncChecked = now;
        if (r.SyncReplace is not { } old) return false;
        r.SyncReplace = null;
        if (r.SavedPath is not { } fresh || string.Equals(fresh, old, StringComparison.Ordinal) || !File.Exists(fresh)) return false;
        try
        {
            // The new copy must be in the same folder: only then is replacing the old one a plain rename.
            if (!string.Equals(Path.GetDirectoryName(fresh), Path.GetDirectoryName(old), StringComparison.Ordinal)) return false;
            File.Move(fresh, old, overwrite: true);
            r.SavedPath = old;
            r.FileName = Path.GetFileName(old);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;  // the old file is open elsewhere: the update stays under its new name
        }
    }
}

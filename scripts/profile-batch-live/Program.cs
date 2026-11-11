using System.Diagnostics;
using Nexonic.Core;

// Lists real public profiles with the app's own Profile Batch code (no login, no cookies) and downloads one video of the
// first profile that worked, in 480p, checking the picture height with ffprobe. Prints a Markdown report.
EnginePaths.FFmpeg = "/usr/bin/ffmpeg";
var urls = args.Length > 0 ? args : new[] { "https://www.youtube.com/@NASA" };
var report = new List<string> { "| Link | Found | Not accessible | Second run (continue) | Result |", "|---|---|---|---|---|" };
string? sample = null;
var failures = 0;
foreach (var url in urls)
{
    try
    {
        ProfileBatch.ForgetScan(url);
        // Stop after 5 videos, then continue: the second run must start after them.
        var cts = new CancellationTokenSource();
        try { ProfileBatch.Discover(url, null, 40, (_, n) => { if (n >= 5) cts.Cancel(); }, cancel: cts.Token); } catch (OperationCanceledException) { }
        var kept = ProfileBatch.LoadScan(url)?.Entries.Count ?? 0;
        var scan = ProfileBatch.Discover(url, null, 40);
        var (take, counts) = ProfileBatch.Choose(scan.Entries, new BatchFilter { Max = 40 }, _ => false);
        var unique = scan.Entries.Select(e => ProfileBatch.Key(e.Url)).Distinct().Count();
        report.Add($"| {url} | {counts.Discovered} | {counts.Inaccessible} | kept {kept}, total {scan.Entries.Count}, unique {unique} | {(scan.Owner.Length > 0 ? scan.Owner : "ok")} |");
        sample ??= take.FirstOrDefault(e => e.Kind == "video")?.Url;
    }
    catch (Exception e) { failures++; report.Add($"| {url} | – | – | – | {e.Message.Replace('|', '/').Split('\n')[0]} |"); }
}
Console.WriteLine(string.Join("\n", report));
if (sample != null)
{
    var folder = Path.Combine(Path.GetTempPath(), "pb-live");
    var a = SiteVideo.Arguments(sample, folder, Path.Combine(folder, ".part"), "/usr/bin/ffmpeg", SiteVideoTools.QuickJs, false, null, null, null, null, null, 0, null, 480);
    var (prepared, _) = SiteVideo.Prepare(a, null);
    var p = Process.Start(new ProcessStartInfo(SiteVideoTools.YtDlp, prepared) { RedirectStandardOutput = true, RedirectStandardError = true })!;
    var err = p.StandardError.ReadToEndAsync();
    p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    var file = Directory.Exists(folder) ? Directory.GetFiles(folder).FirstOrDefault(f => !f.EndsWith(".part")) : null;
    var height = "";
    if (file != null)
    {
        var probe = Process.Start(new ProcessStartInfo("ffprobe", new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=height", "-of", "csv=p=0", file }) { RedirectStandardOutput = true })!;
        height = probe.StandardOutput.ReadToEnd().Trim();
        probe.WaitForExit();
    }
    Console.WriteLine($"\nDownload in 480p of {sample}: exit {p.ExitCode}, file {(file == null ? "none" : Path.GetFileName(file) + $" ({new FileInfo(file).Length} bytes)")}, picture height {height}");
    if (p.ExitCode != 0) Console.WriteLine(SiteVideo.Error(err.Result));
}
return failures == urls.Length ? 1 : 0;

using System.Diagnostics;
using Nexonic.Core;

// Lists real public profiles with the app's own Profile Batch code (no login, no cookies), stops and continues each
// listing, checks it for duplicates, then downloads one video of each profile in 480p twice (the second time must be
// recognised as already there), checking the picture height with ffprobe. Prints a Markdown report in which a site's
// refusal (sign-in, rate limit, region, data-centre address) is told apart from a failure of the app. Exit code 1 only
// for failures of the app.
EnginePaths.FFmpeg = "/usr/bin/ffmpeg";
var urls = args.Length > 0 ? args : new[]
{
    "https://www.instagram.com/nasa/", "https://www.facebook.com/NASA/videos", "https://www.tiktok.com/@nasa",
    "https://x.com/NASA", "https://www.youtube.com/@NASA", "https://vimeo.com/channels/staffpicks",
};
string Kind(string message) => GalleryList.SiteRestriction(message) ? "site restriction" : "app bug";
var report = new List<string> { "| Link | Found | Not accessible | Stop and continue | Duplicates | Result | Class |", "|---|---|---|---|---|---|---|" };
var samples = new List<string>();
var bugs = 0;
foreach (var url in urls)
{
    try
    {
        ProfileBatch.ForgetScan(url);
        // Stop after 5 videos, then continue: the second run must keep them and start after them.
        var cts = new CancellationTokenSource();
        var stopped = false;
        try { ProfileBatch.Discover(url, null, 40, (_, n) => { if (n >= 5) { stopped = true; cts.Cancel(); } }, cancel: cts.Token); } catch (OperationCanceledException) { }
        var kept = ProfileBatch.LoadScan(url)?.Entries.Count ?? 0;
        var scan = ProfileBatch.Discover(url, null, 40);
        var (take, counts) = ProfileBatch.Choose(scan.Entries, new BatchFilter { Max = 40 }, _ => false);
        var unique = scan.Entries.Select(e => ProfileBatch.Key(e.Url)).Distinct().Count();
        var problems = new List<string>();
        if (stopped && kept == 0) problems.Add("stopped listing kept nothing");
        if (scan.Entries.Count < kept) problems.Add("continued listing lost entries");
        if (counts.Discovered != unique) problems.Add("duplicate count differs");
        var cls = problems.Count > 0 ? "app bug" : scan.Entries.Count == 0 ? Kind(scan.Note) : "ok";
        if (cls == "app bug") bugs++;
        var result = problems.Count > 0 ? string.Join("; ", problems) : (scan.Owner.Length > 0 ? scan.Owner : "ok") + (scan.Note.Length > 0 ? " — " + scan.Note : "");
        report.Add($"| {url} | {counts.Discovered} | {counts.Inaccessible} | {(stopped ? $"kept {kept}, total {scan.Entries.Count}" : "not stopped (fewer than 5)")} | {scan.Entries.Count - unique} | {result.Replace('|', '/')} | {cls} |");
        if (take.FirstOrDefault(e => e.Kind != "live") is { } first) samples.Add(first.Url);
    }
    catch (Exception e)
    {
        var cls = Kind(e.Message);
        if (cls == "app bug") bugs++;
        report.Add($"| {url} | – | – | – | – | {e.Message.Replace('|', '/').Split('\n')[0]} | {cls} |");
    }
}
Console.WriteLine(string.Join("\n", report));

(int Exit, string? File, string Err) Download(string sample, string folder)
{
    var a = SiteVideo.Arguments(sample, folder, Path.Combine(folder, ".part"), "/usr/bin/ffmpeg", SiteVideoTools.QuickJs, false, null, null, null, null, null, 0, null, 480);
    var (prepared, _) = SiteVideo.Prepare(a, null);
    var p = Process.Start(new ProcessStartInfo(SiteVideoTools.YtDlp, prepared) { RedirectStandardOutput = true, RedirectStandardError = true })!;
    var err = p.StandardError.ReadToEndAsync();
    p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    var file = Directory.Exists(folder) ? Directory.GetFiles(folder).FirstOrDefault(f => !f.EndsWith(".part")) : null;
    return (p.ExitCode, file, err.Result);
}

// One video of each site that listed, in 480p, downloaded twice into the same folder: the second run must leave the one
// finished file as it is.
Console.WriteLine("\n| Video | Exit | File | Picture height | Second run | Reason | Class |\n|---|---|---|---|---|---|---|");
foreach (var sample in samples)
{
    var folder = Path.Combine(Path.GetTempPath(), "pb-live", Guid.NewGuid().ToString("N"));
    var (exit, file, err) = Download(sample, folder);
    var height = "";
    var again = "";
    var cls = "ok";
    if (file != null)
    {
        var probe = Process.Start(new ProcessStartInfo("ffprobe", new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=height", "-of", "csv=p=0", file }) { RedirectStandardOutput = true })!;
        height = probe.StandardOutput.ReadToEnd().Trim();
        probe.WaitForExit();
        var size = new FileInfo(file).Length;
        var second = Download(sample, folder);
        var files = Directory.GetFiles(folder).Where(f => !f.EndsWith(".part")).ToList();
        again = second.Exit == 0 && files.Count == 1 && new FileInfo(file).Length == size ? "kept the one file" : $"exit {second.Exit}, {files.Count} files";
        if (again != "kept the one file") cls = "app bug";
    }
    else cls = Kind(err);
    if (exit == 0 && file == null) cls = "app bug";
    if (cls == "app bug") bugs++;
    var reason = exit != 0 ? SiteVideo.Error(err).Replace('|', '/').Split('\n')[0] : "";
    Console.WriteLine($"| {sample} | {exit} | {(file == null ? "none" : Path.GetFileName(file).Replace('|', '/') + $" ({new FileInfo(file).Length} bytes)")} | {height} | {again} | {reason} | {cls} |");
}
Console.WriteLine($"\n{bugs} app failure(s); site restrictions are listed but don't fail this check.");
return bugs > 0 ? 1 : 0;

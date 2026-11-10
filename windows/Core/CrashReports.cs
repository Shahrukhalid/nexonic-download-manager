using System.Runtime.InteropServices;

namespace Nexonic.Core;

/// <summary>
/// Help → Crash Reports: when the app stops because of an error, what happened is written to a text file on this
/// computer (never sent anywhere). The newest 20 reports are kept; the user can attach one to a bug report.
/// </summary>
public static class CrashReports
{
    public static string Folder => Path.Combine(AppPaths.Support, "Crash Reports");
    const int Keep = 20;

    /// <summary>Writes one report and returns its path (null when it could not be written).</summary>
    public static string? Write(Exception error, string where, string appVersion, DateTime? at = null)
    {
        try
        {
            var time = at ?? DateTime.Now;
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, $"crash-{time:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.txt");
            File.WriteAllText(path, Text(error, where, appVersion, time));
            foreach (var old in Directory.GetFiles(Folder, "crash-*.txt").OrderByDescending(f => f).Skip(Keep))
                try { File.Delete(old); } catch { }
            return path;
        }
        catch { return null; }
    }

    /// <summary>The report: app version, system, where it happened and the error with its call stack. Download links,
    /// cookies and passwords are not included.</summary>
    public static string Text(Exception error, string where, string appVersion, DateTime at) =>
        $"""
        Nexonic Download Manager crash report
        Time: {at:yyyy-MM-dd HH:mm:ss}
        App version: {appVersion}
        System: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}
        Where: {where}

        {Scrub(error.ToString())}
        """;

    /// <summary>Removes web addresses from error texts (they can hold access keys).</summary>
    public static string Scrub(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"(?i)\b(?:https?|magnet|ftp):[^\s""'<>]+", "[link]");

    public static List<string> List() =>
        Directory.Exists(Folder) ? Directory.GetFiles(Folder, "crash-*.txt").OrderByDescending(f => f).ToList() : new();
}

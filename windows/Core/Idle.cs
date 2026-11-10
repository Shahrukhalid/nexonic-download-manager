using System.Runtime.InteropServices;

namespace Nexonic.Core;

/// <summary>How long the computer has had no keyboard or mouse input (null when the system can't tell).</summary>
public static class Idle
{
    [StructLayout(LayoutKind.Sequential)] struct LastInputInfo { public uint Size; public uint Time; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInputInfo info);

    [StructLayout(LayoutKind.Sequential)]
    struct XScreenSaverInfo { public IntPtr Window; public int State; public int Kind; public ulong TilOrSince; public ulong Idle; public ulong EventMask; }
    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XFree(IntPtr data);
    [DllImport("libXss.so.1")] static extern IntPtr XScreenSaverAllocInfo();
    [DllImport("libXss.so.1")] static extern int XScreenSaverQueryInfo(IntPtr display, IntPtr drawable, IntPtr info);
    static IntPtr display;
    static bool unavailable;

    public static TimeSpan? Time()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
                return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : null;
            }
            if (OperatingSystem.IsLinux() && !unavailable)
            {
                // X11 and XWayland desktops (libXss); elsewhere the limit simply always applies.
                if (display == IntPtr.Zero) display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) { unavailable = true; return null; }
                var info = XScreenSaverAllocInfo();
                try
                {
                    if (XScreenSaverQueryInfo(display, XDefaultRootWindow(display), info) == 0) return null;
                    return TimeSpan.FromMilliseconds(Marshal.PtrToStructure<XScreenSaverInfo>(info).Idle);
                }
                finally { XFree(info); }
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { unavailable = true; }
        return null;
    }

    /// <summary>The limit for all downloads now: the "while in use" limit when there was input in the last minute (or
    /// when idle time can't be read), else the general limit.</summary>
    public static int Effective(int generalKBps, int whileInUseKBps, TimeSpan? idle)
    {
        if (whileInUseKBps <= 0) return generalKBps;
        var inUse = idle is not { } t || t < TimeSpan.FromMinutes(1);
        if (!inUse) return generalKBps;
        return generalKBps <= 0 ? whileInUseKBps : Math.Min(generalKBps, whileInUseKBps);
    }
}

/// <summary>
/// Speed limits by time of day (Options → Connection), one rule per line: "days start-end KB/s", for example
/// "Mon-Fri 09:00-17:00 300" or "Daily 23:00-07:00 0" (0 = no limit). The first rule that applies now replaces the
/// general limit; a range may pass midnight (it then counts from the day it starts).
/// </summary>
public static class SpeedSchedule
{
    public sealed record Rule(bool[] Days, TimeSpan Start, TimeSpan End, int KBps);

    static readonly string[] DayNames = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

    public static (List<Rule> Rules, string? Problem) Parse(string? text)
    {
        var rules = new List<Rule>();
        var n = 0;
        foreach (var raw in (text ?? "").Split('\n'))
        {
            n++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 3 || Days(p[0]) is not { } days || Times(p[1]) is not { } times || !int.TryParse(p[2], out var kb) || kb < 0)
                return (rules, $"Line {n}: write it as days start-end KB/s, for example Mon-Fri 09:00-17:00 300.");
            rules.Add(new Rule(days, times.Start, times.End, Math.Min(kb, 10_000_000)));
            if (rules.Count == 50) break;
        }
        return (rules, null);
    }

    static bool[]? Days(string s)
    {
        var days = new bool[7];
        foreach (var part in s.ToLowerInvariant().Split(','))
        {
            if (part is "daily" or "every" or "all") { Array.Fill(days, true); continue; }
            if (part == "weekdays") { for (var d = 1; d <= 5; d++) days[d] = true; continue; }
            if (part == "weekends") { days[0] = days[6] = true; continue; }
            var range = part.Split('-');
            var a = Array.IndexOf(DayNames, range[0].Length >= 3 ? range[0][..3] : "");
            var b = range.Length == 2 ? Array.IndexOf(DayNames, range[1].Length >= 3 ? range[1][..3] : "") : a;
            if (a < 0 || b < 0 || range.Length > 2) return null;
            for (var d = a; ; d = (d + 1) % 7) { days[d] = true; if (d == b) break; }
        }
        return days;
    }

    static (TimeSpan Start, TimeSpan End)? Times(string s)
    {
        var p = s.Split('-');
        return p.Length == 2 && TimeSpan.TryParseExact(p[0], @"hh\:mm", null, out var a) && TimeSpan.TryParseExact(p[1], @"hh\:mm", null, out var b)
               && a < TimeSpan.FromDays(1) && b < TimeSpan.FromDays(1) && a != b ? (a, b) : null;
    }

    /// <summary>The limit the schedule sets at this local time, or null when no rule applies.</summary>
    public static int? LimitAt(IReadOnlyList<Rule> rules, DateTime local)
    {
        var day = (int)local.DayOfWeek;
        var t = local.TimeOfDay;
        foreach (var r in rules)
        {
            if (r.Start < r.End) { if (r.Days[day] && t >= r.Start && t < r.End) return r.KBps; }
            else if ((r.Days[day] && t >= r.Start) || (r.Days[(day + 6) % 7] && t < r.End)) return r.KBps;  // passes midnight
        }
        return null;
    }
}

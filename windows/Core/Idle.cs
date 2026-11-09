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

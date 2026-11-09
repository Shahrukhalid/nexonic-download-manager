using System.Runtime.InteropServices;

namespace Nexonic.App;

/// <summary>One Windows wake timer: wakes the computer from sleep at the given time while the app runs (in the tray too).</summary>
public static class WakeTimer
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWaitableTimerW(IntPtr attributes, bool manualReset, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);
    [DllImport("kernel32.dll")]
    static extern bool CancelWaitableTimer(IntPtr timer);

    static IntPtr handle;

    /// <summary>Sets (or with null, cancels) the wake time. Returns false when Windows refused it.</summary>
    public static bool Set(DateTime? at)
    {
        try
        {
            if (handle == IntPtr.Zero) handle = CreateWaitableTimerW(IntPtr.Zero, true, null);
            if (handle == IntPtr.Zero) return false;
            if (at is not { } t || t <= DateTime.Now) return CancelWaitableTimer(handle);
            var due = t.ToUniversalTime().ToFileTimeUtc();
            return SetWaitableTimer(handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, true);
        }
        catch { return false; }
    }
}

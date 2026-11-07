using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nexonic.Core;

/// <summary>Checks before a big download is written: enough free space, and a drive that can hold a file that size.</summary>
public static class DiskSpace
{
    /// <summary>The largest file a FAT32 drive can hold (4 GB minus one byte).</summary>
    public const long Fat32Limit = 4L * 1024 * 1024 * 1024 - 1;
    const long Margin = 64L * 1024 * 1024;

    /// <summary>
    /// Why a file of <paramref name="size"/> bytes can't be saved in <paramref name="folder"/>, or null when it can (or
    /// the drive can't be checked). <paramref name="already"/> is what this download has written there so far.
    /// </summary>
    public static string? Problem(string folder, long size, long already = 0)
    {
        try
        {
            if (DriveOf(folder) is not { } drive) return null;
            var format = drive.DriveFormat ?? "";
            if (size > Fat32Limit && (format.Equals("FAT32", StringComparison.OrdinalIgnoreCase)
                                      || format.Equals("vfat", StringComparison.OrdinalIgnoreCase)
                                      || format.Equals("msdos", StringComparison.OrdinalIgnoreCase)))
                return $"This drive ({drive.Name}) can't store files over 4 GB because it is formatted as FAT32. "
                       + "Choose a folder on another drive, or reformat the drive as exFAT or NTFS";
            var need = size - already;
            if (need > 0 && drive.AvailableFreeSpace < need + Margin)
                return $"Not enough free space on {drive.Name}: this download needs {Bytes(need)} more, {Bytes(drive.AvailableFreeSpace)} is free";
        }
        catch { /* not checkable: the write itself reports a real problem */ }
        return null;
    }

    /// <summary>The mounted drive that holds the folder (the longest matching root).</summary>
    static DriveInfo? DriveOf(string folder)
    {
        var full = Path.GetFullPath(folder);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        DriveInfo? best = null;
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                var root = d.RootDirectory.FullName;
                if (!d.IsReady || !full.StartsWith(root, comparison)) continue;
                if (root.Length > 1 && full.Length > root.Length && !root.EndsWith(Path.DirectorySeparatorChar)
                    && full[root.Length] != Path.DirectorySeparatorChar) continue;  // /media/usb is not /media/usb2
                if (best == null || root.Length > best.RootDirectory.FullName.Length) best = d;
            }
            catch { }
        }
        return best;
    }

    public static string Bytes(long n) => n >= 1L << 30 ? $"{n / (double)(1L << 30):0.0} GB" : $"{Math.Max(1, n / (1L << 20))} MB";

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr inBuffer, uint inSize, IntPtr outBuffer,
        uint outSize, out uint returned, IntPtr overlapped);

    /// <summary>
    /// On Windows, marks the file sparse so writing a range far into it doesn't first fill everything before it with
    /// zeros (Mac and Linux file systems do this by themselves). Ignored where it isn't supported (FAT, exFAT).
    /// </summary>
    public static void MakeSparse(FileStream file)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { DeviceIoControl(file.SafeFileHandle, 0x000900C4 /* FSCTL_SET_SPARSE */, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero); }
        catch { }
    }
}

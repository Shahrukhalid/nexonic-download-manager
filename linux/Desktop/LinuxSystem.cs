using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexonic.Linux;

/// <summary>What the app asks of the Linux desktop: opening files, the file manager, the trash, notifications, power.</summary>
public static class LinuxSystem
{
    /// <summary>Runs a program found on PATH without a shell. False when it is missing or fails.</summary>
    public static bool Run(string program, params string[] args) => RunWait(program, args, 0) != null;

    /// <summary>Runs a program and waits up to <paramref name="waitMs"/> (0 = do not wait). Null when it could not start.</summary>
    public static int? RunWait(string program, string[] args, int waitMs)
    {
        if (Which(program) is not { } exe) return null;
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var p = Process.Start(psi);
            if (p == null) return null;
            p.StandardInput.Close();
            _ = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (waitMs <= 0) { p.EnableRaisingEvents = true; p.Exited += (_, _) => p.Dispose(); return 0; }
            if (!p.WaitForExit(waitMs)) return 0; // still running (a viewer or a dialog): that is fine
            var code = p.ExitCode;
            p.Dispose();
            return code;
        }
        catch { return null; }
    }

    /// <summary>The full path of a program on PATH, or null.</summary>
    public static string? Which(string program)
    {
        if (program.Contains('/')) return File.Exists(program) ? program : null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(dir, program);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>The user's Downloads folder as set in the desktop (XDG user dirs), or ~/Downloads.</summary>
    public static string DownloadFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".config");
        try
        {
            var file = Path.Combine(config, "user-dirs.dirs");
            if (File.Exists(file) && UserDir(File.ReadAllText(file), "XDG_DOWNLOAD_DIR", home) is { } d) return d;
        }
        catch { }
        return Path.Combine(home, "Downloads");
    }

    /// <summary>Reads one entry of user-dirs.dirs (XDG_DOWNLOAD_DIR="$HOME/Downloads").</summary>
    public static string? UserDir(string text, string name, string home)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(name + "=", StringComparison.Ordinal)) continue;
            var value = line[(name.Length + 1)..].Trim().Trim('"');
            value = value.Replace("$HOME", home);
            if (!value.StartsWith('/')) return null;
            return value.TrimEnd('/') == home.TrimEnd('/') ? null : value; // "$HOME" itself means the folder is turned off
        }
        return null;
    }

    public static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d
        ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    public static string ConfigHome => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } d
        ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

    /// <summary>Opens a file or folder with the user's default program.</summary>
    public static bool Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path) && !path.Contains("://")) return false;
        return Run("xdg-open", path) || Run("gio", "open", path);
    }

    /// <summary>Opens the file manager with the file selected (falls back to opening its folder).</summary>
    public static void ShowInFolder(string path)
    {
        var uri = new Uri(path).AbsoluteUri;
        // org.freedesktop.FileManager1 is offered by Files (Nautilus), Dolphin, Nemo, Caja, Thunar and others.
        if (RunWait("dbus-send", new[]
            {
                "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "/org/freedesktop/FileManager1",
                "org.freedesktop.FileManager1.ShowItems", "array:string:" + uri, "string:",
            }, 4000) == 0) return;
        if (Path.GetDirectoryName(path) is { } dir) Open(dir);
    }

    /// <summary>Moves a file to the desktop's trash (it can be restored from there). False when it could not.</summary>
    public static bool Trash(string path)
    {
        if (!File.Exists(path)) return false;
        if (RunWait("gio", new[] { "trash", path }, 10000) == 0 && !File.Exists(path)) return true;
        if (RunWait("kioclient6", new[] { "move", path, "trash:/" }, 10000) == 0 && !File.Exists(path)) return true;
        if (RunWait("kioclient5", new[] { "move", path, "trash:/" }, 10000) == 0 && !File.Exists(path)) return true;
        return TrashByHand(path, Path.Combine(DataHome, "Trash"));
    }

    /// <summary>The freedesktop.org trash: the file in Trash/files and where it came from in Trash/info.</summary>
    public static bool TrashByHand(string path, string trash)
    {
        try
        {
            var files = Path.Combine(trash, "files");
            var info = Path.Combine(trash, "info");
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);
            var name = Path.GetFileName(path);
            var target = name;
            for (var n = 2; File.Exists(Path.Combine(files, target)) || File.Exists(Path.Combine(info, target + ".trashinfo")); n++)
                target = $"{Path.GetFileNameWithoutExtension(name)} {n}{Path.GetExtension(name)}";
            var escaped = string.Join("/", Path.GetFullPath(path).Split('/').Select(Uri.EscapeDataString));
            File.WriteAllText(Path.Combine(info, target + ".trashinfo"),
                $"[Trash Info]\nPath={escaped}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
            File.Move(path, Path.Combine(files, target));
            return true;
        }
        catch { return false; }
    }

    /// <summary>A desktop notification (notify-send). Does nothing when notifications are not available.</summary>
    public static void Notify(string title, string body, string? icon = null)
    {
        var args = new List<string> { "--app-name=Nexonic Download Manager" };
        if (icon is { Length: > 0 }) args.Add("--icon=" + icon);
        args.Add(title);
        args.Add(body);
        Run("notify-send", args.ToArray());
    }

    /// <summary>The desktop's "complete" sound.</summary>
    public static void PlayDoneSound()
    {
        if (Run("canberra-gtk-play", "-i", "complete", "-d", "Nexonic Download Manager")) return;
        const string sound = "/usr/share/sounds/freedesktop/stereo/complete.oga";
        if (File.Exists(sound) && !Run("pw-play", sound)) Run("paplay", sound);
    }

    public static bool Suspend() => RunWait("systemctl", new[] { "suspend" }, 10000) == 0 || RunWait("loginctl", new[] { "suspend" }, 10000) == 0;

    /// <summary>Shuts down in one minute (cancel with <c>shutdown -c</c>), through systemd-logind; false when not allowed.</summary>
    public static bool ShutDownSoon() => RunWait("shutdown", new[] { "+1" }, 10000) == 0;

    // ---- where a file came from ----

    [DllImport("libc", SetLastError = true)]
    static extern int setxattr(string path, string name, byte[] value, nuint size, int flags);

    /// <summary>
    /// Records the download's address in the file's extended attributes (user.xdg.origin.url), as browsers do on Linux,
    /// so file managers can show where it came from. Quietly skipped on file systems without them.
    /// </summary>
    public static bool MarkOrigin(string path, string url)
    {
        try
        {
            var value = Encoding.UTF8.GetBytes(url);
            return setxattr(path, "user.xdg.origin.url", value, (nuint)value.Length, 0) == 0;
        }
        catch { return false; }
    }
}

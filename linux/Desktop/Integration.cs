using Nexonic.Core;

namespace Nexonic.Linux;

/// <summary>How this copy of the app was installed, which decides where the browser helper and the ndm command point.</summary>
public enum InstallKind
{
    /// <summary>A single AppImage file: it can be moved, so small launch scripts in the user's folder point to it.</summary>
    AppImage,
    /// <summary>A .deb or .rpm package in /opt: the package manager also provides the menu entry and the ndm command.</summary>
    Package,
    /// <summary>An unpacked folder (a build or the .tar.gz).</summary>
    Folder,
}

/// <summary>
/// Puts the app into the desktop for the current user (no administrator rights): the browser helper for Chrome, Chromium,
/// Edge, Brave, Vivaldi, Opera and Firefox, the ndm command and, for the AppImage, the app menu entry and icon.
/// Everything it writes is listed in <see cref="Remove"/>.
/// </summary>
public sealed class Integration
{
    public const string AppId = "nexonic-download-manager";
    readonly string home;
    readonly string dataHome;
    readonly string configHome;
    readonly string appDir;
    readonly string? appImage;

    public Integration(string? home = null, string? appDir = null, string? appImage = null, string? dataHome = null, string? configHome = null)
    {
        this.home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        this.dataHome = dataHome ?? (home == null ? LinuxSystem.DataHome : Path.Combine(this.home, ".local", "share"));
        this.configHome = configHome ?? (home == null ? LinuxSystem.ConfigHome : Path.Combine(this.home, ".config"));
        this.appDir = appDir ?? AppContext.BaseDirectory;
        this.appImage = appImage ?? (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } a && File.Exists(a) ? a : null);
    }

    public InstallKind Kind => appImage != null ? InstallKind.AppImage
        : appDir.StartsWith("/opt/", StringComparison.Ordinal) || appDir.StartsWith("/usr/", StringComparison.Ordinal) ? InstallKind.Package
        : InstallKind.Folder;

    /// <summary>The AppImage file that is running, when it is one.</summary>
    public string? AppImagePath => appImage;

    /// <summary>Folder for the small launch scripts (inside the app's own data folder).</summary>
    public string BinFolder => Path.Combine(dataHome, AppId, "bin");

    /// <summary>
    /// The folder with the browser extension ("Extension", loaded unpacked) and "Firefox Add-on.xpi". The AppImage's own files
    /// move every time it starts, so they are copied to the user's data folder.
    /// </summary>
    public string ExtensionFolder => Kind == InstallKind.AppImage ? Path.Combine(dataHome, AppId, "browser") : appDir;

    /// <summary>The program the browser starts for the extension.</summary>
    public string HostCommand => Kind == InstallKind.AppImage ? Path.Combine(BinFolder, "nexonic-host") : Path.Combine(appDir, "nexonic-host");

    /// <summary>Per-user folders where each browser looks for helpers (Chromium-based browsers and Firefox).</summary>
    public IReadOnlyList<(string Folder, bool Firefox)> HostFolders() => new List<(string, bool)>
    {
        (Path.Combine(configHome, "google-chrome", "NativeMessagingHosts"), false),
        (Path.Combine(configHome, "google-chrome-beta", "NativeMessagingHosts"), false),
        (Path.Combine(configHome, "chromium", "NativeMessagingHosts"), false),
        (Path.Combine(configHome, "microsoft-edge", "NativeMessagingHosts"), false),
        (Path.Combine(configHome, "BraveSoftware", "Brave-Browser", "NativeMessagingHosts"), false),
        (Path.Combine(configHome, "vivaldi", "NativeMessagingHosts"), false),
        (Path.Combine(configHome, "opera", "NativeMessagingHosts"), false),
        (Path.Combine(home, ".mozilla", "native-messaging-hosts"), true),
        // Flatpak and Snap browsers keep their own folders.
        (Path.Combine(home, ".var", "app", "org.mozilla.firefox", ".mozilla", "native-messaging-hosts"), true),
        (Path.Combine(home, "snap", "firefox", "common", ".mozilla", "native-messaging-hosts"), true),
        (Path.Combine(home, "snap", "chromium", "common", "chromium", "NativeMessagingHosts"), false),
    };

    /// <summary>Writes everything for the current install; returns the browser folders that got the helper.</summary>
    public List<string> Install()
    {
        var done = new List<string>();
        if (Kind == InstallKind.AppImage && appImage != null)
        {
            Script(Path.Combine(BinFolder, "nexonic-host"), appImage, "--host");
            InstallCommand(Path.Combine(BinFolder, "nexonic-host"));
            InstallMenuEntry(appImage);
            CopyExtension();
        }
        else if (Kind == InstallKind.Folder) InstallCommand(Path.Combine(appDir, "nexonic-host"));
        if (!File.Exists(HostCommand)) return done;
        foreach (var (folder, firefox) in HostFolders())
        {
            // Only for browsers that are set up for this user (their folder exists); the helper's own folder is created.
            var browserRoot = Path.GetDirectoryName(folder)!;
            if (!Directory.Exists(browserRoot)) continue;
            try
            {
                Directory.CreateDirectory(folder);
                var manifest = Path.Combine(folder, NativeMessaging.HostName + ".json");
                var text = firefox ? NativeMessaging.FirefoxManifest(HostCommand) : NativeMessaging.Manifest(HostCommand);
                if (!File.Exists(manifest) || File.ReadAllText(manifest) != text) WriteAtomic(manifest, text, executable: false);
                done.Add(folder);
            }
            catch { }
        }
        return done;
    }

    void CopyExtension()
    {
        try
        {
            var src = Path.Combine(appDir, "Extension");
            if (!Directory.Exists(src)) return;
            var target = Path.Combine(ExtensionFolder, "Extension");
            var manifest = Path.Combine(src, "manifest.json");
            var copied = Path.Combine(target, "manifest.json");
            if (File.Exists(copied) && File.Exists(manifest) && File.ReadAllText(copied) == File.ReadAllText(manifest)) return;
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(target, Path.GetRelativePath(src, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to, true);
            }
            var xpi = Path.Combine(appDir, "Firefox Add-on.xpi");
            if (File.Exists(xpi)) File.Copy(xpi, Path.Combine(ExtensionFolder, "Firefox Add-on.xpi"), true);
        }
        catch { }
    }

    /// <summary>Removes the helper registrations, the ndm command, the menu entry and the launch scripts written by this app.</summary>
    public void Remove()
    {
        foreach (var (folder, _) in HostFolders()) Delete(Path.Combine(folder, NativeMessaging.HostName + ".json"));
        var ndm = Path.Combine(home, ".local", "bin", "ndm");
        if (File.Exists(ndm) && File.ReadAllText(ndm).Contains(Marker)) Delete(ndm);
        Delete(Path.Combine(dataHome, "applications", AppId + ".desktop"));
        foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256, 512 })
            Delete(Path.Combine(dataHome, "icons", "hicolor", $"{size}x{size}", "apps", AppId + ".png"));
        try { if (Directory.Exists(BinFolder)) Directory.Delete(BinFolder, true); } catch { }
        try { var browser = Path.Combine(dataHome, AppId, "browser"); if (Directory.Exists(browser)) Directory.Delete(browser, true); } catch { }
        LinuxSystem.Run("update-desktop-database", Path.Combine(dataHome, "applications"));
    }

    const string Marker = "# Written by Nexonic Download Manager";

    /// <summary>A launch script that runs <paramref name="target"/> with the given first argument and the rest passed on.</summary>
    public static string ScriptText(string target, string? firstArgument) =>
        $"#!/bin/sh\n{Marker}\nexec {Quote(target)}{(firstArgument != null ? " " + firstArgument : "")} \"$@\"\n";

    static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    void Script(string path, string target, string? firstArgument)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = ScriptText(target, firstArgument);
        if (File.Exists(path) && File.ReadAllText(path) == text) return;
        WriteAtomic(path, text, executable: true);
    }

    /// <summary>~/.local/bin/ndm (on the PATH of most distributions), unless something else already has that name.</summary>
    void InstallCommand(string hostCommand)
    {
        try
        {
            var path = Path.Combine(home, ".local", "bin", "ndm");
            if (File.Exists(path) && !File.ReadAllText(path).Contains(Marker)) return;
            if (!File.Exists(hostCommand)) return;
            Script(path, hostCommand, null);
        }
        catch { }
    }

    /// <summary>App menu entry and icon for the AppImage (packages install their own in /usr/share).</summary>
    void InstallMenuEntry(string target)
    {
        try
        {
            var icon = Path.Combine(appDir, "nexonic-download-manager.png");
            var iconTarget = Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", AppId + ".png");
            if (File.Exists(icon))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(iconTarget)!);
                File.Copy(icon, iconTarget, true);
            }
            foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256, 512 })
            {
                var source = Path.Combine(appDir, "icons", $"{size}.png");
                if (!File.Exists(source)) continue;
                var destination = Path.Combine(dataHome, "icons", "hicolor", $"{size}x{size}", "apps", AppId + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
            var entry = Path.Combine(dataHome, "applications", AppId + ".desktop");
            var text = DesktopEntry(target);
            if (File.Exists(entry) && File.ReadAllText(entry) == text) return;
            Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
            WriteAtomic(entry, text, executable: false);
            LinuxSystem.Run("update-desktop-database", Path.GetDirectoryName(entry)!);
        }
        catch { }
    }

    /// <summary>The app menu entry. Links can be opened with the app (Open With), from the command line or another program.</summary>
    public static string DesktopEntry(string exec) =>
        "[Desktop Entry]\n" +
        "Type=Application\n" +
        "Name=Nexonic Download Manager\n" +
        "GenericName=Download Manager\n" +
        "Comment=Fast downloads with pause and resume, queues, a scheduler and video downloads\n" +
        $"Exec={EscapeExec(exec)} %U\n" +
        $"Icon={AppId}\n" +
        "Terminal=false\n" +
        "Categories=Network;FileTransfer;\n" +
        "Keywords=download;manager;video;queue;resume;\n" +
        "MimeType=x-scheme-handler/nexonic;text/x-uri;application/x-mswinurl;\n" +
        "StartupWMClass=NexonicDownloadManager\n" +
        "X-AppImage-Integrate=false\n";

    /// <summary>Quotes a path for the Exec key of a desktop entry.</summary>
    public static string EscapeExec(string path)
    {
        if (path.All(c => char.IsLetterOrDigit(c) || "/._-+".Contains(c))) return path;
        var escaped = path.Replace("\\", "\\\\\\\\").Replace("\"", "\\\\\"").Replace("`", "\\\\`").Replace("$", "\\\\$");
        return "\"" + escaped + "\"";
    }

    static void WriteAtomic(string path, string text, bool executable)
    {
        var temp = path + ".tmp-" + Environment.ProcessId;
        File.WriteAllText(temp, text);
        File.SetUnixFileMode(temp, executable
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.Move(temp, path, true);
    }

    static void Delete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}

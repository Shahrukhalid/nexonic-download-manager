using System.IO;
using System.Runtime.InteropServices;

namespace Nexonic.App;

/// <summary>
/// The Microsoft Store edition (an MSIX package). The Store installs its updates, so the app's own updater stays off, and
/// browsers reach the helper through its stable alias in %LOCALAPPDATA%\Microsoft\WindowsApps (the package's own folder
/// changes with every version).
/// </summary>
public static class StorePackage
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int length, char[]? name);

    public static readonly bool IsPackaged = Detect();

    static bool Detect()
    {
        try { var n = 0; return GetCurrentPackageFullName(ref n, null) != 15700; }  // APPMODEL_ERROR_NO_PACKAGE
        catch { return false; }
    }

    /// <summary>The helper browsers start: the package's alias when installed from the Store.</summary>
    public static string HelperPath(string beside)
    {
        if (!IsPackaged) return beside;
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "nexonic-host.exe");
        return File.Exists(alias) ? alias : beside;
    }
}

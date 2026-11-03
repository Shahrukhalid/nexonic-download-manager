using System.Text.Json;
using Avalonia.Styling;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>Settings only the Linux app has (the shared settings stay in settings.json, the same as on Windows).</summary>
public sealed class Prefs
{
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";

    static string FilePath => Path.Combine(AppPaths.Support, "linux.json");

    public ThemeVariant Variant => Theme switch { "light" => ThemeVariant.Light, "dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };

    public static Prefs Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Prefs>(File.ReadAllText(FilePath)) ?? new Prefs() : new Prefs(); }
        catch { return new Prefs(); }
    }

    public void Save()
    {
        try { SafeFile.Write(FilePath, JsonSerializer.Serialize(this)); } catch { }
    }
}

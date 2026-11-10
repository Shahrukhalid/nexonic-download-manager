using System.Text.Json;
using Avalonia.Styling;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>Settings only the Linux app has (the shared settings stay in settings.json, the same as on Windows).</summary>
public sealed class Prefs
{
    /// <summary>"system", "light", "dark", "scheme:Name" (a built-in colour scheme) or "custom" (the user's own).</summary>
    public string Theme { get; set; } = "system";
    /// <summary>The user's own colour scheme (JSON, see ColorScheme).</summary>
    public string SchemeJson { get; set; } = "";
    /// <summary>The font of the whole app; empty = the system's.</summary>
    public string Font { get; set; } = "";

    public ColorScheme? Scheme => Theme == "custom" ? ColorScheme.Import(SchemeJson).Scheme
        : Theme.StartsWith("scheme:") ? ColorScheme.BuiltIn.FirstOrDefault(b => b.Name == Theme[7..]) : null;

    /// <summary>Where the main window was (null until it has been moved or closed once).</summary>
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    static string FilePath => Path.Combine(AppPaths.Support, "linux.json");

    public ThemeVariant Variant => Scheme is { } s ? (s.Base == "dark" ? ThemeVariant.Dark : ThemeVariant.Light)
        : Theme switch { "light" => ThemeVariant.Light, "dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };

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

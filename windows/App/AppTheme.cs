using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>Options → General → Appearance: light, dark, or the same as Windows (apps setting).</summary>
public static class AppTheme
{
    public static readonly string[] Choices = { "system", "light", "dark" };
    public static string Title(string c) => c switch
    {
        "light" => "Light", "dark" => "Dark", "custom" => "My colour scheme",
        _ when c.StartsWith("scheme:") => c[7..], _ => "Same as Windows",
    };

    /// <summary>Light, Dark, Same as Windows, the built-in colour schemes, and the user's own scheme when there is one.</summary>
    public static string[] AllChoices(string customJson) => Choices
        .Concat(ColorScheme.BuiltIn.Select(b => "scheme:" + b.Name))
        .Concat(ColorScheme.Import(customJson).Scheme != null ? new[] { "custom" } : Array.Empty<string>()).ToArray();

    public static ColorScheme? SchemeFor(string? choice, string customJson) =>
        choice == "custom" ? ColorScheme.Import(customJson).Scheme
        : choice?.StartsWith("scheme:") == true ? ColorScheme.BuiltIn.FirstOrDefault(b => b.Name == choice[7..]) : null;

    /// <summary>The app's surfaces from a scheme's few colours (hover and pressed shades are mixed from them).</summary>
    static Dictionary<string, string> FromScheme(ColorScheme c) => new()
    {
        ["Win.Back"] = c.Background, ["Win.Panel"] = c.Panel, ["Win.Side"] = ColorScheme.Mix(c.Background, c.Text, 0.04),
        ["Win.Text"] = c.Text, ["Win.Muted"] = c.Muted, ["Win.Faint"] = ColorScheme.Mix(c.Muted, c.Background, 0.25),
        ["Win.Line"] = c.Line, ["Win.GridLine"] = ColorScheme.Mix(c.Line, c.Panel, 0.5), ["Win.Border"] = ColorScheme.Mix(c.Line, c.Text, 0.15),
        ["Win.Header"] = ColorScheme.Mix(c.Panel, c.Background, 0.5), ["Win.Hover"] = ColorScheme.Mix(c.Panel, c.Accent, 0.12),
        ["Win.Pressed"] = ColorScheme.Mix(c.Panel, c.Accent, 0.22), ["Win.RowHover"] = ColorScheme.Mix(c.Panel, c.Accent, 0.06),
        ["Win.Selected"] = c.Selected,
    };

    static readonly Dictionary<string, (string Light, string Dark)> Colors = new()
    {
        ["Win.Back"] = ("#F7F9FC", "#16191D"), ["Win.Panel"] = ("#FFFFFF", "#1E2227"), ["Win.Side"] = ("#EEF2F7", "#1A1E23"),
        ["Win.Text"] = ("#1F2328", "#E6EDF3"), ["Win.Muted"] = ("#57606A", "#9DA7B3"), ["Win.Faint"] = ("#8B949E", "#7D8590"),
        ["Win.Line"] = ("#E3E8EF", "#30363D"), ["Win.GridLine"] = ("#EEF1F4", "#262B31"), ["Win.Border"] = ("#D0D7DE", "#3D444D"),
        ["Win.Header"] = ("#F6F8FA", "#22272E"), ["Win.Hover"] = ("#EAF1FD", "#26324A"), ["Win.Pressed"] = ("#D6E4FB", "#2C3E62"),
        ["Win.RowHover"] = ("#F3F7FE", "#232A35"), ["Win.Selected"] = ("#DCE8FC", "#1F3A66"),
    };

    /// <summary>Whether Windows is set to dark mode for apps.</summary>
    public static bool WindowsIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch { return false; }
    }

    public static void Apply(string? choice, string customJson = "", string font = "")
    {
        if (Application.Current is not { } app) return;
        var scheme = SchemeFor(choice, customJson);
        var dark = scheme != null ? scheme.Base == "dark" : choice == "dark" || (choice is not ("light" or "dark") && WindowsIsDark());
        var colors = scheme != null ? FromScheme(scheme) : Colors.ToDictionary(c => c.Key, c => dark ? c.Value.Dark : c.Value.Light);
        foreach (var (key, value) in colors)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            b.Freeze();
            app.Resources[key] = b;
        }
        Font = font.Trim().Length > 0 ? new FontFamily(font.Trim()) : null;
        foreach (Window w in app.Windows) ApplyFont(w);
#pragma warning disable WPF0001 // the Fluent theme gives menus, lists and dialogs their dark look
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.None;
#pragma warning restore WPF0001
    }

    /// <summary>The user's font for every window (null = the system's).</summary>
    static FontFamily? Font;
    static bool hooked;

    /// <summary>Gives each window the chosen font when it opens.</summary>
    public static void HookWindows()
    {
        if (hooked) return;
        hooked = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => { if (s is Window w) ApplyFont(w); }));
    }

    static void ApplyFont(Window w)
    {
        if (Font != null) w.FontFamily = Font;
        else w.ClearValue(Control.FontFamilyProperty);
    }

    /// <summary>The font names installed on this computer, for the Options list.</summary>
    public static List<string> InstalledFonts() => Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(f => f).ToList();
}

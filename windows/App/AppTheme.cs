using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Nexonic.App;

/// <summary>Options → General → Appearance: light, dark, or the same as Windows (apps setting).</summary>
public static class AppTheme
{
    public static readonly string[] Choices = { "system", "light", "dark" };
    public static string Title(string c) => c switch { "light" => "Light", "dark" => "Dark", _ => "Same as Windows" };

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

    public static void Apply(string? choice)
    {
        if (Application.Current is not { } app) return;
        var dark = choice == "dark" || (choice is not ("light" or "dark") && WindowsIsDark());
        foreach (var (key, (light, darkColor)) in Colors)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? darkColor : light));
            b.Freeze();
            app.Resources[key] = b;
        }
#pragma warning disable WPF0001 // the Fluent theme gives menus, lists and dialogs their dark look
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.None;
#pragma warning restore WPF0001
    }
}

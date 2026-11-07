using System.Windows;
using System.Windows.Media;

namespace Nexonic.App;

/// <summary>Runtime WPF palettes, including a PaperGray-inspired color scheme.</summary>
public static class ThemeManager
{
    public static readonly string[] Names = ["Light", "Dark", "PaperGray", "Slate"];
    public static void Apply(string? name)
    {
        var colors = name switch
        {
            "Dark" => new[] { "#77ABFF", "#191C22", "#252A32", "#21262E", "#F1F3F6", "#ABB5C1", "#3D4653", "#343B46", "#40506A" },
            "PaperGray" => new[] { "#606064", "#E7E6E4", "#F5F4F2", "#DAD8D5", "#232326", "#606064", "#B9B6B1", "#D0CDCA", "#C4C0BC" },
            "Slate" => new[] { "#8BC5DF", "#202A34", "#2C3945", "#25313D", "#EEF3F7", "#AFC0CE", "#4D5F6B", "#3B4B59", "#4A5C6D" },
            _ => new[] { "#1F6FEB", "#F7F9FC", "#FFFFFF", "#EEF2F7", "#1F2328", "#57606A", "#E3E8EF", "#F3F7FE", "#DCE8FC" }
        };
        var keys = new[] { "AccentBrush", "WindowBrush", "SurfaceBrush", "SidebarBrush",
            "TextBrush", "MutedBrush", "BorderBrushTheme", "HoverBrush", "SelectedBrush" };
        for (int i = 0; i < keys.Length; i++)
            Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
}

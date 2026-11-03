using System.IO;
using System.Text.Json;
using System.Windows;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>Remembers where the main window was and how big, and puts it back there (if that place is still on a screen).</summary>
public static class WindowPlace
{
    sealed record Place(double Left, double Top, double Width, double Height, bool Maximized);

    static string SavedPath => Path.Combine(AppPaths.Support, "window.json");

    public static void Restore(Window w)
    {
        try
        {
            if (!File.Exists(SavedPath) || JsonSerializer.Deserialize<Place>(File.ReadAllText(SavedPath)) is not { } p) return;
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var place = new Rect(p.Left, p.Top, p.Width, p.Height);
            if (p.Width < w.MinWidth || p.Height < w.MinHeight || !screen.Contains(new Point(p.Left + 40, p.Top + 20))) return;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = place.Left;
            w.Top = place.Top;
            w.Width = Math.Min(place.Width, screen.Width);
            w.Height = Math.Min(place.Height, screen.Height);
            if (p.Maximized) w.WindowState = WindowState.Maximized;
        }
        catch { }
    }

    public static void Save(Window w)
    {
        try
        {
            var b = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight) : w.RestoreBounds;
            if (b.IsEmpty || b.Width <= 0) return;
            SafeFile.Write(SavedPath, JsonSerializer.Serialize(new Place(b.Left, b.Top, b.Width, b.Height, w.WindowState == WindowState.Maximized)));
        }
        catch { }
    }
}

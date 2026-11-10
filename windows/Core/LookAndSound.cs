using System.Globalization;
using System.Text.Json;

namespace Nexonic.Core;

/// <summary>
/// A colour scheme (Options → General → Appearance): a few colours from which the app draws every surface. Schemes can
/// be exported and imported as small JSON files. A scheme whose text would be hard to read (WCAG contrast below 4.5:1
/// for text, 3:1 for secondary text) is refused, so no scheme can make the app unreadable.
/// </summary>
public sealed class ColorScheme
{
    public string Name { get; set; } = "";
    /// <summary>"light" or "dark": the base the system parts (menus, dialogs) use.</summary>
    public string Base { get; set; } = "light";
    public string Background { get; set; } = "#F7F9FC";
    public string Panel { get; set; } = "#FFFFFF";
    public string Text { get; set; } = "#1F2328";
    public string Muted { get; set; } = "#57606A";
    public string Accent { get; set; } = "#0969DA";
    public string Line { get; set; } = "#E3E8EF";
    public string Selected { get; set; } = "#DCE8FC";

    public static readonly ColorScheme[] BuiltIn =
    {
        new() { Name = "Ocean", Base = "dark", Background = "#0E1A24", Panel = "#13222F", Text = "#E3EEF7", Muted = "#9DB4C7", Accent = "#3AA0F2", Line = "#24384A", Selected = "#1C3D5C" },
        new() { Name = "Forest", Base = "light", Background = "#F4F8F3", Panel = "#FFFFFF", Text = "#1D2B1F", Muted = "#4E6352", Accent = "#2E7D32", Line = "#DCE6DA", Selected = "#D8EBD6" },
        new() { Name = "Midnight", Base = "dark", Background = "#0B0B10", Panel = "#14141C", Text = "#EDEDF5", Muted = "#A3A3B8", Accent = "#8B7CF6", Line = "#26263A", Selected = "#2A2547" },
        new() { Name = "High Contrast", Base = "dark", Background = "#000000", Panel = "#000000", Text = "#FFFFFF", Muted = "#E0E0E0", Accent = "#FFD400", Line = "#FFFFFF", Selected = "#003C8F" },
    };

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Export() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads an exported scheme; null with the reason when it is not one or would be hard to read.</summary>
    public static (ColorScheme? Scheme, string? Problem) Import(string? json)
    {
        ColorScheme? s;
        try { s = JsonSerializer.Deserialize<ColorScheme>(json ?? "", Json); }
        catch (JsonException) { return (null, "This is not a colour scheme file."); }
        if (s == null) return (null, "This is not a colour scheme file.");
        s.Name = (s.Name ?? "").Trim();
        if (s.Name.Length is 0 or > 40) return (null, "The scheme needs a name (at most 40 characters).");
        s.Base = s.Base == "dark" ? "dark" : "light";
        return s.Problem() is { } p ? (null, p) : (s, null);
    }

    /// <summary>Null when every colour is valid and text is easy to read on the background and panels.</summary>
    public string? Problem()
    {
        foreach (var (name, value) in new[] { ("background", Background), ("panel", Panel), ("text", Text), ("secondary text", Muted),
                     ("accent", Accent), ("line", Line), ("selection", Selected) })
            if (Parse(value) == null) return $"The {name} colour must be written as #RRGGBB.";
        foreach (var surface in new[] { Background, Panel, Selected })
        {
            if (Contrast(Text, surface) < 4.5) return $"Text is too hard to read on {surface} (contrast below 4.5:1).";
            if (Contrast(Muted, surface) < 3.0) return $"Secondary text is too hard to read on {surface} (contrast below 3:1).";
        }
        return null;
    }

    public static (byte R, byte G, byte B)? Parse(string? hex)
    {
        var h = (hex ?? "").Trim();
        if (h.Length != 7 || h[0] != '#' || !int.TryParse(h[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
        return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>WCAG 2 contrast ratio of two colours (1 to 21).</summary>
    public static double Contrast(string a, string b)
    {
        static double L(string hex)
        {
            var (r, g, bl) = Parse(hex) ?? (0, 0, 0);
            static double C(byte c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * C(r) + 0.7152 * C(g) + 0.0722 * C(bl);
        }
        var (x, y) = (L(a), L(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>A colour between two others (for hover and pressed shades the scheme does not name).</summary>
    public static string Mix(string a, string b, double t)
    {
        var (r1, g1, b1) = Parse(a) ?? (0, 0, 0);
        var (r2, g2, b2) = Parse(b) ?? (0, 0, 0);
        byte M(byte x, byte y) => (byte)Math.Round(x + (y - x) * Math.Clamp(t, 0, 1));
        return $"#{M(r1, r2):X2}{M(g1, g2):X2}{M(b1, b2):X2}";
    }
}

/// <summary>
/// Sounds (Options → After Download): a sound for a finished download, a failed one and a finished queue — the system's
/// sound, a sound file of your own, or none — and quiet hours during which nothing plays.
/// </summary>
public static class Sounds
{
    public enum Event { Finished, Failed, QueueDone }

    /// <summary>"" = the system's sound, "none" = silent, else a sound file.</summary>
    public static string Choice(AppSettings s, Event e) => e switch
    {
        Event.Finished => s.SoundFinished,
        Event.Failed => s.SoundFailed,
        _ => s.SoundQueueDone,
    } ?? "";

    /// <summary>What to play now: null for nothing, "" for the system's sound, else the file.</summary>
    public static string? Pick(AppSettings s, Event e, DateTime local)
    {
        if (!s.PlaySound) return null;
        if (InQuietHours(s.QuietHours, local)) return null;
        var c = Choice(s, e).Trim();
        if (c == "none") return null;
        if (c.Length == 0) return e == Event.Failed ? null : "";  // failures are silent unless a sound is chosen
        return File.Exists(c) ? c : "";                             // a moved file falls back to the system's sound
    }

    /// <summary>"22:00-07:00" (may pass midnight); empty = no quiet hours.</summary>
    public static bool InQuietHours(string? range, DateTime local)
    {
        var p = (range ?? "").Split('-');
        if (p.Length != 2 || !TimeSpan.TryParseExact(p[0].Trim(), @"hh\:mm", null, out var a) || !TimeSpan.TryParseExact(p[1].Trim(), @"hh\:mm", null, out var b) || a == b)
            return false;
        var t = local.TimeOfDay;
        return a < b ? t >= a && t < b : t >= a || t < b;
    }

    /// <summary>Null when the sound setting can be used on this system, else why not.</summary>
    public static string? Problem(string? choice, string? quiet)
    {
        var c = (choice ?? "").Trim();
        if (c.Length > 0 && c != "none")
        {
            if (!Path.IsPathFullyQualified(c) || !File.Exists(c)) return $"There is no sound file at {c}.";
            var ext = Path.GetExtension(c).ToLowerInvariant();
            var ok = OperatingSystem.IsWindows() ? ext == ".wav" : ext is ".wav" or ".ogg" or ".oga" or ".flac" || OperatingSystem.IsMacOS() && ext is ".aiff" or ".mp3" or ".m4a";
            if (!ok) return OperatingSystem.IsWindows() ? "Windows plays .wav sound files." : "Choose a .wav, .ogg or .flac sound file.";
        }
        var q = (quiet ?? "").Trim();
        var parts = q.Split('-');
        if (q.Length > 0 && (parts.Length != 2 || !TimeSpan.TryParseExact(parts[0].Trim(), @"hh\:mm", null, out _) || !TimeSpan.TryParseExact(parts[1].Trim(), @"hh\:mm", null, out _)))
            return "Write quiet hours as start-end, for example 22:00-07:00.";
        return null;
    }
}

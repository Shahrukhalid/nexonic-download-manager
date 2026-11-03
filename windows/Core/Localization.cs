using System.Globalization;
using System.Text.Json;

namespace Nexonic.Core;

/// <summary>
/// The app's languages. Texts are looked up by their English wording; anything not translated stays in English.
/// Translations live in Locales/*.json and are marked when they still need review by a native speaker.
/// </summary>
public static class L
{
    public sealed record Language(string Code, string Name, bool RightToLeft, bool Reviewed);

    /// <summary>English first; the others are machine translations until a native speaker has checked them.</summary>
    public static readonly Language[] Languages =
    {
        new("en", "English", false, true),
        new("ar", "العربية", true, false),
        new("de", "Deutsch", false, false),
        new("es", "Español", false, false),
        new("fr", "Français", false, false),
        new("hi", "हिन्दी", false, false),
        new("pt", "Português", false, false),
        new("ur", "اردو", true, false),
    };

    static Dictionary<string, string> map = new();

    public static string Code { get; private set; } = "en";
    public static bool RightToLeft { get; private set; }

    /// <summary>The translation of an English text in the language in use (the text itself when there is none).</summary>
    public static string T(string english) => map.TryGetValue(english, out var t) && t.Length > 0 ? t : english;

    /// <summary>Switches to a language: a code ("ur"), or empty for the system's language. Unknown codes use English.</summary>
    public static void Use(string? setting)
    {
        var code = Resolve(setting);
        Code = code;
        RightToLeft = Languages.First(l => l.Code == code).RightToLeft;
        map = code == "en" ? new() : Load(code) ?? new();
    }

    public static string Resolve(string? setting)
    {
        var code = string.IsNullOrWhiteSpace(setting) ? SystemLanguage() : setting.Trim().ToLowerInvariant();
        return Languages.Any(l => l.Code == code) ? code : "en";
    }

    /// <summary>The desktop's language: LANGUAGE/LC_ALL/LC_MESSAGES/LANG on Linux, the UI culture elsewhere.</summary>
    static string SystemLanguage()
    {
        foreach (var name in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
            if (Environment.GetEnvironmentVariable(name) is { Length: >= 2 } v && v != "C" && !v.StartsWith("C.") && v != "POSIX")
                return v[..2].ToLowerInvariant();
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    }

    public sealed class File
    {
        public string Language { get; set; } = "";
        public string Status { get; set; } = "";
        public Dictionary<string, string> Strings { get; set; } = new();
    }

    /// <summary>A language's file, built into the app.</summary>
    public static File? Read(string code)
    {
        using var s = typeof(L).Assembly.GetManifestResourceStream($"locale.{code}.json");
        if (s == null) return null;
        return JsonSerializer.Deserialize<File>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    static Dictionary<string, string>? Load(string code) => Read(code)?.Strings;
}

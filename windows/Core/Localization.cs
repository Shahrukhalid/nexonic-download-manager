using System.Globalization;
using System.Text.Json;

namespace Nexonic.Core;

/// <summary>
/// The app's languages. Texts are looked up by their English wording; anything not translated stays in English.
/// Translations live in Locales/*.json.
/// </summary>
public static class L
{
    public sealed record Language(string Code, string Name, bool RightToLeft, bool Reviewed);

    /// <summary>English first. Reviewed = the translation has been checked.</summary>
    public static readonly Language[] Languages =
    {
        new("en", "English", false, true),
        new("ar", "العربية", true, true),
        new("de", "Deutsch", false, true),
        new("es", "Español", false, true),
        new("fr", "Français", false, true),
        new("hi", "हिन्दी", false, true),
        new("id", "Bahasa Indonesia", false, true),
        new("ja", "日本語", false, true),
        new("ko", "한국어", false, true),
        new("pt", "Português", false, true),
        new("ru", "Русский", false, true),
        new("tr", "Türkçe", false, true),
        new("ur", "اردو", true, true),
        new("zh-cn", "简体中文", false, true),
        new("zh-tw", "繁體中文", false, true),
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
        var code = Normalize(string.IsNullOrWhiteSpace(setting) ? SystemLanguage() : setting);
        return Languages.Any(l => l.Code == code) ? code : "en";
    }

    /// <summary>"zh_TW.UTF-8" → "zh-tw", "zh-Hans" → "zh-cn", "in" → "id", "pt-BR" → "pt".</summary>
    public static string Normalize(string tag)
    {
        var t = tag.Trim().Split('.', '@')[0].Replace('_', '-').ToLowerInvariant();
        var lang = t.Split('-')[0];
        if (lang == "zh")
            return t.Contains("hant") || t.Contains("-tw") || t.Contains("-hk") || t.Contains("-mo") ? "zh-tw" : "zh-cn";
        if (lang == "in") return "id";
        return Languages.Any(l => l.Code == t) ? t : lang;
    }

    /// <summary>The desktop's language: LANGUAGE/LC_ALL/LC_MESSAGES/LANG on Linux, the UI culture elsewhere.</summary>
    static string SystemLanguage()
    {
        foreach (var name in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
            if (Environment.GetEnvironmentVariable(name) is { Length: >= 2 } v && v != "C" && !v.StartsWith("C.") && v != "POSIX")
                return v.Split(':')[0];
        return CultureInfo.CurrentUICulture.Name;
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

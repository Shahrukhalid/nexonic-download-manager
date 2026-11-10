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
        // BEGIN LANGUAGES (written by scripts/translate/translate.py apply)
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
        new("af", "Afrikaans", false, false),
        new("am", "አማርኛ", false, false),
        new("as", "অসমীয়া", false, false),
        new("az", "Azərbaycan", false, false),
        new("be", "Беларуская", false, false),
        new("bg", "Български", false, false),
        new("bn", "বাংলা", false, false),
        new("bs", "Bosanski", false, false),
        new("ca", "Català", false, false),
        new("ceb", "Cebuano", false, false),
        new("ckb", "کوردیی ناوەندی", true, false),
        new("cs", "Čeština", false, false),
        new("cy", "Cymraeg", false, false),
        new("da", "Dansk", false, false),
        new("el", "Ελληνικά", false, false),
        new("eo", "Esperanto", false, false),
        new("et", "Eesti", false, false),
        new("eu", "Euskara", false, false),
        new("fa", "فارسی", true, false),
        new("fi", "Suomi", false, false),
        new("fil", "Filipino", false, false),
        new("fy", "Frysk", false, false),
        new("ga", "Gaeilge", false, false),
        new("gd", "Gàidhlig", false, false),
        new("gl", "Galego", false, false),
        new("gu", "ગુજરાતી", false, false),
        new("ha", "Hausa", false, false),
        new("haw", "ʻŌlelo Hawaiʻi", false, false),
        new("he", "עברית", true, false),
        new("hmn", "Hmoob", false, false),
        new("hr", "Hrvatski", false, false),
        new("ht", "Kreyòl ayisyen", false, false),
        new("hu", "Magyar", false, false),
        new("hy", "Հայերեն", false, false),
        new("ig", "Igbo", false, false),
        new("is", "Íslenska", false, false),
        new("it", "Italiano", false, false),
        new("jv", "Basa Jawa", false, false),
        new("ka", "ქართული", false, false),
        new("kk", "Қазақ тілі", false, false),
        new("km", "ខ្មែរ", false, false),
        new("kn", "ಕನ್ನಡ", false, false),
        new("ku", "Kurdî", false, false),
        new("ky", "Кыргызча", false, false),
        new("la", "Latina", false, false),
        new("lb", "Lëtzebuergesch", false, false),
        new("lo", "ລາວ", false, false),
        new("lt", "Lietuvių", false, false),
        new("lv", "Latviešu", false, false),
        new("mg", "Malagasy", false, false),
        new("mi", "Te Reo Māori", false, false),
        new("mk", "Македонски", false, false),
        new("ml", "മലയാളം", false, false),
        new("mn", "Монгол", false, false),
        new("mr", "मराठी", false, false),
        new("ms", "Bahasa Melayu", false, false),
        new("mt", "Malti", false, false),
        new("my", "မြန်မာ", false, false),
        new("nb", "Norsk bokmål", false, false),
        new("ne", "नेपाली", false, false),
        new("nl", "Nederlands", false, false),
        new("ny", "Chichewa", false, false),
        new("or", "ଓଡ଼ିଆ", false, false),
        new("pa", "ਪੰਜਾਬੀ", false, false),
        new("pl", "Polski", false, false),
        new("ps", "پښتو", true, false),
        new("ro", "Română", false, false),
        new("rw", "Kinyarwanda", false, false),
        new("sd", "سنڌي", true, false),
        new("si", "සිංහල", false, false),
        new("sk", "Slovenčina", false, false),
        new("sl", "Slovenščina", false, false),
        new("sm", "Gagana Samoa", false, false),
        new("sn", "chiShona", false, false),
        new("so", "Soomaali", false, false),
        new("sq", "Shqip", false, false),
        new("sr", "Српски", false, false),
        new("st", "Sesotho", false, false),
        new("su", "Basa Sunda", false, false),
        new("sv", "Svenska", false, false),
        new("sw", "Kiswahili", false, false),
        new("ta", "தமிழ்", false, false),
        new("te", "తెలుగు", false, false),
        new("tg", "Тоҷикӣ", false, false),
        new("th", "ไทย", false, false),
        new("tk", "Türkmen", false, false),
        new("tt", "Татарча", false, false),
        new("ug", "ئۇيغۇرچە", true, false),
        new("uk", "Українська", false, false),
        new("uz", "Oʻzbek", false, false),
        new("vi", "Tiếng Việt", false, false),
        new("xh", "isiXhosa", false, false),
        new("yi", "ייִדיש", true, false),
        new("yo", "Yorùbá", false, false),
        new("zu", "isiZulu", false, false),
        // END LANGUAGES
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
        if (lang == "iw") return "he";
        if (lang == "ji") return "yi";
        if (lang is "tl") return "fil";
        if (lang is "no" or "nn") return "nb";
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

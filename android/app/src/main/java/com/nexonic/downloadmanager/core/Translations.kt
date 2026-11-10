package com.nexonic.downloadmanager.core

/**
 * Interface translations. English is the default; the files in assets/locales/<code>.json use the
 * same format as the desktop apps ({"language", "status", "strings": {"English": "Translation"}}).
 * Anything without a translation stays in English.
 */
object Translations {
    // BEGIN LANGUAGES (written by scripts/translate/translate.py apply)
    val languages = listOf("ar", "de", "es", "fr", "hi", "id", "ja", "ko", "pt", "ru", "tr", "ur", "zh-cn", "zh-tw", "af", "am", "as", "az", "be", "bg", "bn", "bs", "ca", "ceb", "ckb", "cs", "cy", "da", "el", "eo", "et", "eu", "fa", "fi", "fil", "fy", "ga", "gd", "gl", "gu", "ha", "haw", "he", "hmn", "hr", "ht", "hu", "hy", "ig", "is", "it", "jv", "ka", "kk", "km", "kn", "ku", "ky", "la", "lb", "lo", "lt", "lv", "mg", "mi", "mk", "ml", "mn", "mr", "ms", "mt", "my", "nb", "ne", "nl", "ny", "or", "pa", "pl", "ps", "ro", "rw", "sd", "si", "sk", "sl", "sm", "sn", "so", "sq", "sr", "st", "su", "sv", "sw", "ta", "te", "tg", "th", "tk", "tt", "ug", "uk", "uz", "vi", "xh", "yi", "yo", "zu")

    /** Each language's own name, for the picker. */
    val names = mapOf(
        "en" to "English",
        "ar" to "العربية",
        "de" to "Deutsch",
        "es" to "Español",
        "fr" to "Français",
        "hi" to "हिन्दी",
        "id" to "Bahasa Indonesia",
        "ja" to "日本語",
        "ko" to "한국어",
        "pt" to "Português",
        "ru" to "Русский",
        "tr" to "Türkçe",
        "ur" to "اردو",
        "zh-cn" to "简体中文",
        "zh-tw" to "繁體中文",
        "af" to "Afrikaans",
        "am" to "አማርኛ",
        "as" to "অসমীয়া",
        "az" to "Azərbaycan",
        "be" to "Беларуская",
        "bg" to "Български",
        "bn" to "বাংলা",
        "bs" to "Bosanski",
        "ca" to "Català",
        "ceb" to "Cebuano",
        "ckb" to "کوردیی ناوەندی",
        "cs" to "Čeština",
        "cy" to "Cymraeg",
        "da" to "Dansk",
        "el" to "Ελληνικά",
        "eo" to "Esperanto",
        "et" to "Eesti",
        "eu" to "Euskara",
        "fa" to "فارسی",
        "fi" to "Suomi",
        "fil" to "Filipino",
        "fy" to "Frysk",
        "ga" to "Gaeilge",
        "gd" to "Gàidhlig",
        "gl" to "Galego",
        "gu" to "ગુજરાતી",
        "ha" to "Hausa",
        "haw" to "ʻŌlelo Hawaiʻi",
        "he" to "עברית",
        "hmn" to "Hmoob",
        "hr" to "Hrvatski",
        "ht" to "Kreyòl ayisyen",
        "hu" to "Magyar",
        "hy" to "Հայերեն",
        "ig" to "Igbo",
        "is" to "Íslenska",
        "it" to "Italiano",
        "jv" to "Basa Jawa",
        "ka" to "ქართული",
        "kk" to "Қазақ тілі",
        "km" to "ខ្មែរ",
        "kn" to "ಕನ್ನಡ",
        "ku" to "Kurdî",
        "ky" to "Кыргызча",
        "la" to "Latina",
        "lb" to "Lëtzebuergesch",
        "lo" to "ລາວ",
        "lt" to "Lietuvių",
        "lv" to "Latviešu",
        "mg" to "Malagasy",
        "mi" to "Te Reo Māori",
        "mk" to "Македонски",
        "ml" to "മലയാളം",
        "mn" to "Монгол",
        "mr" to "मराठी",
        "ms" to "Bahasa Melayu",
        "mt" to "Malti",
        "my" to "မြန်မာ",
        "nb" to "Norsk bokmål",
        "ne" to "नेपाली",
        "nl" to "Nederlands",
        "ny" to "Chichewa",
        "or" to "ଓଡ଼ିଆ",
        "pa" to "ਪੰਜਾਬੀ",
        "pl" to "Polski",
        "ps" to "پښتو",
        "ro" to "Română",
        "rw" to "Kinyarwanda",
        "sd" to "سنڌي",
        "si" to "සිංහල",
        "sk" to "Slovenčina",
        "sl" to "Slovenščina",
        "sm" to "Gagana Samoa",
        "sn" to "chiShona",
        "so" to "Soomaali",
        "sq" to "Shqip",
        "sr" to "Српски",
        "st" to "Sesotho",
        "su" to "Basa Sunda",
        "sv" to "Svenska",
        "sw" to "Kiswahili",
        "ta" to "தமிழ்",
        "te" to "తెలుగు",
        "tg" to "Тоҷикӣ",
        "th" to "ไทย",
        "tk" to "Türkmen",
        "tt" to "Татарча",
        "ug" to "ئۇيغۇرچە",
        "uk" to "Українська",
        "uz" to "Oʻzbek",
        "vi" to "Tiếng Việt",
        "xh" to "isiXhosa",
        "yi" to "ייִדיש",
        "yo" to "Yorùbá",
        "zu" to "isiZulu",
    )

    /** Languages written right to left. */
    val rightToLeft = setOf("ar", "ur", "ckb", "fa", "he", "ps", "sd", "ug", "yi")
    // END LANGUAGES

    /** A system tag ("zh-Hant-TW", "in-ID", "pt_BR") to a file code ("zh-tw", "id", "pt"). */
    fun normalize(tag: String): String {
        val t = tag.replace('_', '-').lowercase()
        val lang = t.substringBefore('-')
        return when (lang) {
            "zh" -> if (listOf("hant", "-tw", "-hk", "-mo").any { t.contains(it) }) "zh-tw" else "zh-cn"
            "in" -> "id"
            "iw" -> "he"
            "ji" -> "yi"
            "tl" -> "fil"
            "no", "nn" -> "nb"
            else -> lang
        }
    }

    /** The Android locale tag for a file code. */
    fun tag(code: String): String = when (code) { "zh-cn" -> "zh-CN"; "zh-tw" -> "zh-TW"; "id" -> "in"; "he" -> "iw"; else -> code }

    @Volatile var strings: Map<String, String> = emptyMap()
    @Volatile var code = "en"

    fun t(english: String): String = strings[english]?.takeIf { it.isNotEmpty() } ?: english

    /** Loads [code]'s strings with [read] (returns a file's text or null); unknown codes fall back to English. */
    fun use(language: String, read: (String) -> String?) {
        val c = normalize(language)
        if (c == code && strings.isNotEmpty()) return
        val text = if (c in languages) runCatching { read("locales/$c.json") }.getOrNull() else null
        strings = text?.let { runCatching { parse(it).second }.getOrNull() } ?: emptyMap()
        code = if (strings.isEmpty()) "en" else c
    }

    /** Parses a locale file: returns its status line and its strings. */
    fun parse(json: String): Pair<String, Map<String, String>> {
        val root = Reader(json).value() as? Map<*, *> ?: error("not an object")
        val strings = (root["strings"] as? Map<*, *>)?.entries?.associate { it.key as String to it.value as String } ?: emptyMap()
        return (root["status"] as? String ?: "") to strings
    }

    /** Small reader for the subset of JSON the locale files use: objects, arrays, strings, numbers, true/false/null. */
    private class Reader(val s: String) {
        var i = 0
        fun ws() { while (i < s.length && s[i].isWhitespace()) i++ }
        fun value(): Any? {
            ws()
            return when (s[i]) {
                '{' -> obj(); '[' -> arr(); '"' -> str()
                else -> { val start = i; while (i < s.length && s[i] !in ",}] \n\r\t") i++; s.substring(start, i) }
            }
        }
        fun obj(): Map<String, Any?> {
            val m = LinkedHashMap<String, Any?>(); i++; ws()
            if (s[i] == '}') { i++; return m }
            while (true) {
                ws(); val k = str(); ws(); check(s[i++] == ':'); m[k] = value(); ws()
                if (s[i++] == '}') return m
            }
        }
        fun arr(): List<Any?> {
            val l = ArrayList<Any?>(); i++; ws()
            if (s[i] == ']') { i++; return l }
            while (true) { l += value(); ws(); if (s[i++] == ']') return l }
        }
        fun str(): String {
            check(s[i++] == '"'); val b = StringBuilder()
            while (true) {
                val c = s[i++]
                when (c) {
                    '"' -> return b.toString()
                    '\\' -> when (val e = s[i++]) {
                        'n' -> b.append('\n'); 't' -> b.append('\t'); 'r' -> b.append('\r'); 'b' -> b.append('\b'); 'f' -> b.append('\u000c')
                        'u' -> { b.append(s.substring(i, i + 4).toInt(16).toChar()); i += 4 }
                        else -> b.append(e)
                    }
                    else -> b.append(c)
                }
            }
        }
    }
}

/** Translates an English interface text (unchanged when there is no translation). */
fun tr(english: String): String = Translations.t(english)

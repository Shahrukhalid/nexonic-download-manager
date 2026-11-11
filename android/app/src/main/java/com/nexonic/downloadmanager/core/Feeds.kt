package com.nexonic.downloadmanager.core

import org.json.JSONArray
import org.json.JSONObject
import org.w3c.dom.Element
import java.io.ByteArrayInputStream
import java.io.IOException
import java.util.Locale
import java.util.UUID
import javax.xml.parsers.DocumentBuilderFactory

/** An RSS or Atom feed whose new items are downloaded when they match the rules (the same rules as the desktop apps). */
data class FeedSubscription(
    val id: String = UUID.randomUUID().toString(),
    var name: String = "",
    var url: String = "",
    var enabled: Boolean = true,
    var intervalMinutes: Int = 30,
    var include: String = "",
    var exclude: String = "",
    var quality: String = "",
    var minSizeMB: Int = 0,
    var maxSizeMB: Int = 0,
    var oneOfEachEpisode: Boolean = false,
    var skipExisting: Boolean = true,
    var lastChecked: Long = 0,
    var lastResult: String = "",
    val seen: MutableList<String> = mutableListOf(),
    val episodes: MutableList<String> = mutableListOf(),
) {
    fun toJson(): JSONObject = JSONObject().put("id", id).put("name", name).put("url", url).put("enabled", enabled)
        .put("intervalMinutes", intervalMinutes).put("include", include).put("exclude", exclude).put("quality", quality)
        .put("minSizeMB", minSizeMB).put("maxSizeMB", maxSizeMB).put("oneOfEachEpisode", oneOfEachEpisode)
        .put("skipExisting", skipExisting).put("lastChecked", lastChecked).put("lastResult", lastResult)
        .put("seen", JSONArray(seen)).put("episodes", JSONArray(episodes))

    companion object {
        fun fromJson(o: JSONObject): FeedSubscription {
            fun list(key: String) = o.optJSONArray(key)?.let { a -> MutableList(a.length()) { a.optString(it) } } ?: mutableListOf()
            return FeedSubscription(
                id = o.optString("id").ifEmpty { UUID.randomUUID().toString() }, name = o.optString("name"), url = o.optString("url"),
                enabled = o.optBoolean("enabled", true), intervalMinutes = o.optInt("intervalMinutes", 30), include = o.optString("include"),
                exclude = o.optString("exclude"), quality = o.optString("quality"), minSizeMB = o.optInt("minSizeMB"), maxSizeMB = o.optInt("maxSizeMB"),
                oneOfEachEpisode = o.optBoolean("oneOfEachEpisode"), skipExisting = o.optBoolean("skipExisting", true),
                lastChecked = o.optLong("lastChecked"), lastResult = o.optString("lastResult"), seen = list("seen"), episodes = list("episodes"),
            )
        }
    }
}

data class FeedItem(val id: String, val title: String, val link: String, val size: Long?)

object Feeds {
    const val MAX_SEEN = 5000

    fun toJson(feeds: List<FeedSubscription>): String = JSONArray(feeds.map { it.toJson() }).toString()
    fun fromJson(text: String?): MutableList<FeedSubscription> = runCatching {
        val a = JSONArray(text ?: "[]"); MutableList(a.length()) { FeedSubscription.fromJson(a.getJSONObject(it)) }
    }.getOrDefault(mutableListOf())

    private fun isLink(s: String?): Boolean =
        !s.isNullOrBlank() && (s.startsWith("magnet:?", true) || s.startsWith("https://", true) || s.startsWith("http://", true))

    /** RSS 2.0, RSS 1.0 and Atom (no DTDs or external entities). The link is the enclosure, else a magnet, else the item's link. */
    fun parse(xml: ByteArray): List<FeedItem> {
        val f = DocumentBuilderFactory.newInstance()
        f.isNamespaceAware = true
        runCatching { f.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true) }
        runCatching { f.setFeature("http://xml.org/sax/features/external-general-entities", false) }
        runCatching { f.setFeature("http://xml.org/sax/features/external-parameter-entities", false) }
        f.isExpandEntityReferences = false
        val text = String(xml, Charsets.UTF_8)
        if (text.contains("<!DOCTYPE", ignoreCase = true)) throw IOException("The feed is not valid.")
        val doc = f.newDocumentBuilder().parse(ByteArrayInputStream(xml))
        val out = mutableListOf<FeedItem>()
        val all = doc.getElementsByTagName("*")
        for (n in 0 until all.length) {
            val e = all.item(n) as Element
            val name = e.localName ?: e.tagName.substringAfter(':')
            if (name != "item" && name != "entry") continue
            val kids = (0 until e.childNodes.length).mapNotNull { e.childNodes.item(it) as? Element }
            fun local(c: Element) = c.localName ?: c.tagName.substringAfter(':')
            fun child(n: String) = kids.firstOrNull { local(it) == n }?.textContent?.trim()
            val enclosure = kids.firstOrNull { local(it) == "enclosure" }
            val links = kids.filter { local(it) == "link" && it.hasAttribute("href") }
            val atomEnclosure = links.firstOrNull { it.getAttribute("rel") == "enclosure" }
            val atomLink = links.firstOrNull { it.getAttribute("rel").isEmpty() || it.getAttribute("rel") == "alternate" }?.getAttribute("href")
            val inner = e.getElementsByTagName("*")
            val magnet = (0 until inner.length).map { inner.item(it) as Element }
                .firstOrNull { local(it) == "magnetURI" }?.textContent?.trim()
            val link = listOf(enclosure?.getAttribute("url"), atomEnclosure?.getAttribute("href"), magnet, atomLink, child("link"))
                .firstOrNull(::isLink) ?: continue
            val size = (enclosure ?: atomEnclosure)?.getAttribute("length")?.toLongOrNull() ?: child("contentLength")?.toLongOrNull()
                ?: child("size")?.let(::parseSize)
            out += FeedItem((child("guid") ?: child("id") ?: link).trim(), child("title") ?: "", link.trim(), size)
        }
        return out
    }

    /** "1.4 GiB", "700 MB", "123456" → bytes. */
    fun parseSize(text: String): Long? {
        val m = Regex("""^(\d+(?:[.,]\d+)?)\s*([KMGT]i?B|B)?$""", RegexOption.IGNORE_CASE).find(text.trim()) ?: return null
        val v = m.groupValues[1].replace(',', '.').toDoubleOrNull() ?: return null
        val u = m.groupValues[2].uppercase(Locale.ROOT)
        val mult = when { u.startsWith("K") -> 1024.0; u.startsWith("M") -> 1048576.0; u.startsWith("G") -> 1073741824.0; u.startsWith("T") -> 1099511627776.0; else -> 1.0 }
        return if (v > 0) (v * mult).toLong() else null
    }

    private fun matches(title: String, rule: String, any: Boolean): Boolean {
        val r = rule.trim()
        if (r.length > 2 && r.startsWith("/") && r.endsWith("/"))
            return runCatching { Regex(r.substring(1, r.length - 1), RegexOption.IGNORE_CASE).containsMatchIn(title) }.getOrDefault(false)
        val words = r.split(Regex("\\s+")).filter { it.isNotEmpty() }
        return if (any) words.any { title.contains(it, true) } else words.all { title.contains(it, true) }
    }

    /** A problem with a rule (an invalid regular expression), or null. */
    fun ruleProblem(rule: String): String? {
        val r = rule.trim()
        if (r.length > 2 && r.startsWith("/") && r.endsWith("/") && runCatching { Regex(r.substring(1, r.length - 1)) }.isFailure)
            return "The rule's regular expression is not valid."
        return null
    }

    fun wanted(f: FeedSubscription, i: FeedItem): Boolean =
        (f.include.isBlank() || matches(i.title, f.include, false)) && (f.exclude.isBlank() || !matches(i.title, f.exclude, true))

    /** "show name|s01e02" (or "show name|2026-11-10"), or null when the title names no episode. */
    fun episodeKey(title: String): String? {
        val se = Regex("""\b[Ss](\d{1,2})[ ._-]?[Ee](\d{1,4})\b|\b(\d{1,2})x(\d{2,3})\b""").find(title)
        val ep: String; val at: Int
        if (se != null) {
            val season = (se.groups[1] ?: se.groups[3])!!.value.toInt(); val episode = (se.groups[2] ?: se.groups[4])!!.value.toInt()
            ep = String.format(Locale.ROOT, "s%02de%02d", season, episode); at = se.range.first
        } else {
            val d = Regex("""\b(20\d{2})[ ._-](\d{2})[ ._-](\d{2})\b""").find(title) ?: return null
            ep = "${d.groupValues[1]}-${d.groupValues[2]}-${d.groupValues[3]}"; at = d.range.first
        }
        val show = title.substring(0, at).replace(Regex("""\[[^\]]*\]|\([^)]*\)"""), " ").lowercase(Locale.ROOT)
            .replace(Regex("""[^\p{L}\p{Nd}]+"""), " ").trim()
        return if (show.isEmpty()) null else "$show|$ep"
    }

    /** The quality named in a title ("1080p"; 4k/uhd count as 2160p), or "". */
    fun qualityOf(title: String): String {
        val q = Regex("""\b(4320p|2160p|1440p|1080p|720p|576p|480p|360p|4k|8k|uhd)\b""", RegexOption.IGNORE_CASE).find(title)?.value?.lowercase(Locale.ROOT) ?: ""
        return when (q) { "4k", "uhd" -> "2160p"; "8k" -> "4320p"; else -> q }
    }

    /** The new items to download: rules, size limits, preferred qualities and one per episode (best quality wins). */
    fun choose(f: FeedSubscription, items: List<FeedItem>): List<FeedItem> {
        val prefs = f.quality.lowercase(Locale.ROOT).split(Regex("\\s+")).filter { it.isNotEmpty() }.map { if (it == "4k" || it == "uhd") "2160p" else it }
        fun rank(i: FeedItem) = if (prefs.isEmpty()) 0 else prefs.indexOf(qualityOf(i.title)).let { if (it < 0) Int.MAX_VALUE else it }
        val chosen = LinkedHashMap<String, FeedItem>()
        val out = mutableListOf<FeedItem>()
        for (i in items) {
            if (!wanted(f, i)) continue
            if (f.minSizeMB > 0 && i.size != null && i.size < f.minSizeMB * 1048576L) continue
            if (f.maxSizeMB > 0 && i.size != null && i.size > f.maxSizeMB * 1048576L) continue
            if (prefs.isNotEmpty() && rank(i) == Int.MAX_VALUE) continue
            val key = if (f.oneOfEachEpisode) episodeKey(i.title) else null
            if (key != null) {
                if (key in f.episodes) continue
                val best = chosen[key]
                if (best == null || rank(i) < rank(best)) chosen[key] = i
                continue
            }
            out += i
        }
        return out + chosen.values
    }

    /** Marks every new item as seen and returns the ones to download (the first check with skipExisting only remembers). */
    fun check(f: FeedSubscription, xml: ByteArray, now: Long = System.currentTimeMillis()): List<FeedItem> {
        val items = parse(xml)
        val first = f.lastChecked == 0L
        val fresh = items.filter { it.id !in f.seen }
        f.seen += fresh.map { it.id }
        if (f.seen.size > MAX_SEEN) repeat(f.seen.size - MAX_SEEN) { f.seen.removeAt(0) }
        f.lastChecked = now
        val wanted = if (first && f.skipExisting) emptyList() else choose(f, fresh)
        if (f.oneOfEachEpisode) f.episodes += wanted.mapNotNull { episodeKey(it.title) }
        f.lastResult = "${items.size} items, ${wanted.size} new to download"
        return wanted
    }

    fun due(f: FeedSubscription, now: Long = System.currentTimeMillis()): Boolean =
        f.enabled && (f.lastChecked == 0L || now - f.lastChecked >= maxOf(15, f.intervalMinutes) * 60_000L)
}

enum class DebridService(val title: String) { NONE("Off"), REAL_DEBRID("Real-Debrid"), ALL_DEBRID("AllDebrid"), PREMIUMIZE("Premiumize") }

/** An optional debrid account (your own API key; off by default), used only for the sites listed. */
object Debrid {
    data class Request(val url: String, val form: Map<String, String>, val bearer: String?)

    fun applies(url: String, service: DebridService, key: String, hosts: String): Boolean {
        if (service == DebridService.NONE || key.isBlank()) return false
        val host = runCatching { java.net.URI(url) }.getOrNull()?.takeIf { it.scheme.equals("http", true) || it.scheme.equals("https", true) }
            ?.host?.lowercase(Locale.ROOT) ?: return false
        return hosts.split(Regex("[,;\\s]+")).map { it.trim().lowercase(Locale.ROOT).trimStart('.') }
            .any { it.isNotEmpty() && (host == it || host.endsWith(".$it")) }
    }

    fun request(service: DebridService, key: String, link: String): Request = when (service) {
        DebridService.REAL_DEBRID -> Request("https://api.real-debrid.com/rest/1.0/unrestrict/link", mapOf("link" to link), key.trim())
        DebridService.ALL_DEBRID -> Request("https://api.alldebrid.com/v4/link/unlock?agent=nexonic", mapOf("link" to link), key.trim())
        DebridService.PREMIUMIZE -> Request("https://www.premiumize.me/api/transfer/directdl", mapOf("apikey" to key.trim(), "src" to link), null)
        DebridService.NONE -> throw IOException("No debrid service is chosen.")
    }

    fun parseAnswer(service: DebridService, json: String): String {
        val o = runCatching { JSONObject(json) }.getOrDefault(JSONObject())
        val link = when (service) {
            DebridService.REAL_DEBRID -> o.optString("download")
            DebridService.ALL_DEBRID -> o.optJSONObject("data")?.optString("link")
            DebridService.PREMIUMIZE -> o.optJSONArray("content")?.optJSONObject(0)?.optString("link")?.ifEmpty { null } ?: o.optString("location")
            DebridService.NONE -> null
        }
        if (!link.isNullOrEmpty() && Links.isWebLink(link)) return link
        val message = o.optJSONObject("error")?.optString("message") ?: o.optString("error").ifEmpty { o.optString("message") }
        throw IOException("${service.title} could not unlock the link" + if (message.isNullOrEmpty()) "." else ": $message")
    }

    fun formBody(form: Map<String, String>): String =
        form.entries.sortedBy { it.key }.joinToString("&") { java.net.URLEncoder.encode(it.key, "UTF-8") + "=" + java.net.URLEncoder.encode(it.value, "UTF-8") }
}

/** Colour schemes (the same built-in ones as the desktop apps): a light or dark base, an accent and a background. */
data class ColorSchemeChoice(val name: String, val dark: Boolean, val accent: Long, val background: Long, val surface: Long) {
    companion object {
        val builtIn = listOf(
            ColorSchemeChoice("Ocean", true, 0xFF3AA0F2, 0xFF0E1A24, 0xFF13222F),
            ColorSchemeChoice("Forest", false, 0xFF2E7D32, 0xFFF4F8F3, 0xFFFFFFFF),
            ColorSchemeChoice("Midnight", true, 0xFF8B7CF6, 0xFF0B0B10, 0xFF14141C),
            ColorSchemeChoice("High Contrast", true, 0xFFFFD400, 0xFF000000, 0xFF000000),
        )
        fun named(name: String?) = builtIn.firstOrNull { it.name == name } ?: custom(name)

        /** The user's own scheme, saved as "custom:dark:#RRGGBB:#RRGGBB" (light or dark, accent, background). */
        fun custom(code: String?): ColorSchemeChoice? {
            val p = code?.split(':') ?: return null
            if (p.size != 4 || p[0] != "custom" || p[1] !in setOf("dark", "light")) return null
            val accent = rgb(p[2]) ?: return null
            val background = rgb(p[3]) ?: return null
            val dark = p[1] == "dark"
            // Panels: a little lighter than a dark background, white on a light one.
            val surface = if (dark) mix(background, 0xFFFFFFFF, 0.06) else 0xFFFFFFFF
            return ColorSchemeChoice(code, dark, accent, background, surface)
        }

        fun customCode(dark: Boolean, accent: String, background: String) =
            "custom:${if (dark) "dark" else "light"}:${accent.uppercase()}:${background.uppercase()}"

        /** "#RRGGBB" → 0xFFRRGGBB, or null. */
        fun rgb(hex: String): Long? {
            val h = hex.trim().removePrefix("#")
            if (h.length != 6) return null
            return h.toLongOrNull(16)?.let { 0xFF000000 or it }
        }

        private fun mix(a: Long, b: Long, t: Double): Long {
            fun ch(c: Long, s: Int) = (c shr s) and 0xFF
            fun m(s: Int) = Math.round(ch(a, s) * (1 - t) + ch(b, s) * t)
            return 0xFF000000 or (m(16) shl 16) or (m(8) shl 8) or m(0)
        }

        /** WCAG contrast ratio (1 to 21); 3 or more keeps the accent readable on the background. */
        fun contrast(a: Long, b: Long): Double {
            fun lum(c: Long): Double {
                fun ch(s: Int): Double { val v = ((c shr s) and 0xFF) / 255.0; return if (v <= 0.03928) v / 12.92 else Math.pow((v + 0.055) / 1.055, 2.4) }
                return 0.2126 * ch(16) + 0.7152 * ch(8) + 0.0722 * ch(0)
            }
            val l1 = lum(a); val l2 = lum(b)
            return (maxOf(l1, l2) + 0.05) / (minOf(l1, l2) + 0.05)
        }
    }
}

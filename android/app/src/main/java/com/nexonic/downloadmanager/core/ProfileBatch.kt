package com.nexonic.downloadmanager.core

import org.json.JSONArray
import org.json.JSONObject

/** One video found on a profile, channel, playlist or page. */
data class BatchEntry(val url: String, val title: String, val duration: Double? = null, val date: Long? = null,
                      val kind: String = "video", val available: Boolean = true)

/** Which found videos to keep. Values a site does not report (a date, a length) never exclude a video. */
data class BatchFilter(val from: Long? = null, val to: Long? = null, val keywords: String = "", val minSeconds: Int? = null,
                       val maxSeconds: Int? = null, val kind: String = "all", val max: Int = ProfileBatch.DEFAULT_MAX)

data class BatchCounts(val discovered: Int, val queued: Int, val duplicates: Int, val filtered: Int, val inaccessible: Int) {
    val text: String get() = Translations.t("%d found · %d added · %d already downloaded · %d left out by filters · %d not accessible")
        .format(discovered, queued, duplicates, filtered, inaccessible)
}

/** A profile checked again for new videos (while the app runs). */
data class SavedProfile(val id: String, val url: String, val title: String, val quality: String, val filter: BatchFilter,
                        val checkHours: Int, val lastChecked: Long = 0, val lastResult: String = "", val seen: Set<String> = emptySet()) {
    fun due(now: Long = System.currentTimeMillis()) = checkHours > 0 && now - lastChecked >= checkHours * 3_600_000L
}

/**
 * Profile Batch: every video the video engine can list at one link (it follows the site's pages), read without any login,
 * cookies or keys. Videos behind a login are counted as not accessible and never downloaded.
 */
object ProfileBatch {
    const val DEFAULT_MAX = 500
    const val LIMIT = 5000

    data class Quality(val key: String, val title: String, val selector: String, val ext: String, val audio: Boolean = false)

    val qualities = listOf(
        Quality("best", "Best", "bv*[ext=mp4]+ba[ext=m4a]/bv*+ba/b", "mp4"),
        Quality("8k", "8K", sel(4320), "mp4"), Quality("4k", "4K", sel(2160), "mp4"), Quality("1080", "1080p", sel(1080), "mp4"),
        Quality("720", "720p", sel(720), "mp4"), Quality("480", "480p", sel(480), "mp4"),
        Quality("original", "Original (no conversion)", "bv*+ba/b", "mkv"),
        Quality("audio", "Sound only", "ba[ext=m4a]/ba/b", "m4a", audio = true),
    )

    private fun sel(h: Int) = "bv*[height<=$h][ext=mp4]+ba[ext=m4a]/bv*[height<=$h]+ba/b[height<=$h]/b"

    fun quality(key: String) = qualities.firstOrNull { it.key == key } ?: qualities[0]

    const val FIELDS = "%(.{url,webpage_url,original_url,title,fulltitle,duration,timestamp,upload_date,release_timestamp,live_status,was_live,availability,ie_key,id,playlist_title,playlist_uploader,uploader,channel})j"

    /** Options for listing (no login, no cookies); start continues an interrupted scan. */
    fun options(start: Int, max: Int): List<Pair<String, String?>> = listOf(
        "--flat-playlist" to null, "--ignore-errors" to null, "--extractor-retries" to "3", "--retry-sleep" to "extractor:exp=2:30",
        "--print" to FIELDS, "--playlist-start" to start.toString(), "--playlist-end" to (start + max.coerceIn(1, LIMIT) - 1).toString(),
        "--socket-timeout" to "30",
    )

    /** Messages from Nexonic's profile listers start with this, so the app shows them as they are. */
    const val MARKER = "Nexonic:"

    /** What a site's lister said about the list itself (warnings that start with [MARKER]). */
    fun note(errors: String?): String = errors.orEmpty().lines().map { it.trim() }
        .filter { it.startsWith("WARNING:") && MARKER in it }.map { it.substringAfter(MARKER).trim() }.distinct().joinToString(" ")

    fun parseLine(line: String): Pair<BatchEntry, String?>? {
        val t = line.trim()
        if (!t.startsWith("{")) return null
        val o = runCatching { JSONObject(t) }.getOrNull() ?: return null
        fun s(k: String) = o.optString(k, "").takeIf { it.isNotEmpty() && it != "null" }
        fun n(k: String) = if (o.has(k) && !o.isNull(k)) o.optDouble(k).takeIf { !it.isNaN() } else null
        val id = s("id") ?: ""
        var url = s("webpage_url") ?: s("url") ?: s("original_url")
        if ((url == null || !url.startsWith("http", true)) && id.isNotEmpty() && (s("ie_key") ?: "").startsWith("Youtube")) url = "https://www.youtube.com/watch?v=$id"
        if (url == null || !url.startsWith("http", true)) return null
        val date = (n("timestamp") ?: n("release_timestamp"))?.let { (it * 1000).toLong() }
            ?: s("upload_date")?.takeIf { it.length == 8 }?.let { d ->
                runCatching { java.text.SimpleDateFormat("yyyyMMdd", java.util.Locale.US).apply { timeZone = java.util.TimeZone.getTimeZone("UTC") }.parse(d)?.time }.getOrNull()
            }
        val live = s("live_status") in setOf("is_live", "was_live", "is_upcoming") || o.optBoolean("was_live", false)
        val kind = if (live) "live" else if (url.contains("/shorts/", true) || url.contains("/reel/", true)) "shorts" else "video"
        val title = s("title") ?: s("fulltitle") ?: url
        val available = s("availability") !in setOf("private", "premium_only", "subscriber_only", "needs_auth") &&
            !title.startsWith("[Private video]") && !title.startsWith("[Deleted video]")
        return BatchEntry(url, title, n("duration"), date, kind, available) to (s("playlist_title") ?: s("playlist_uploader") ?: s("channel") ?: s("uploader"))
    }

    fun key(url: String): String {
        val base = url.substringBefore('#').trim()
        return runCatching { val u = java.net.URI(base); java.net.URI(u.scheme?.lowercase(), u.authority?.lowercase(), u.path, u.query, null).toString() }.getOrDefault(base)
    }

    private fun day(t: Long) = t / 86_400_000L

    fun matches(e: BatchEntry, f: BatchFilter): Boolean {
        e.date?.let { d -> if (f.from != null && day(d) < day(f.from) || f.to != null && day(d) > day(f.to)) return false }
        e.duration?.let { s -> if (f.minSeconds != null && s < f.minSeconds || f.maxSeconds != null && s > f.maxSeconds) return false }
        if (f.kind == "videos" && e.kind != "video" || f.kind in setOf("shorts", "live") && e.kind != f.kind) return false
        val words = f.keywords.split(' ').map { it.trim() }.filter { it.isNotEmpty() }
        val include = words.filter { !it.startsWith("!") }
        val exclude = words.filter { it.startsWith("!") && it.length > 1 }.map { it.substring(1) }
        if (include.isNotEmpty() && include.none { e.title.contains(it, true) }) return false
        return exclude.none { e.title.contains(it, true) }
    }

    fun choose(found: List<BatchEntry>, filter: BatchFilter, alreadyDownloaded: (String) -> Boolean, seen: Set<String>? = null): Pair<List<BatchEntry>, BatchCounts> {
        var discovered = 0; var duplicates = 0; var filtered = 0; var inaccessible = 0
        val keys = HashSet<String>(); val take = mutableListOf<BatchEntry>()
        for (e in found) {
            if (!keys.add(key(e.url))) continue
            discovered++
            if (!e.available) { inaccessible++; continue }
            if (alreadyDownloaded(e.url) || seen?.contains(key(e.url)) == true) { duplicates++; continue }
            if (!matches(e, filter) || take.size >= filter.max.coerceIn(1, LIMIT)) { filtered++; continue }
            take += e
        }
        return take to BatchCounts(discovered, take.size, duplicates, filtered, inaccessible)
    }

    fun export(entries: List<BatchEntry>, format: String): String {
        val df = java.text.SimpleDateFormat("yyyy-MM-dd", java.util.Locale.US).apply { timeZone = java.util.TimeZone.getTimeZone("UTC") }
        return when (format) {
            "csv" -> {
                fun c(v: String) = if (v.any { it in ",\"\n\r" }) "\"" + v.replace("\"", "\"\"") + "\"" else v
                "url,title,date,duration_seconds,type,accessible\r\n" + entries.joinToString("") {
                    listOf(c(it.url), c(it.title), it.date?.let { d -> df.format(java.util.Date(d)) } ?: "", it.duration?.toLong()?.toString() ?: "",
                        it.kind, if (it.available) "yes" else "no").joinToString(",") + "\r\n"
                }
            }
            "json" -> JSONArray(entries.map {
                JSONObject().put("url", it.url).put("title", it.title).put("date", it.date?.let { d -> df.format(java.util.Date(d)) } ?: JSONObject.NULL)
                    .put("duration_seconds", it.duration ?: JSONObject.NULL).put("type", it.kind).put("accessible", it.available)
            }).toString(2)
            else -> entries.joinToString("\n") { it.url } + if (entries.isEmpty()) "" else "\n"
        }
    }

    fun entryJson(e: BatchEntry): JSONObject = JSONObject().put("url", e.url).put("title", e.title).put("duration", e.duration ?: JSONObject.NULL)
        .put("date", e.date ?: JSONObject.NULL).put("kind", e.kind).put("available", e.available)

    fun entryFrom(o: JSONObject) = BatchEntry(o.getString("url"), o.optString("title"), if (o.isNull("duration")) null else o.optDouble("duration"),
        if (o.isNull("date")) null else o.optLong("date"), o.optString("kind", "video"), o.optBoolean("available", true))

    fun profilesToJson(list: List<SavedProfile>): String = JSONArray(list.map { p ->
        JSONObject().put("id", p.id).put("url", p.url).put("title", p.title).put("quality", p.quality).put("checkHours", p.checkHours)
            .put("lastChecked", p.lastChecked).put("lastResult", p.lastResult).put("seen", JSONArray(p.seen.toList()))
            .put("filter", JSONObject().put("keywords", p.filter.keywords).put("kind", p.filter.kind).put("max", p.filter.max)
                .put("minSeconds", p.filter.minSeconds ?: JSONObject.NULL).put("maxSeconds", p.filter.maxSeconds ?: JSONObject.NULL))
    }).toString()

    fun profilesFrom(json: String?): List<SavedProfile> = runCatching {
        val a = JSONArray(json ?: "[]")
        (0 until a.length()).map { i ->
            val o = a.getJSONObject(i); val f = o.optJSONObject("filter") ?: JSONObject(); val seen = o.optJSONArray("seen") ?: JSONArray()
            SavedProfile(o.getString("id"), o.getString("url"), o.optString("title"), o.optString("quality", "best"),
                BatchFilter(keywords = f.optString("keywords"), kind = f.optString("kind", "all"), max = f.optInt("max", DEFAULT_MAX),
                    minSeconds = if (f.isNull("minSeconds")) null else f.optInt("minSeconds"), maxSeconds = if (f.isNull("maxSeconds")) null else f.optInt("maxSeconds")),
                o.optInt("checkHours"), o.optLong("lastChecked"), o.optString("lastResult"), (0 until seen.length()).map { seen.getString(it) }.toSet())
        }
    }.getOrDefault(emptyList())
}

/**
 * Instagram shows visitors who are not signed in the latest posts of a profile, but only through its own page scripts.
 * The app opens the profile page in a hidden web view, like a visitor's browser, runs [SCRIPT] and reads the video links
 * the page shows. Nothing else of Instagram's is called.
 */
object InstagramPage {
    private val reserved = setOf("p", "reel", "reels", "tv", "stories", "explore", "accounts", "direct", "about", "legal",
        "developer", "api", "graphql", "web", "challenge", "emails", "session", "privacy", "terms")
    private val profile = Regex("""^https?://(?:www\.)?instagram\.com/([A-Za-z0-9._]{1,30})/?(?:reels/?|videos/?)?(?:[?#].*)?$""", RegexOption.IGNORE_CASE)

    /** The user name of an Instagram profile link (instagram.com/name, /name/reels), or null. */
    fun user(link: String): String? = profile.find(link.trim())?.groupValues?.get(1)?.takeIf { it.lowercase() !in reserved }

    /** Run in the loaded page: {"posts": links to posts shown, "videos": [{"u": link, "t": picture text}]}. */
    val SCRIPT = """
(() => {
  const all = [...document.querySelectorAll('a[href*="/reel/"], a[href*="/p/"]')], seen = new Set(), videos = [];
  for (const a of all) {
    const link = new URL(a.getAttribute('href'), location.href);
    const m = link.pathname.match(/\/(p|reel)\/([A-Za-z0-9_-]+)/);
    if (!m) continue;
    const alt = (a.querySelector('img') || {}).alt || '';
    const video = m[1] === 'reel' || /^Video by /.test(alt) || !!a.querySelector('svg[aria-label="Clip"], svg[aria-label="Video"], svg[aria-label="Reel"]');
    const u = 'https://www.instagram.com/' + m[1] + '/' + m[2] + '/';
    if (video && !seen.has(u)) { seen.add(u); videos.push({ u, t: alt }); }
  }
  return JSON.stringify({ posts: all.length, videos });
})()
""".trimIndent()

    /** What [SCRIPT] returned (the web view hands it back JSON-quoted): posts shown and the videos among them. */
    fun read(raw: String?): Pair<Int, List<BatchEntry>> {
        var json = raw?.trim().orEmpty()
        if (json.startsWith("\"")) json = runCatching { org.json.JSONTokener(json).nextValue() as? String }.getOrNull().orEmpty()
        val o = runCatching { JSONObject(json) }.getOrNull() ?: return 0 to emptyList()
        val dates = java.text.SimpleDateFormat("MMMM d, yyyy", java.util.Locale.US)
        val videos = mutableListOf<BatchEntry>()
        val a = o.optJSONArray("videos") ?: JSONArray()
        for (i in 0 until a.length()) {
            val v = a.optJSONObject(i) ?: continue
            val url = v.optString("u")
            if (!url.startsWith("https://www.instagram.com/") && !url.startsWith("https://www.facebook.com/")) continue
            val text = v.optString("t")
            val date = Regex(""" on ([A-Z][a-z]+ \d{1,2}, \d{4})""").find(text)?.let { m -> runCatching { dates.parse(m.groupValues[1])?.time }.getOrNull() }
            var title = text.split(Regex("""\s+""")).filter { it.isNotEmpty() }.joinToString(" ")
            if (title.length > 120) title = title.take(120) + "…"
            videos += BatchEntry(url, title.ifEmpty { url }, null, date, if ("/reel/" in url) "shorts" else "video")
        }
        return o.optInt("posts") to videos
    }

    fun note(posts: Int) = "Instagram shows visitors who are not signed in only the latest $posts posts of a profile; Nexonic took the videos among them."
    const val NOTHING = "Instagram did not show any posts of this profile to a visitor who is not signed in (it may be private or not exist)."
    fun noVideos(posts: Int) = "None of the latest $posts posts Instagram shows visitors of this profile is a video."
}

/**
 * Instagram and Facebook profiles in Profile Batch. Without signing in, the profile page shows visitors only its latest
 * posts. A user who wants all of them can sign in once on the site's own page inside Nexonic (optional; the session stays
 * on this device and can be removed any time): the profile page is then scrolled to the end, as the user would.
 */
data class SocialSite(val name: String, val loginUrl: String, val domain: String, val sessionCookie: String, val script: String)

object SocialPages {
    val instagram = SocialSite("Instagram", "https://www.instagram.com/accounts/login/", "instagram.com", "sessionid", InstagramPage.SCRIPT)
    val facebook = SocialSite("Facebook", "https://www.facebook.com/login/", "facebook.com", "c_user", "")
    private val fbProfile = Regex("""^https?://(?:www\.|m\.|web\.)?facebook\.com/(?:profile\.php\?id=(\d+)|(?!(?:watch|reel|videos|groups|events|share|story\.php|photo|permalink\.php|login|marketplace|gaming|people|pages)\b)([A-Za-z0-9.\-]{2,80}))""", RegexOption.IGNORE_CASE)

    /** The site, the profile's name and the pages that list its videos; null for other links. */
    fun find(link: String): Triple<SocialSite, String, List<String>>? {
        InstagramPage.user(link)?.let { return Triple(instagram, it, listOf("https://www.instagram.com/$it/")) }
        val m = fbProfile.find(link.trim()) ?: return null
        val id = m.groupValues[1]
        if (id.isNotEmpty()) return Triple(fbSite, id, listOf("https://www.facebook.com/profile.php?id=$id&sk=reels_tab", "https://www.facebook.com/profile.php?id=$id&sk=videos"))
        val name = m.groupValues[2].ifEmpty { return null }
        return Triple(fbSite, name, listOf("https://www.facebook.com/$name/reels/", "https://www.facebook.com/$name/videos/"))
    }

    /** Run in a loaded Facebook page: its reels and videos, as [InstagramPage.SCRIPT] reports them. */
    val FACEBOOK_SCRIPT = """
(() => {
  const seen = new Set(), videos = [];
  for (const a of document.querySelectorAll('a[href*="/reel/"], a[href*="/videos/"], a[href*="watch/?v="], a[href*="watch?v="]')) {
    const link = new URL(a.getAttribute('href'), location.href);
    let id = null, reel = false, m;
    if ((m = link.pathname.match(/\/reel\/(\d+)/))) { id = m[1]; reel = true; }
    else if ((m = link.pathname.match(/\/videos\/(?:[^/]+\/)?(\d{6,})/))) id = m[1];
    else if (/^\d{6,}${"$"}/.test(link.searchParams.get('v') || '')) id = link.searchParams.get('v');
    if (!id || seen.has(id)) continue;
    seen.add(id);
    const u = reel ? 'https://www.facebook.com/reel/' + id + '/' : 'https://www.facebook.com/watch/?v=' + id;
    videos.push({ u, t: (a.getAttribute('aria-label') || a.innerText || '').trim().slice(0, 200) });
  }
  return JSON.stringify({ posts: videos.length, videos });
})()
""".trimIndent()

    val fbSite get() = facebook.copy(script = FACEBOOK_SCRIPT)

    /** Scrolls the page one screen further down (signed-in scans load older posts this way). */
    const val SCROLL = "window.scrollBy(0, Math.max(window.innerHeight, 800)); 0"

    fun signedInNote(site: SocialSite, count: Int) = "Found $count videos by scrolling the whole profile with your ${site.name} sign-in."
    fun offerSignIn(site: SocialSite) = "${site.name} shows visitors who are not signed in only the latest posts. To find all of this profile's videos, choose \"Find all (sign in)\": you sign in once on ${site.name}'s own page inside Nexonic, and the session stays on this device."

    /** Adds what a page shows now to what was found before; true when anything new came. Stops at [max]. */
    fun merge(found: LinkedHashMap<String, BatchEntry>, now: List<BatchEntry>, max: Int): Boolean {
        var grew = false
        for (e in now) if (found.size < max && !found.containsKey(e.url)) { found[e.url] = e; grew = true }
        return grew
    }
}

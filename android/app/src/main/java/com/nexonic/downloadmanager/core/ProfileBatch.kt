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

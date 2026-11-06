package com.nexonic.downloadmanager.core

import org.json.JSONObject
import java.net.URI
import java.util.Locale

/** Playlists and channels: one link that stands for many videos (YouTube, Vimeo, SoundCloud, Bandcamp…). */
object Playlists {
    data class Entry(val url: String, val title: String, val duration: Double?)
    data class Result(val title: String, val entries: List<Entry>)

    /** The most videos read from one playlist or channel. */
    const val MAX_ENTRIES = 500

    private fun parts(url: String): Triple<String, String, String>? = runCatching {
        val u = URI(url.trim())
        if (u.scheme?.lowercase() !in setOf("http", "https") || u.host == null) return null
        Triple(u.host.lowercase(), (u.rawPath ?: "").trimEnd('/'), u.rawQuery ?: "")
    }.getOrNull()

    /** True for links that are a list of videos rather than one video. */
    fun isPlaylist(url: String): Boolean {
        val (host, path, query) = parts(url) ?: return false
        fun on(h: String) = host == h || host.endsWith(".$h")
        return when {
            on("youtube.com") -> when {
                path == "/playlist" && query.contains("list=") -> true
                path == "/watch" -> false // one video, even when it is part of a list
                else -> path.startsWith("/@") || path.startsWith("/channel/") || path.startsWith("/c/") || path.startsWith("/user/")
            }
            on("vimeo.com") -> path.startsWith("/showcase/") || path.startsWith("/channels/") || path.contains("/albums/")
            on("soundcloud.com") -> path.contains("/sets/")
            on("bandcamp.com") -> path.startsWith("/album/")
            on("dailymotion.com") -> path.startsWith("/playlist/")
            else -> false
        }
    }

    /** A channel's home page lists its tabs; its Videos tab lists the videos. */
    fun normalize(url: String): String {
        val t = url.trim()
        val (host, path, _) = parts(t) ?: return t
        val youTube = host == "youtube.com" || host.endsWith(".youtube.com")
        val p = path.split('/').filter { it.isNotEmpty() }
        val channelHome = p.size == 1 && p[0].startsWith("@") || p.size == 2 && p[0] in setOf("channel", "c", "user")
        return if (youTube && channelHome) "${URI(t).scheme}://${URI(t).host}$path/videos" else t
    }

    /** Reads the videos from yt-dlp's `--flat-playlist -J` output. Null when it isn't a list. */
    fun parse(json: String): Result? = runCatching {
        val o = JSONObject(json)
        val list = mutableListOf<Entry>()
        val seen = HashSet<String>()
        val entries = o.optJSONArray("entries")
        if (entries != null) for (i in 0 until entries.length()) {
            val e = entries.optJSONObject(i) ?: continue
            fun str(k: String) = if (e.has(k) && !e.isNull(k)) e.opt(k) as? String else null
            var url = str("url") ?: str("webpage_url")
            val id = str("id")
            if ((url == null || !url.startsWith("http")) && id != null && (str("ie_key") ?: "").startsWith("Youtube"))
                url = "https://www.youtube.com/watch?v=$id"
            if (url == null || !url.startsWith("http") || !seen.add(url)) continue
            val duration = (e.opt("duration") as? Number)?.toDouble()
            list += Entry(url, str("title") ?: url, duration)
        }
        Result(o.optString("title", ""), list)
    }.getOrNull()

    /** "1:05" or "1:02:03". */
    fun duration(seconds: Double): String {
        val t = seconds.toInt()
        return if (t >= 3600) String.format(Locale.ROOT, "%d:%02d:%02d", t / 3600, t / 60 % 60, t % 60)
        else String.format(Locale.ROOT, "%d:%02d", t / 60, t % 60)
    }
}

/** Subtitles kept inside saved videos (opt-in): the phone's language and English. A missing one never fails the video. */
object VideoSubtitles {
    fun languages(locale: Locale = Locale.getDefault()): String {
        val l = locale.language.lowercase()
        return if (l.isEmpty() || l == "en") "en.*" else "$l.*,en.*"
    }

    fun arguments(locale: Locale = Locale.getDefault()): List<String> =
        listOf("--write-subs", "--sub-langs", languages(locale), "--embed-subs", "--ignore-errors")
}

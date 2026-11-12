package com.nexonic.downloadmanager.video

import android.content.Context
import android.util.Log
import android.webkit.CookieManager
import com.nexonic.downloadmanager.core.Job
import com.nexonic.downloadmanager.core.StreamFormat
import com.nexonic.downloadmanager.core.VideoChoice
import com.nexonic.downloadmanager.core.VideoPages
import com.nexonic.downloadmanager.core.Playlists
import com.nexonic.downloadmanager.core.VideoSubtitles
import com.yausername.youtubedl_android.YoutubeDL
import com.yausername.youtubedl_android.YoutubeDLRequest
import java.io.File
import java.util.concurrent.atomic.AtomicBoolean

/** What the video engine found on a page. */
data class VideoPage(val title: String?, val choices: List<VideoChoice>, val durationSeconds: Int)

class VideoException(message: String) : Exception(message)

/**
 * The video engine (yt-dlp with ffmpeg, the same engine as the Mac and Windows apps). It reads video pages from
 * YouTube, Facebook, Instagram, TikTok and 1,000+ other sites and saves the video itself, never the web page.
 */
object VideoEngine {
    private val lock = Any()
    @Volatile private var ready = false
    private lateinit var app: Context

    /** Unpacks the engine on first use (a few seconds, once) and keeps yt-dlp current, at most once a day. */
    fun init(context: Context) {
        app = context.applicationContext
        Thread({ runCatching { ensure() }; runCatching { updateIfDue() } }, "nexonic-video-init").start()
    }

    private fun ensure() {
        if (ready) return
        synchronized(lock) {
            if (ready) return
            try {
                YoutubeDL.getInstance().init(app)
                // Versions up to 0.27 unpacked a large FFmpeg here; the app now runs its own from the library folder.
                File(app.noBackupFilesDir, "youtubedl-android/packages/ffmpeg").takeIf { it.exists() }?.deleteRecursively()
            } catch (e: Throwable) {
                Log.e(TAG, "video engine start failed", e)
                throw VideoException(VideoPages.engineError(e.javaClass.simpleName, e.message))
            }
            ready = true
        }
    }

    private fun updateIfDue() {
        val prefs = app.getSharedPreferences("video-engine", Context.MODE_PRIVATE)
        val last = prefs.getLong("updated", 0)
        if (System.currentTimeMillis() - last < 24L * 3600 * 1000) return
        YoutubeDL.getInstance().updateYoutubeDL(app, YoutubeDL.UpdateChannel.STABLE)
        prefs.edit().putLong("updated", System.currentTimeMillis()).apply()
    }

    fun version(): String? = runCatching { YoutubeDL.getInstance().versionName(app) ?: YoutubeDL.getInstance().version(app) }.getOrNull()

    /** Checks for a newer yt-dlp now (Settings). */
    fun updateNow(): String {
        ensure()
        val status = YoutubeDL.getInstance().updateYoutubeDL(app, YoutubeDL.UpdateChannel.STABLE)
        app.getSharedPreferences("video-engine", Context.MODE_PRIVATE).edit().putLong("updated", System.currentTimeMillis()).apply()
        return status?.name ?: "DONE"
    }

    /**
     * Cookies of the built-in browser for this site, as a cookies file for yt-dlp. Sites like Instagram and Facebook
     * often show videos only to signed-in people: signing in once in the Browser tab is then enough.
     */
    private fun cookiesFile(url: String): File? = runCatching {
        val host = java.net.URI(url).host ?: return null
        val header = CookieManager.getInstance().getCookie(url)?.takeIf { it.isNotBlank() } ?: return null
        val domain = "." + host.lowercase().removePrefix("www.").removePrefix("m.")
        val lines = VideoPages.netscapeCookies(domain, header)
        if (lines.isEmpty()) return null
        File(app.cacheDir, "video-cookies.txt").apply {
            writeText("# Netscape HTTP Cookie File\n" + lines.joinToString("\n") + "\n")
            setReadable(false, false); setReadable(true, true)
        }
    }.getOrNull()

    private fun base(url: String, userAgent: String?, referer: String?) = YoutubeDLRequest(url).apply {
        addOption("--no-playlist")
        com.nexonic.downloadmanager.data.Vpn.sourceAddress()?.let { addOption("--source-address", it) }
        cookiesFile(url)?.let { addOption("--cookies", it.absolutePath) }
        addOption("--no-mtime")
        addOption("--socket-timeout", "30")
        userAgent?.let { addOption("--user-agent", it) }
        referer?.let { addOption("--referer", it) }
    }

    /** Reads the page: title and quality choices. Throws [VideoException] with a readable message. */
    fun read(url: String, userAgent: String? = null, referer: String? = null): VideoPage {
        ensure()
        val info = try {
            YoutubeDL.getInstance().getInfo(base(resolve(url), userAgent, referer))
        } catch (e: Exception) {
            Log.w(TAG, "read failed", e)
            throw VideoException(VideoPages.friendlyError(e.message))
        } catch (e: Throwable) {
            Log.e(TAG, "read failed", e)
            throw VideoException(VideoPages.engineError(e.javaClass.simpleName, e.message))
        }
        val formats = info.formats.orEmpty().map { f ->
            StreamFormat(
                height = f.height,
                // Some sites (Instagram, Facebook) leave the codecs out: a format with a height is a video then.
                hasVideo = if (f.vcodec == null) f.height > 0 else f.vcodec != "none",
                hasAudio = if (f.acodec == null) f.vcodec == null else f.acodec != "none",
                ext = f.ext,
                size = f.fileSize.takeIf { it > 0 } ?: f.fileSizeApproximate.takeIf { it > 0 },
            )
        }
        return VideoPage(info.title, VideoPages.choices(formats), info.duration)
    }

    /**
     * Downloads [choice] into [dir] (an empty folder of its own). Calls [progress] with 0–100. Returns the finished file.
     * yt-dlp keeps its .part files, so a paused download continues where it stopped.
     */
    fun download(id: String, url: String, choice: VideoChoice, dir: File, rateKB: Long, userAgent: String?, referer: String?,
                 subtitles: Boolean = false, audioFormat: String = "original", progress: (Float, Long, Long?, Long?) -> Unit): File {
        ensure()
        dir.mkdirs()
        val req = base(resolve(url), userAgent, referer).apply {
            addOption("-f", choice.selector)
            // Sites whose videos all have the same title ("Video by name"): named after the caption, with the video's id
            // so two videos never share a name (the engine would skip the second as "already downloaded").
            val captioned = runCatching { java.net.URI(url).host?.lowercase().orEmpty() }.getOrDefault("").let { h ->
                listOf("instagram.com", "facebook.com", "fb.watch", "x.com", "twitter.com", "snapchat.com", "threads.net", "threads.com").any { h == it || h.endsWith(".$it") }
            }
            if (captioned) {
                addOption("--parse-metadata", "%(description,title)s:(?s)(?P<title>.+)")
                addOption("-o", File(dir, "%(title).100B [%(id)s].%(ext)s").absolutePath)
            } else addOption("-o", File(dir, "%(title).100B.%(ext)s").absolutePath)
            addOption("--concurrent-fragments", "4")
            if (choice.audioOnly) addOption("--no-keep-video") else addOption("--merge-output-format", "mp4")
            // Title, artist, date, description and chapters stay inside the saved file (as on the desktop apps).
            addOption("--embed-metadata")
            addOption("--embed-chapters")
            if (!choice.audioOnly && subtitles) VideoSubtitles.arguments().forEach { addOption(it) }
            // Sound only: convert to MP3, Opus or FLAC when chosen in Settings (the bundled FFmpeg does it).
            if (choice.audioOnly && audioFormat in setOf("mp3", "opus", "flac")) {
                addOption("-x")
                addOption("--audio-format", audioFormat)
                if (audioFormat == "mp3") addOption("--audio-quality", "0")
            }
            if (rateKB > 0) addOption("--limit-rate", "${rateKB}K")
        }
        try {
            YoutubeDL.getInstance().execute(req, id, false) { p, eta, line ->
                if (p >= 0) { val (total, speed) = VideoPages.parseProgress(line); progress(p, eta, total, speed) }
            }
        } catch (e: YoutubeDL.CanceledException) {
            throw e
        } catch (e: Exception) {
            Log.w(TAG, "download failed", e)
            throw VideoException(VideoPages.friendlyError(e.message))
        } catch (e: Throwable) {
            Log.e(TAG, "download failed", e)
            throw VideoException(VideoPages.engineError(e.javaClass.simpleName, e.message))
        }
        return dir.listFiles()?.filter { it.isFile && !it.name.endsWith(".part") && !it.name.endsWith(".ytdl") && it.length() > 0 }
            ?.maxByOrNull { it.length() }
            ?: throw VideoException("The video could not be saved.")
    }

    /**
     * Lists the videos of a playlist or channel without downloading them (up to [Playlists.MAX_ENTRIES], two minutes at
     * most). Throws [VideoException] with a readable message.
     */
    fun readPlaylist(url: String): Playlists.Result {
        ensure()
        val target = Playlists.normalize(url)
        val req = YoutubeDLRequest(target).apply {
            com.nexonic.downloadmanager.data.Vpn.sourceAddress()?.let { addOption("--source-address", it) }
            addOption("--flat-playlist")
            addOption("-J")
            addOption("--playlist-end", Playlists.MAX_ENTRIES.toString())
            addOption("--socket-timeout", "30")
            cookiesFile(target)?.let { addOption("--cookies", it.absolutePath) }
        }
        val pid = "playlist-" + System.nanoTime()
        val timer = Thread({
            try { Thread.sleep(120_000); cancel(pid) } catch (e: InterruptedException) { }
        }, "nexonic-playlist-timer").apply { isDaemon = true; start() }
        val out = try {
            YoutubeDL.getInstance().execute(req, pid).out
        } catch (e: Exception) {
            Log.w(TAG, "playlist read failed", e)
            throw VideoException(VideoPages.friendlyError(e.message))
        } catch (e: Throwable) {
            Log.e(TAG, "playlist read failed", e)
            throw VideoException(VideoPages.engineError(e.javaClass.simpleName, e.message))
        } finally {
            timer.interrupt()
        }
        val r = Playlists.parse(out) ?: throw VideoException("The list of videos could not be read.")
        if (r.entries.isEmpty()) throw VideoException("No videos were found at this link.")
        return r
    }

    /**
     * Profile Batch: lists the videos at [url] without any login or cookies (the site's pages are followed by the engine),
     * from video number [start]. [line] gets each printed entry as it comes. Throws [VideoException] with a readable reason.
     */
    fun discover(url: String, start: Int, max: Int, stopId: String, line: (String) -> Unit): String {
        ensure()
        val req = YoutubeDLRequest(Playlists.normalize(url)).apply {
            com.nexonic.downloadmanager.data.Vpn.sourceAddress()?.let { addOption("--source-address", it) }
            com.nexonic.downloadmanager.core.ProfileBatch.options(start, max).forEach { (k, v) -> if (v == null) addOption(k) else addOption(k, v) }
            plugins()?.let { addOption("--plugin-dirs", it) }
        }
        lastNote = ""
        return try {
            val r = YoutubeDL.getInstance().execute(req, stopId, false) { _, _, l -> l.lineSequence().forEach(line) }
            lastNote = com.nexonic.downloadmanager.core.ProfileBatch.note(r.err)
            r.out
        } catch (e: YoutubeDL.CanceledException) {
            throw e
        } catch (e: Exception) {
            Log.w(TAG, "profile scan failed", e)
            throw VideoException(VideoPages.friendlyError(e.message))
        } catch (e: Throwable) {
            Log.e(TAG, "profile scan failed", e)
            throw VideoException(VideoPages.engineError(e.javaClass.simpleName, e.message))
        }
    }

    /** What the last profile scan's site said about the list (for example that visitors see only the latest posts). */
    @Volatile var lastNote = ""
        private set

    /**
     * The profile listers Nexonic adds to the video engine (engine/nexonic_profiles.py, shipped as an app asset): they only read what
     * a site shows visitors who are not signed in. Copied to the app's files when missing or changed.
     */
    private fun plugins(): String? = runCatching {
        val rel = "nexonic/yt_dlp_plugins/extractor/nexonic_profiles.py"
        val bytes = app.assets.open("nexonic_profiles.py").use { it.readBytes() }
        val root = java.io.File(app.filesDir, "engine-plugins")
        val file = java.io.File(root, rel)
        if (!file.exists() || !file.readBytes().contentEquals(bytes)) { file.parentFile?.mkdirs(); file.writeBytes(bytes) }
        root.absolutePath
    }.getOrNull()

    /** Short share links (facebook.com/share/…, fb.watch) point to the real video page: follow them first. */
    private fun resolve(url: String): String {
        if (!VideoPages.isShareLink(url)) return url
        var current = url
        repeat(5) {
            val conn = (java.net.URL(current).let { com.nexonic.downloadmanager.core.Net.opener?.invoke(it) ?: it.openConnection() } as java.net.HttpURLConnection).apply {
                instanceFollowRedirects = false
                connectTimeout = 15000; readTimeout = 15000
                setRequestProperty("User-Agent", DESKTOP_UA)
                CookieManager.getInstance().getCookie(current)?.let { setRequestProperty("Cookie", it) }
            }
            val next = runCatching { if (conn.responseCode in 300..399) conn.getHeaderField("Location") else null }.getOrNull()
            conn.disconnect()
            if (next.isNullOrBlank()) return current
            current = java.net.URL(java.net.URL(current), next).toString()
            if (VideoPages.isLoginPage(current)) return url
        }
        return current
    }

    private const val TAG = "NexonicVideo"
    private const val DESKTOP_UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36"

    fun cancel(id: String) { runCatching { YoutubeDL.getInstance().destroyProcessById(id) } }
}

/** Runs one video download on its own thread, like [com.nexonic.downloadmanager.core.Transfer] does for files. */
class VideoJob(
    private val id: Long,
    private val url: String,
    private val choice: VideoChoice,
    private val dir: File,
    private val rateKB: Long,
    private val userAgent: String?,
    private val referer: String?,
    /** Percent, seconds left, total bytes and bytes per second (when yt-dlp reports them). */
    private val onProgress: (Float, Long, Long?, Long?) -> Unit,
    private val onDone: (File) -> Unit,
    private val onFail: (String) -> Unit,
) : Job {
    private val stopped = AtomicBoolean(false)

    override fun start() {
        Thread({
            try {
                val file = VideoEngine.download(id.toString(), url, choice, dir, rateKB, userAgent, referer,
                    com.nexonic.downloadmanager.data.Downloads.settings.videoSubtitles,
                    com.nexonic.downloadmanager.data.Downloads.settings.audioFormat) { p, eta, total, speed ->
                    if (!stopped.get()) onProgress(p, eta, total, speed)
                }
                if (!stopped.get()) onDone(file)
            } catch (e: Throwable) {
                if (!stopped.get()) onFail(e.message ?: "The video could not be saved.")
            }
        }, "nexonic-video-$id").start()
    }

    override fun pause() {
        stopped.set(true)
        VideoEngine.cancel(id.toString())
    }
}

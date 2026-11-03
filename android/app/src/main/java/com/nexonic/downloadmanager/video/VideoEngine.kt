package com.nexonic.downloadmanager.video

import android.content.Context
import com.nexonic.downloadmanager.core.Job
import com.nexonic.downloadmanager.core.StreamFormat
import com.nexonic.downloadmanager.core.VideoChoice
import com.nexonic.downloadmanager.core.VideoPages
import com.yausername.ffmpeg.FFmpeg
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
            YoutubeDL.getInstance().init(app)
            FFmpeg.getInstance().init(app)
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

    private fun base(url: String, userAgent: String?, referer: String?) = YoutubeDLRequest(url).apply {
        addOption("--no-playlist")
        addOption("--no-mtime")
        addOption("--socket-timeout", "30")
        userAgent?.let { addOption("--user-agent", it) }
        referer?.let { addOption("--referer", it) }
    }

    /** Reads the page: title and quality choices. Throws [VideoException] with a readable message. */
    fun read(url: String, userAgent: String? = null, referer: String? = null): VideoPage {
        ensure()
        val info = try {
            YoutubeDL.getInstance().getInfo(base(url, userAgent, referer))
        } catch (e: Exception) {
            throw VideoException(VideoPages.friendlyError(e.message))
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
                 progress: (Float, Long) -> Unit): File {
        ensure()
        dir.mkdirs()
        val req = base(url, userAgent, referer).apply {
            addOption("-f", choice.selector)
            addOption("-o", File(dir, "%(title).100B.%(ext)s").absolutePath)
            addOption("--concurrent-fragments", "4")
            if (choice.audioOnly) addOption("--no-keep-video") else addOption("--merge-output-format", "mp4")
            if (rateKB > 0) addOption("--limit-rate", "${rateKB}K")
        }
        try {
            YoutubeDL.getInstance().execute(req, id, false) { p, eta, _ -> if (p >= 0) progress(p, eta) }
        } catch (e: YoutubeDL.CanceledException) {
            throw e
        } catch (e: Exception) {
            throw VideoException(VideoPages.friendlyError(e.message))
        }
        return dir.listFiles()?.filter { it.isFile && !it.name.endsWith(".part") && !it.name.endsWith(".ytdl") && it.length() > 0 }
            ?.maxByOrNull { it.length() }
            ?: throw VideoException("The video could not be saved.")
    }

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
    private val onProgress: (Float, Long) -> Unit,
    private val onDone: (File) -> Unit,
    private val onFail: (String) -> Unit,
) : Job {
    private val stopped = AtomicBoolean(false)

    override fun start() {
        Thread({
            try {
                val file = VideoEngine.download(id.toString(), url, choice, dir, rateKB, userAgent, referer) { p, eta ->
                    if (!stopped.get()) onProgress(p, eta)
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

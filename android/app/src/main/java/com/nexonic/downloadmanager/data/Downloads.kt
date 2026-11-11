package com.nexonic.downloadmanager.data

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.os.Handler
import android.os.Looper
import com.nexonic.downloadmanager.core.DownloadRequest
import com.nexonic.downloadmanager.core.FileNames
import com.nexonic.downloadmanager.core.Job
import com.nexonic.downloadmanager.core.VideoChoice
import com.nexonic.downloadmanager.core.VideoPages
import com.nexonic.downloadmanager.video.VideoJob
import com.nexonic.downloadmanager.video.VideoEngine
import com.nexonic.downloadmanager.core.Links
import com.nexonic.downloadmanager.core.Net
import com.nexonic.downloadmanager.core.SpeedLimiter
import com.nexonic.downloadmanager.core.SpeedSchedule
import com.nexonic.downloadmanager.core.Transfer
import com.nexonic.downloadmanager.core.tr
import com.nexonic.downloadmanager.core.TransferListener
import com.nexonic.downloadmanager.core.TransferState
import com.nexonic.downloadmanager.service.DownloadService
import com.nexonic.downloadmanager.service.Notifier
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.File
import java.util.concurrent.Executors

/** What the app needs to add a download (from the Add window, the browser or a shared link). */
data class NewDownload(
    val url: String,
    val fileName: String,
    val size: Long? = null,
    val resumable: Boolean = false,
    val referer: String? = null,
    val userAgent: String? = null,
    val secrets: Secrets = Secrets(),
    val mime: String? = null,
    val quality: String? = null,
    /** Set for video pages: the quality picked in the download window. */
    val video: VideoChoice? = null,
)

/**
 * The download list and the queue: at most [Settings.maxActive] downloads run at once, the rest wait their turn.
 * Downloads are written to the app's own folder and moved to the public Downloads folder when they finish.
 */
object Downloads {
    private lateinit var app: Context
    private lateinit var store: ListStore
    lateinit var secrets: SecretStore
        private set
    lateinit var settings: Settings
        private set
    private val io = Executors.newSingleThreadExecutor()
    private val main = Handler(Looper.getMainLooper())
    private val transfers = mutableMapOf<Long, Job>()
    private val mimes = mutableMapOf<Long, String?>()
    val limiter = SpeedLimiter()

    private val _items = MutableStateFlow<List<DownloadItem>>(emptyList())
    val items: StateFlow<List<DownloadItem>> = _items.asStateFlow()

    // Download limit: bytes counted since the last save, and downloads stopped by it (started again in the next period).
    private var cap = com.nexonic.downloadmanager.core.DataCap()
    private var capUnsaved = 0L
    private val heldByCap = mutableSetOf<Long>()
    private val _capReached = MutableStateFlow(false)
    val capReached: StateFlow<Boolean> = _capReached.asStateFlow()

    private val _waitingForWifi = MutableStateFlow(false)
    val waitingForWifi: StateFlow<Boolean> = _waitingForWifi.asStateFlow()

    fun init(context: Context) {
        if (::app.isInitialized) return
        app = context.applicationContext
        store = ListStore(app)
        secrets = SecretStore(app)
        settings = Settings(app)
        settings.applyFormats()
        applySpeedLimit()
        main.postDelayed(object : Runnable { override fun run() { applySpeedLimit(); main.postDelayed(this, 30_000) } }, 30_000)
        cap = settings.dataCap
        Vpn.init(app, settings.vpnOnly)
        feeds = com.nexonic.downloadmanager.core.Feeds.fromJson(settings.feeds)
        // RSS feeds that are due are checked every minute while the app runs.
        main.post(object : Runnable { override fun run() { checkFeeds(); checkProfiles(); main.postDelayed(this, 60_000) } })
        // Downloads that were running when the app stopped continue.
        _items.value = store.load().map { if (it.status == Status.DOWNLOADING) it.copy(status = Status.QUEUED, speed = 0) else it.copy(speed = 0) }
        if (_items.value.any { it.status == Status.QUEUED }) main.post { pump() }
        if (settings.removeMissingOnStart) Thread({ removeMissing() }, "nexonic-missing").start()
        // Waiting for Wi-Fi: start as soon as an unmetered network is back.
        runCatching {
            app.getSystemService(ConnectivityManager::class.java)?.registerDefaultNetworkCallback(object : ConnectivityManager.NetworkCallback() {
                override fun onCapabilitiesChanged(network: android.net.Network, caps: NetworkCapabilities) { main.post { networkChanged() } }
                override fun onLost(network: android.net.Network) { main.post { networkChanged() } }
            })
        }
    }

    private fun partFile(id: Long): File = File(app.getExternalFilesDir(null) ?: app.filesDir, "partial/$id.part")
    private fun videoDir(id: Long): File = File(app.getExternalFilesDir(null) ?: app.filesDir, "partial/video-$id")
    private fun torrentDir(id: Long): File = File(app.getExternalFilesDir(null) ?: app.filesDir, "partial/torrent-$id")
    private fun torrentState(id: Long): File = File(app.filesDir, "torrents/$id")

    /** Adds a torrent whose files were chosen in the Add Torrent window. */
    @Synchronized fun addTorrent(info: com.nexonic.downloadmanager.torrent.TorrentInfo, startNow: Boolean): Long {
        val key = info.source.ifEmpty { "torrent:" + info.infoHash }
        _items.value.firstOrNull { it.torrent?.infoHash == info.infoHash && it.status != Status.COMPLETE }?.let { existing ->
            if (startNow && existing.status !in listOf(Status.DOWNLOADING, Status.QUEUED)) resume(existing.id)
            return existing.id
        }
        val id = maxOf(System.currentTimeMillis(), (_items.value.maxOfOrNull { it.id } ?: 0) + 1)
        val item = DownloadItem(id = id, url = key, fileName = FileNames.sanitize(info.name), size = info.selectedSize, resumable = true,
            status = if (startNow) Status.QUEUED else Status.LATER, torrent = info)
        _items.value = listOf(item) + _items.value
        persist()
        if (startNow) pump()
        return id
    }

    private fun startTorrent(item: DownloadItem) {
        val id = item.id
        val info = item.torrent!!
        val job = com.nexonic.downloadmanager.torrent.TorrentJob(info, torrentState(id), torrentDir(id), settings.speedLimitKB,
            object : com.nexonic.downloadmanager.torrent.TorrentListener {
                override fun progress(received: Long, wanted: Long, downloadSpeed: Long, uploadSpeed: Long, peers: Int, seeds: Int, seedsTotal: Int, peersTotal: Int) {
                    update(id, save = false) {
                        if (it.status != Status.DOWNLOADING) it
                        else it.copy(written = received, size = wanted.takeIf { w -> w > 0 } ?: it.size, speed = downloadSpeed,
                            torrent = it.torrent?.copy(peers = peers, seeds = seeds, seedsTotal = seedsTotal, peersTotal = peersTotal, uploadSpeed = uploadSpeed))
                    }
                }

                override fun finished(folder: File, files: List<com.nexonic.downloadmanager.torrent.TorrentFileInfo>) {
                    synchronized(this@Downloads) { transfers.remove(id) }
                    try {
                        // Each file goes to Downloads (or the chosen folder), named after the torrent when it has several.
                        var first: Publisher.Saved? = null
                        for (f in files) {
                            val source = File(folder, f.path)
                            val base = f.path.substringAfterLast('/')
                            val name = if (files.size == 1) base else FileNames.sanitize(info.name + " - " + base)
                            val mime = android.webkit.MimeTypeMap.getSingleton().getMimeTypeFromExtension(base.substringAfterLast('.', "").lowercase())
                            val saved = Publisher.publish(app, source, name, mime, settings.categoryFolders, settings.saveTree, item.url)
                            if (first == null) first = saved
                        }
                        update(id) {
                            it.copy(status = Status.COMPLETE, written = info.selectedSize, size = info.selectedSize, speed = 0,
                                fileName = if (files.size == 1) first!!.name else it.fileName,
                                contentUri = first?.uri?.toString(), savedPath = first?.path, finishedAt = System.currentTimeMillis(), error = null)
                        }
                        folder.deleteRecursively(); torrentState(id).deleteRecursively()
                        get(id)?.let { Notifier.finished(app, it) }
                    } catch (e: Exception) {
                        update(id) { it.copy(status = Status.FAILED, speed = 0, error = "Could not save to Downloads: ${e.message}") }
                    }
                    main.post { pump() }
                }

                override fun failed(message: String) {
                    synchronized(this@Downloads) { transfers.remove(id) }
                    update(id) { it.copy(status = Status.FAILED, error = message, speed = 0) }
                    get(id)?.let { Notifier.failed(app, it) }
                    main.post { pump() }
                }
            })
        transfers[id] = job
        update(id) { it.copy(status = Status.DOWNLOADING, error = null) }
        job.start()
    }

    @Synchronized private fun update(id: Long, save: Boolean = true, change: (DownloadItem) -> DownloadItem) {
        var got = 0L
        _items.value = _items.value.map { if (it.id == id) change(it).also { n -> got = n.written - it.written } else it }
        if (save) persist()
        if (got > 0) countData(got)
    }

    /** Download limit: counts what arrived; at the limit, downloads stop until the next period. */
    private fun countData(bytes: Long) {
        val limit = settings.dataCapMB
        if (limit <= 0) return
        val now = System.currentTimeMillis()
        cap = cap.rolled(now, settings.dataCapHours).plus(bytes)
        capUnsaved += bytes
        if (capUnsaved > 4L shl 20) { settings.dataCap = cap; capUnsaved = 0 }
        if (!cap.reached(limit) || _capReached.value) return
        settings.dataCap = cap
        _capReached.value = true
        main.post {
            _items.value.filter { it.status == Status.DOWNLOADING || it.status == Status.QUEUED }.forEach { heldByCap.add(it.id); pause(it.id) }
            Notifier.message(app, "Download limit reached", "Downloads continue " +
                java.text.DateFormat.getDateTimeInstance(java.text.DateFormat.SHORT, java.text.DateFormat.SHORT).format(java.util.Date(cap.nextPeriod(settings.dataCapHours))))
            main.postDelayed({ releaseCap() }, (cap.nextPeriod(settings.dataCapHours) - System.currentTimeMillis()).coerceAtLeast(1000))
        }
    }

    /** The next period started (or the limit was raised or turned off): what the limit stopped starts again. */
    fun releaseCap() {
        if (!_capReached.value) return
        cap = cap.rolled(System.currentTimeMillis(), settings.dataCapHours)
        if (settings.dataCapMB > 0 && cap.reached(settings.dataCapMB)) {
            main.postDelayed({ releaseCap() }, (cap.nextPeriod(settings.dataCapHours) - System.currentTimeMillis()).coerceAtLeast(1000))
            return
        }
        _capReached.value = false
        val held = heldByCap.toList()
        heldByCap.clear()
        held.forEach { resume(it) }
        pump()
    }

    private fun persist() {
        val snapshot = _items.value
        io.execute { runCatching { store.save(snapshot) } }
    }

    fun get(id: Long): DownloadItem? = _items.value.firstOrNull { it.id == id }

    /** Adds a download; `startNow = false` keeps it for later (Start queue or the scheduler starts it). */
    @Synchronized fun add(d: NewDownload, startNow: Boolean): Long {
        val existing = _items.value.firstOrNull { it.url == d.url && it.status != Status.COMPLETE && it.status != Status.FAILED }
        if (existing != null) {
            // Never add a link that is already being downloaded: show the running one instead.
            if (startNow && existing.status !in listOf(Status.DOWNLOADING, Status.QUEUED)) resume(existing.id)
            return existing.id
        }
        val id = maxOf(System.currentTimeMillis(), (_items.value.maxOfOrNull { it.id } ?: 0) + 1)
        val name = FileNames.sanitize(d.fileName)
        if (!d.secrets.isEmpty) secrets.put(id, d.secrets)
        mimes[id] = d.mime
        val item = DownloadItem(
            id = id, url = d.url, fileName = name, size = d.size, resumable = d.resumable,
            referer = d.referer, userAgent = d.userAgent, quality = d.video?.label ?: d.quality,
            status = if (startNow) Status.QUEUED else Status.LATER,
            videoFormat = d.video?.selector, videoExt = d.video?.ext,
        )
        _items.value = listOf(item) + _items.value
        persist()
        if (startNow) pump()
        // Added before its page was read: find the video's title in the background and show it in the list.
        if (d.video != null && name == VideoPages.AUTO_NAME) Thread({
            runCatching { VideoEngine.read(d.url, d.userAgent, d.referer) }.getOrNull()?.title?.let { title ->
                update(id) { if (it.fileName == VideoPages.AUTO_NAME && it.status != Status.COMPLETE) it.copy(fileName = VideoPages.fileName(title, it.videoExt ?: "mp4")) else it }
            }
        }, "nexonic-video-title").start()
        return id
    }

    fun resume(id: Long) {
        update(id) { if (it.status == Status.COMPLETE || it.status == Status.DOWNLOADING) it else it.copy(status = Status.QUEUED, error = null) }
        if (settings.maxManualActive > 0) synchronized(this) { manual.add(id) }
        pump()
    }

    // ---- Profile Batch ----

    /** A scan's progress, kept so an interrupted scan continues after the videos it already found. */
    private fun scanFile(url: String) = File(app.filesDir, "profile-scans/" + Integer.toHexString(com.nexonic.downloadmanager.core.ProfileBatch.key(url).hashCode()) + ".json")

    /**
     * Lists the videos at [url] (no login, no cookies), continuing an interrupted scan unless [fresh]. Runs on the calling
     * (background) thread; [count] gets the number found so far. Returns the videos and the profile's name.
     */
    fun discover(url: String, max: Int, fresh: Boolean, stopId: String, count: (Int) -> Unit): Pair<List<com.nexonic.downloadmanager.core.BatchEntry>, String> {
        val pb = com.nexonic.downloadmanager.core.ProfileBatch
        val file = scanFile(url)
        val earlier = if (fresh) emptyList() else runCatching {
            val o = org.json.JSONObject(file.readText()); val a = o.getJSONArray("entries")
            (0 until a.length()).map { pb.entryFrom(a.getJSONObject(it)) } to o.optString("owner")
        }.getOrNull()?.let { it.first to it.second } ?: (emptyList<com.nexonic.downloadmanager.core.BatchEntry>() to "")
        val entries = earlier.first.toMutableList(); var owner = earlier.second
        fun save() = runCatching {
            file.parentFile?.mkdirs()
            file.writeText(org.json.JSONObject().put("owner", owner).put("entries", org.json.JSONArray(entries.map(pb::entryJson))).toString())
        }
        count(entries.size)
        if (entries.size < max) {
            var seenLines = 0
            val out = try {
                com.nexonic.downloadmanager.video.VideoEngine.discover(url, entries.size + 1, max - entries.size, stopId) { l ->
                    if (l.trimStart().startsWith("{")) { seenLines++; if (seenLines % 10 == 0) count(entries.size + seenLines) }
                }
            } catch (e: Exception) {
                if (e is com.yausername.youtubedl_android.YoutubeDL.CanceledException) save()
                throw e
            }
            for (l in out.lineSequence()) pb.parseLine(l)?.let { (e, o) -> entries += e; if (owner.isEmpty() && o != null) owner = o }
        }
        file.delete()
        if (entries.isEmpty()) throw java.io.IOException(tr("No videos were found at this link"))
        return entries to owner
    }

    fun alreadyDownloaded(url: String): Boolean {
        val k = com.nexonic.downloadmanager.core.ProfileBatch.key(url)
        return _items.value.any { com.nexonic.downloadmanager.core.ProfileBatch.key(it.url) == k }
    }

    /** Adds the chosen videos in [quality]; they start now unless [start] is off (then they wait under Download later). */
    fun queueBatch(take: List<com.nexonic.downloadmanager.core.BatchEntry>, quality: String, start: Boolean): Int {
        val q = com.nexonic.downloadmanager.core.ProfileBatch.quality(quality)
        val choice = VideoChoice(q.title, q.selector, q.ext, null, audioOnly = q.audio)
        for (e in take) add(NewDownload(url = e.url, fileName = (if (e.title.isBlank() || e.title == e.url) "Video" else e.title) + "." + q.ext, video = choice), start)
        return take.size
    }

    var profiles: List<com.nexonic.downloadmanager.core.SavedProfile>
        get() = com.nexonic.downloadmanager.core.ProfileBatch.profilesFrom(settings.savedProfiles)
        set(v) { settings.savedProfiles = com.nexonic.downloadmanager.core.ProfileBatch.profilesToJson(v) }
    @Volatile private var checkingProfiles = false

    /** Saved profiles that are due are listed again; their new videos are added and start. */
    fun checkProfiles() {
        if (!::app.isInitialized || checkingProfiles) return
        val due = profiles.filter { it.due() }
        if (due.isEmpty()) return
        checkingProfiles = true
        Thread({
            try {
                for (p in due) {
                    val result = runCatching { discover(p.url, p.filter.max, true, "profile-" + p.id) {} }
                    val now = System.currentTimeMillis()
                    val updated = result.fold({ (entries, owner) ->
                        val pb = com.nexonic.downloadmanager.core.ProfileBatch
                        val (take, counts) = pb.choose(entries, p.filter, ::alreadyDownloaded, p.seen)
                        main.post { queueBatch(take, p.quality, true) }
                        p.copy(title = p.title.ifEmpty { owner }, lastChecked = now, lastResult = counts.text, seen = p.seen + entries.map { pb.key(it.url) })
                    }, { p.copy(lastChecked = now, lastResult = it.message ?: "") })
                    profiles = profiles.map { if (it.id == p.id) updated else it }
                }
            } finally { checkingProfiles = false }
        }, "nexonic-profiles").start()
    }

    /** Downloads resumed by hand while Settings gives them their own slots beside the queue. */
    private val manual = mutableSetOf<Long>()

    /** True when a finished download's file was deleted or moved outside the app. */
    fun isMissing(item: DownloadItem): Boolean {
        if (item.status != Status.COMPLETE) return false
        item.contentUri?.let { uri ->
            return try {
                app.contentResolver.openFileDescriptor(android.net.Uri.parse(uri), "r")?.close()
                false
            } catch (e: java.io.FileNotFoundException) {
                true
            } catch (e: Exception) {
                false  // no permission or the storage provider is gone: not proof that the file was deleted
            }
        }
        return item.savedPath?.let { !File(it).exists() } ?: false
    }

    /** Removes finished downloads whose file is gone from the list (the files themselves are not touched). */
    fun removeMissing(): Int {
        val gone = _items.value.filter { isMissing(it) }.map { it.id }.toSet()
        if (gone.isEmpty()) return 0
        synchronized(this) { _items.value = _items.value.filterNot { it.id in gone } }
        store.save(_items.value)
        return gone.size
    }

    fun pause(id: Long) {
        val t = synchronized(this) { transfers.remove(id) }
        update(id) { if (it.status == Status.COMPLETE) it else it.copy(status = Status.PAUSED, speed = 0) }
        t?.pause()
        pump()
    }

    fun retry(id: Long) {
        update(id) { it.copy(status = Status.QUEUED, error = null) }
        pump()
    }

    /** A fresh link for a download whose link expired: it continues where it stopped. */
    fun refreshLink(id: Long, url: String, cookies: String?) {
        if (!Links.isWebLink(url)) return
        pause(id)
        if (!cookies.isNullOrEmpty()) secrets.put(id, secrets.get(id).copy(cookies = cookies))
        update(id) { it.copy(url = url) }
        resume(id)
    }

    fun setLogin(id: Long, user: String, password: String) {
        secrets.put(id, secrets.get(id).copy(user = user.ifEmpty { null }, password = password.ifEmpty { null }))
    }

    fun pauseAll() { _items.value.filter { it.isActive }.forEach { pause(it.id) } }

    fun startAll() {
        _items.value.filter { it.status in listOf(Status.PAUSED, Status.LATER, Status.FAILED) }.forEach { item ->
            update(item.id, save = false) { it.copy(status = Status.QUEUED, error = null) }
        }
        persist()
        pump()
    }

    /** The scheduler: everything kept for later starts now. */
    fun startLater() {
        _items.value.filter { it.status == Status.LATER }.forEach { item -> update(item.id, save = false) { it.copy(status = Status.QUEUED) } }
        persist()
        pump()
    }

    fun remove(id: Long, deleteFile: Boolean) {
        val t = synchronized(this) { transfers.remove(id) }
        t?.pause()
        val item = get(id) ?: return
        synchronized(this) { _items.value = _items.value.filterNot { it.id == id } }
        persist()
        secrets.remove(id)
        io.execute {
            Thread.sleep(300)
            partFile(id).delete()
            videoDir(id).deleteRecursively()
            torrentDir(id).deleteRecursively(); torrentState(id).deleteRecursively()
            if (deleteFile) item.contentUri?.let { Publisher.delete(app, it) }
        }
        pump()
    }

    fun clearFinished() {
        synchronized(this) { _items.value = _items.value.filterNot { it.status == Status.COMPLETE } }
        persist()
    }

    fun setSpeedLimit(kb: Long) {
        settings.speedLimitKB = kb
        applySpeedLimit()
    }

    /** The speed schedule's limit when one of its rules applies now, else the normal limit. */
    fun applySpeedLimit() {
        val scheduled = SpeedSchedule.parse(settings.speedSchedule).rules.let { SpeedSchedule.limitAt(it, java.time.LocalDateTime.now()) }
        limiter.bytesPerSecond = (scheduled?.toLong() ?: settings.speedLimitKB) * 1024
    }

    private val _waitingForVpn = MutableStateFlow(false)
    val waitingForVpn: StateFlow<Boolean> = _waitingForVpn.asStateFlow()

    /** VPN only: when the VPN disconnects, running downloads stop at once and wait for it (kill switch). */
    private fun networkChanged() {
        if (!Vpn.ready()) pauseForSystem()
        if (_waitingForWifi.value || _waitingForVpn.value || !Vpn.ready()) pump()
    }

    /** The torrent blocklist ranges from Settings (downloaded at most once a day); throws when it can't be read. */
    fun torrentBlocklist(): List<Pair<String, String>> =
        com.nexonic.downloadmanager.core.TorrentBlocklist.load(settings.torrentBlocklist, java.io.File(app.filesDir, "torrent-blocklist"))

    fun setVpnOnly(on: Boolean) {
        settings.vpnOnly = on
        Vpn.set(on)
        networkChanged()
        pump()
    }

    private fun onWifiOrNotNeeded(): Boolean {
        if (!settings.wifiOnly) return true
        val cm = app.getSystemService(ConnectivityManager::class.java) ?: return true
        val caps = cm.getNetworkCapabilities(cm.activeNetwork) ?: return false
        return caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_METERED)
    }

    /** Starts queued downloads while there is room. */
    @Synchronized fun pump() {
        if (!::app.isInitialized) return
        if (_capReached.value) { if (transfers.isEmpty()) DownloadService.stop(app); return }
        val queued = _items.value.filter { it.status == Status.QUEUED && it.id !in transfers }
        if (queued.isEmpty()) { if (transfers.isEmpty()) DownloadService.stop(app); return }
        if (!Vpn.ready()) {
            _waitingForVpn.value = true
            if (transfers.isEmpty()) DownloadService.stop(app)
            return
        }
        _waitingForVpn.value = false
        if (!onWifiOrNotNeeded()) {
            _waitingForWifi.value = true
            if (transfers.isEmpty()) DownloadService.stop(app)
            return
        }
        _waitingForWifi.value = false
        val byHand = synchronized(this) { manual.retainAll { id -> _items.value.any { it.id == id && it.status in setOf(Status.QUEUED, Status.DOWNLOADING) } }; manual.toSet() }
        var room = settings.maxActive - transfers.keys.count { it !in byHand }
        for (item in queued.filter { it.id !in byHand }.sortedBy { it.addedAt }) {
            if (room <= 0) break
            startTransfer(item)
            room--
        }
        // Resumed by hand: their own slots (Settings → Downloads), so they start without waiting for the queue.
        var free = settings.maxManualActive - transfers.keys.count { it in byHand }
        for (item in queued.filter { it.id in byHand }.sortedBy { it.addedAt }) {
            if (free <= 0) break
            startTransfer(item)
            free--
        }
        if (transfers.isNotEmpty()) DownloadService.start(app)
    }

    private fun startVideo(item: DownloadItem) {
        val id = item.id
        val choice = VideoChoice(item.quality ?: "", item.videoFormat!!, item.videoExt ?: "mp4", item.size, audioOnly = item.videoExt != "mp4")
        var lastBytes = item.written
        var lastTime = System.currentTimeMillis()
        val job = VideoJob(
            id, item.url, choice, videoDir(id), settings.speedLimitKB, item.userAgent, item.referer,
            onProgress = { percent, _, total, speed ->
                update(id, save = false) {
                    if (it.status != Status.DOWNLOADING) return@update it
                    // yt-dlp reports the size of the part it is downloading (video, then sound) and its speed.
                    val size = total?.takeIf { t -> t > 0 } ?: it.size
                    val written = if (size != null && size > 0) (size * percent / 100).toLong() else it.written
                    val now = System.currentTimeMillis()
                    val measured = if (now - lastTime >= 1000) ((written - lastBytes) * 1000 / (now - lastTime)).coerceAtLeast(0).also {
                        lastBytes = written; lastTime = now
                    } else it.speed
                    it.copy(written = written, size = size, speed = speed ?: measured)
                }
            },
            onDone = { file ->
                synchronized(this@Downloads) { transfers.remove(id) }
                val current = get(id)
                if (current != null) try {
                    val ext = file.extension.ifEmpty { current.videoExt ?: "mp4" }
                    val name = if (current.fileName == VideoPages.AUTO_NAME) FileNames.sanitize(file.name)
                        else current.fileName.substringBeforeLast('.', current.fileName) + "." + ext
                    val mime = android.webkit.MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext)
                    val length = file.length()
                    val saved = Publisher.publish(app, file, name, mime, settings.categoryFolders, settings.saveTree, current.url)
                    videoDir(id).deleteRecursively()
                    update(id) {
                        it.copy(status = Status.COMPLETE, written = length, size = length, speed = 0,
                            fileName = saved.name, contentUri = saved.uri.toString(), savedPath = saved.path,
                            finishedAt = System.currentTimeMillis(), error = null)
                    }
                    get(id)?.let { Notifier.finished(app, it) }
                } catch (e: Exception) {
                    update(id) { it.copy(status = Status.FAILED, speed = 0, error = "Could not save to Downloads: ${e.message}") }
                }
                main.post { pump() }
            },
            onFail = { message ->
                synchronized(this@Downloads) { transfers.remove(id) }
                update(id) { it.copy(status = Status.FAILED, error = message, speed = 0) }
                get(id)?.let { Notifier.failed(app, it) }
                main.post { pump() }
            },
        )
        transfers[id] = job
        update(id) { it.copy(status = Status.DOWNLOADING, error = null) }
        job.start()
    }

    // ---- RSS feeds (Downloads → RSS feeds) ----

    var feeds: MutableList<com.nexonic.downloadmanager.core.FeedSubscription> = mutableListOf()
        private set
    /** A torrent from a feed, to show in the Add Torrent window (one at a time). */
    val feedTorrent = MutableStateFlow<String?>(null)
    @Volatile private var checkingFeeds = false

    @Synchronized fun saveFeeds() { settings.feeds = com.nexonic.downloadmanager.core.Feeds.toJson(feeds) }

    /** Checks the feeds that are due (or all), adding each new matching file; torrents open in the Add Torrent window. */
    fun checkFeeds(all: Boolean = false, done: (() -> Unit)? = null) {
        if (!::app.isInitialized || checkingFeeds) return
        val due = synchronized(this) { feeds.filter { it.enabled && (all || com.nexonic.downloadmanager.core.Feeds.due(it)) } }
        if (due.isEmpty()) { done?.let { d -> main.post { d() } }; return }
        checkingFeeds = true
        Thread {
            try {
                for (f in due) {
                    try {
                        val url = java.net.URL(f.url)
                        if (!Links.isWebLink(f.url)) throw java.io.IOException("The feed address must start with http:// or https://")
                        val c = (Net.opener?.invoke(url) ?: url.openConnection()) as java.net.HttpURLConnection
                        val xml = try {
                            c.connectTimeout = 30_000; c.readTimeout = 60_000; c.setRequestProperty("User-Agent", "NexonicDownloadManager")
                            if (c.responseCode !in 200..299) throw java.io.IOException("HTTP ${c.responseCode}")
                            c.inputStream.use { it.readBytes() }.also { if (it.size > 16 * 1024 * 1024) throw java.io.IOException("The feed is too large.") }
                        } finally { c.disconnect() }
                        val items = synchronized(this) { com.nexonic.downloadmanager.core.Feeds.check(f, xml) }
                        for (i in items) {
                            if (i.link.startsWith("magnet:?", true) || i.link.substringBefore('?').endsWith(".torrent", true)) {
                                if (feedTorrent.value == null) feedTorrent.value = i.link
                                continue
                            }
                            val req = DownloadRequest(i.link)
                            val probe = runCatching { com.nexonic.downloadmanager.core.Http.probe(req) }.getOrNull()
                            add(NewDownload(url = i.link, fileName = probe?.fileName ?: FileNames.fromUrl(i.link), size = probe?.size ?: i.size,
                                resumable = probe?.resumable ?: false, mime = probe?.contentType), startNow = true)
                        }
                    } catch (e: Exception) {
                        synchronized(this) { f.lastChecked = System.currentTimeMillis(); f.lastResult = "Check failed: ${e.message}" }
                    }
                }
                saveFeeds()
            } finally {
                checkingFeeds = false
                done?.let { d -> main.post { d() } }
            }
        }.start()
    }

    // ---- Debrid (Settings → Debrid service): your own key, kept encrypted with the other secrets ----

    private const val DEBRID_KEY_ID = Long.MIN_VALUE
    var debridKey: String
        get() = secrets.get(DEBRID_KEY_ID).password.orEmpty()
        set(v) { if (v.isBlank()) secrets.remove(DEBRID_KEY_ID) else secrets.put(DEBRID_KEY_ID, Secrets(password = v.trim())) }
    private val debridService get() = runCatching { com.nexonic.downloadmanager.core.DebridService.valueOf(settings.debridService) }
        .getOrDefault(com.nexonic.downloadmanager.core.DebridService.NONE)

    /** The direct link from the debrid service, for the sites listed in Settings only. */
    private fun debridLink(url: String): String {
        val r = com.nexonic.downloadmanager.core.Debrid.request(debridService, debridKey, url)
        val c = (Net.opener?.invoke(java.net.URL(r.url)) ?: java.net.URL(r.url).openConnection()) as java.net.HttpURLConnection
        try {
            c.requestMethod = "POST"; c.doOutput = true; c.connectTimeout = 30_000; c.readTimeout = 60_000
            c.setRequestProperty("Content-Type", "application/x-www-form-urlencoded")
            r.bearer?.let { c.setRequestProperty("Authorization", "Bearer $it") }
            c.outputStream.use { it.write(com.nexonic.downloadmanager.core.Debrid.formBody(r.form).toByteArray()) }
            val body = (if (c.responseCode < 400) c.inputStream else c.errorStream)?.use { it.readBytes().toString(Charsets.UTF_8) }.orEmpty()
            return com.nexonic.downloadmanager.core.Debrid.parseAnswer(debridService, body)
        } finally { c.disconnect() }
    }

    private fun startTransfer(item: DownloadItem, direct: String? = null) {
        if (item.isVideo) return startVideo(item)
        if (item.isTorrent) return startTorrent(item)
        val id = item.id
        if (direct == null && com.nexonic.downloadmanager.core.Debrid.applies(item.url, debridService, debridKey, settings.debridHosts)) {
            // A fresh direct link on every start, so a resume never uses an expired one.
            update(id) { it.copy(status = Status.DOWNLOADING, error = null) }
            Thread {
                val link = runCatching { debridLink(item.url) }
                main.post {
                    val now = get(id) ?: return@post
                    if (now.status != Status.DOWNLOADING || id in transfers) return@post
                    link.fold({ startTransfer(now, it) }, { e -> update(id) { it.copy(status = Status.FAILED, error = e.message, speed = 0) }; pump() })
                }
            }.start()
            return
        }
        val s = secrets.get(id)
        // The site's cookies and login never go to the debrid service's link.
        val request = if (direct != null) DownloadRequest(direct, null, item.referer, item.userAgent)
            else DownloadRequest(item.url, s.cookies, item.referer, item.userAgent, s.user, s.password)
        val listener = object : TransferListener {
            override fun progress(state: TransferState, bytesPerSecond: Long) {
                update(id, save = false) {
                    if (it.status != Status.DOWNLOADING) it
                    else it.copy(written = state.written, size = state.size ?: it.size, speed = bytesPerSecond, resumable = state.resumable)
                }
            }

            override fun checkpoint(state: TransferState) {
                update(id) { if (it.status == Status.COMPLETE) it else it.copy(state = state, written = state.written, size = state.size ?: it.size) }
            }

            override fun finished(state: TransferState) {
                synchronized(this@Downloads) { transfers.remove(id) }
                val current = get(id) ?: return
                try {
                    val saved = Publisher.publish(app, partFile(id), current.fileName, mimes[id], settings.categoryFolders, settings.saveTree, current.url)
                    update(id) {
                        it.copy(status = Status.COMPLETE, written = state.written, size = state.size ?: state.written, speed = 0,
                            fileName = saved.name, contentUri = saved.uri.toString(), savedPath = saved.path,
                            finishedAt = System.currentTimeMillis(), state = null, error = null)
                    }
                    secrets.remove(id)
                    get(id)?.let { Notifier.finished(app, it) }
                } catch (e: Exception) {
                    update(id) { it.copy(status = Status.FAILED, speed = 0, error = "Could not save to Downloads: ${e.message}") }
                }
                main.post { pump() }
            }

            override fun failed(message: String, state: TransferState) {
                synchronized(this@Downloads) { transfers.remove(id) }
                update(id) { it.copy(status = Status.FAILED, error = message, speed = 0, state = state) }
                get(id)?.let { Notifier.failed(app, it) }
                main.post { pump() }
            }
        }
        val t = Transfer(request, partFile(id), settings.connections, item.state, listener, limiter, auto = settings.autoConnections)
        transfers[id] = t
        update(id) { it.copy(status = Status.DOWNLOADING, error = null) }
        t.start()
    }

    /** Called when the system ends background time (Android 15 limits data-sync services): keep progress, retry later. */
    fun pauseForSystem() {
        _items.value.filter { it.status == Status.DOWNLOADING }.forEach { item ->
            val t = synchronized(this) { transfers.remove(item.id) }
            t?.pause()
            update(item.id) { it.copy(status = Status.QUEUED, speed = 0) }
        }
    }

    /** Screenshots only (debug builds): replaces the list with sample downloads. */
    @Synchronized fun loadDemo(sample: List<DownloadItem>) {
        _items.value = sample
        persist()
    }

    val activeCount: Int get() = _items.value.count { it.status == Status.DOWNLOADING }
    val totalSpeed: Long get() = _items.value.filter { it.status == Status.DOWNLOADING }.sumOf { it.speed }
}

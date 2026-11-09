package com.nexonic.downloadmanager.core

import java.io.File
import java.io.PrintWriter
import java.io.StringWriter
import java.net.URI
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Download limit (Settings → Downloads): at most N MB every H hours. When the amount is reached, downloads stop and
 * wait for the next period; they start again by themselves. The amount used is kept across restarts.
 */
data class DataCap(val periodStart: Long = 0, val used: Long = 0) {
    fun length(hours: Int): Long = hours.coerceIn(1, 24 * 31) * 3_600_000L

    /** A new period when the old one is over (the same one otherwise). */
    fun rolled(now: Long, hours: Int): DataCap =
        if (periodStart > 0 && now >= periodStart && now - periodStart < length(hours)) this else DataCap(now, 0)

    fun plus(bytes: Long): DataCap = if (bytes > 0) copy(used = used + bytes) else this
    fun reached(limitMB: Int): Boolean = limitMB > 0 && used >= limitMB * 1024L * 1024L
    fun nextPeriod(hours: Int): Long = periodStart + length(hours)
}

/** Link Grabber: links in pasted text, grouped into packages (the parts of a split archive together). */
object LinkGrabber {
    data class Package(val name: String, val links: List<String>)

    const val OTHER_FILES = "Other files"
    private val plain = Regex("""(?:https?://|magnet:\?)[^\s"'<>\]\[)(]+""", RegexOption.IGNORE_CASE)
    private val setPart = Regex("""^(.+?)(?:\.part\d+\.rar|\.r\d{2,3}|\.z\d{2}|\.(?:7z|zip|rar|tar|gz)\.\d{3}|\.\d{3})$""", RegexOption.IGNORE_CASE)
    private val archive = Regex("""^(.+?)\.(?:rar|zip|7z|tar|tar\.gz|tgz|tar\.xz|tar\.bz2)$""", RegexOption.IGNORE_CASE)

    fun find(text: String): List<String> {
        val seen = LinkedHashSet<String>()
        for (m in plain.findAll(text)) {
            val link = m.value.trimEnd('.', ',', ';')
            if (link.startsWith("magnet:", ignoreCase = true) || runCatching { URI(link).host }.getOrNull() != null) seen.add(link)
            if (seen.size >= 5000) break
        }
        return seen.toList()
    }

    fun setName(fileName: String): String? = setPart.find(fileName)?.groupValues?.get(1)

    fun group(links: List<String>): List<Package> {
        val named = LinkedHashMap<String, MutableList<String>>()
        val other = mutableListOf<String>()
        for (link in links) {
            val file = if (link.startsWith("magnet:", ignoreCase = true)) "" else FileNames.fromUrl(link)
            val base = setName(file) ?: archive.find(file)?.groupValues?.get(1)
            if (base == null) other.add(link) else named.getOrPut(FileNames.sanitize(base)) { mutableListOf() }.add(link)
        }
        return named.map { Package(it.key, it.value) } + if (other.isEmpty()) emptyList() else listOf(Package(OTHER_FILES, other))
    }
}

/** Crash reports kept on the phone (Settings → Crash reports). Never sent anywhere; the newest 20 are kept. */
object CrashReports {
    fun scrub(text: String): String = text.replace(Regex("""(?i)\b(?:https?|magnet|ftp):[^\s"'<>]+"""), "[link]")

    fun text(error: Throwable, version: String, device: String, at: Date = Date()): String {
        val trace = StringWriter().also { error.printStackTrace(PrintWriter(it)) }.toString()
        val time = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.ROOT).format(at)
        return "Nexonic Download Manager crash report\nTime: $time\nApp version: $version\nDevice: $device\n\n${scrub(trace)}"
    }

    fun write(folder: File, text: String, at: Date = Date()) {
        runCatching {
            folder.mkdirs()
            val name = "crash-" + SimpleDateFormat("yyyyMMdd-HHmmss", Locale.ROOT).format(at) + ".txt"
            File(folder, name).writeText(text)
            folder.listFiles { f -> f.name.startsWith("crash-") }?.sortedByDescending { it.name }?.drop(20)?.forEach { it.delete() }
        }
    }

    fun latest(folder: File): File? = folder.listFiles { f -> f.name.startsWith("crash-") }?.maxByOrNull { it.name }
}

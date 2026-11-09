package com.nexonic.downloadmanager.core

import java.io.File
import java.io.IOException
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.Inet6Address
import java.net.InetAddress
import java.net.URL
import java.util.zip.GZIPInputStream

/**
 * Address ranges torrents never connect to. Reads P2P ("name:1.2.3.0-1.2.3.255"), DAT
 * ("001.002.003.000 - 001.002.003.255 , 000 , name"), CIDR ("1.2.3.0/24") and single addresses, plain or gzip.
 */
object TorrentBlocklist {
    const val MAX_RANGES = 2_000_000
    private const val MAX_BYTES = 256L * 1024 * 1024

    fun parse(input: InputStream): List<Pair<String, String>> {
        val buffered = input.buffered()
        buffered.mark(2)
        val gzip = buffered.read() == 0x1f && buffered.read() == 0x8b
        buffered.reset()
        val text = (if (gzip) GZIPInputStream(buffered) else buffered).bufferedReader()
        val out = ArrayList<Pair<String, String>>()
        var bytes = 0L
        text.forEachLine { raw ->
            bytes += raw.length + 1
            if (bytes > MAX_BYTES) throw IOException("The blocklist is too large.")
            val line = raw.trim()
            if (line.isEmpty() || line.startsWith("#") || line.startsWith(";") || line.startsWith("//")) return@forEachLine
            range(line)?.let { out += it }
            if (out.size > MAX_RANGES) throw IOException("The blocklist has more than $MAX_RANGES ranges.")
        }
        return out
    }

    private fun range(line: String): Pair<String, String>? {
        val comma = line.indexOf(',')
        val body = if (comma > 0 && line.substring(0, comma).contains('-')) line.substring(0, comma) else line
        val dash = body.lastIndexOf('-')
        if (dash > 0) {
            var left = body.substring(0, dash).trim()
            if (address(left) == null && left.contains(':')) left = left.substringAfterLast(':').trim()
            val a = address(left) ?: return null
            val b = address(body.substring(dash + 1).trim()) ?: return null
            if (a.size != b.size || compare(a, b) > 0) return null
            return text(a) to text(b)
        }
        val slash = body.indexOf('/')
        if (slash > 0) {
            val net = address(body.substring(0, slash).trim()) ?: return null
            val bits = body.substring(slash + 1).trim().toIntOrNull() ?: return null
            if (bits !in 0..net.size * 8) return null
            val first = net.copyOf(); val last = net.copyOf()
            for (i in bits until net.size * 8) {
                first[i / 8] = (first[i / 8].toInt() and (0x80 shr (i % 8)).inv()).toByte()
                last[i / 8] = (last[i / 8].toInt() or (0x80 shr (i % 8))).toByte()
            }
            return text(first) to text(last)
        }
        val one = address(body) ?: return null
        return text(one) to text(one)
    }

    /** IPv4 (also with leading zeros) or IPv6 as bytes; never looks a name up. */
    private fun address(s: String): ByteArray? {
        val parts = s.split('.')
        if (parts.size == 4) {
            val n = parts.map { p -> p.takeIf { it.length in 1..3 && it.all(Char::isDigit) }?.toInt()?.takeIf { it <= 255 } ?: return null }
            return ByteArray(4) { n[it].toByte() }
        }
        if (!s.contains(':') || !s.all { it.isLetterOrDigit() || it == ':' || it == '.' }) return null
        return runCatching { InetAddress.getByName(s) }.getOrNull()?.takeIf { it is Inet6Address }?.address
    }

    private fun compare(a: ByteArray, b: ByteArray): Int {
        for (i in a.indices) if (a[i] != b[i]) return (a[i].toInt() and 0xff) - (b[i].toInt() and 0xff)
        return 0
    }

    private fun text(b: ByteArray): String = InetAddress.getByAddress(b).hostAddress!!.substringBefore('%')

    /** A problem with the setting, or null. Android takes https:// addresses only. */
    fun problem(setting: String): String? {
        val s = setting.trim()
        return if (s.isEmpty() || s.startsWith("https://", ignoreCase = true)) null else "The blocklist address must start with https://"
    }

    /**
     * The ranges for the setting: the list at the https:// address, downloaded at most once a day into [cacheDir]
     * (the last copy is used when the server can't be reached). Empty setting = no ranges.
     */
    fun load(setting: String, cacheDir: File): List<Pair<String, String>> {
        val s = setting.trim()
        if (s.isEmpty()) return emptyList()
        problem(s)?.let { throw IOException(it) }
        val cache = File(cacheDir, "blocklist-" + Integer.toHexString(s.hashCode()) + ".list")
        if (!cache.exists() || System.currentTimeMillis() - cache.lastModified() > 86_400_000L) {
            runCatching {
                val url = URL(s)
                val c = (Net.opener?.invoke(url) ?: url.openConnection()) as HttpURLConnection
                c.connectTimeout = 30_000; c.readTimeout = 60_000
                try {
                    if (c.responseCode != 200) throw IOException("HTTP ${c.responseCode}")
                    val tmp = File(cacheDir, cache.name + ".tmp")
                    cacheDir.mkdirs()
                    c.inputStream.use { input -> tmp.outputStream().use { input.copyTo(it) } }
                    if (tmp.length() > MAX_BYTES || tmp.inputStream().use { parse(it) }.isEmpty()) { tmp.delete(); throw IOException("no ranges") }
                    tmp.renameTo(cache)
                } finally { c.disconnect() }
            }.onFailure { if (!cache.exists()) throw IOException("The blocklist could not be downloaded: ${it.message}") }
        }
        val ranges = cache.inputStream().use { parse(it) }
        if (ranges.isEmpty()) throw IOException("The blocklist has no address ranges.")
        return ranges
    }
}

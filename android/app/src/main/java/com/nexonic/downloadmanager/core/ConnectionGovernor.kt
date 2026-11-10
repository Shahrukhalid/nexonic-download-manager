package com.nexonic.downloadmanager.core

import java.io.IOException

/**
 * How many connections a download may use right now, as in the desktop apps. Auto (the default) starts with a few, adds
 * more while the measured speed keeps rising and stops adding when it no longer does; any mode backs off when the server
 * refuses (HTTP 429/503/509) and waits as long as the server asks (Retry-After) before growing again.
 */
class ConnectionGovernor(private val auto: Boolean, maximum: Int, now: Long) {
    companion object {
        const val AUTO_START = 4
        const val SAMPLE_MS = 1_000L

        fun isRefusal(code: Int) = code == 429 || code == 503 || code == 509

        /** Retry-After as seconds or an HTTP date, in milliseconds; null when absent or unreadable. */
        fun retryAfterMs(header: String?, nowMs: Long): Long? {
            val h = header?.trim()?.takeIf { it.isNotEmpty() } ?: return null
            h.toLongOrNull()?.let { return maxOf(0L, it) * 1000 }
            val at = runCatching {
                java.time.ZonedDateTime.parse(h, java.time.format.DateTimeFormatter.RFC_1123_DATE_TIME).toInstant().toEpochMilli()
            }.getOrNull() ?: return null
            return maxOf(0L, at - nowMs)
        }
    }

    private val ceiling = maximum.coerceIn(1, SegmentPlanner.MAX_CONNECTIONS)
    private var allowed = if (auto) minOf(AUTO_START, ceiling) else ceiling
    private var bytes = 0L
    private var sampleStart = now
    private var bestRate = 0.0
    private var quietUntil = 0L
    private var refusedAt = Int.MAX_VALUE  // the number of connections a server refused at: Auto stays below it

    @get:Synchronized val allowedNow: Int get() = allowed
    @get:Synchronized val quietUntilMs: Long get() = quietUntil

    @Synchronized fun received(n: Long) { bytes += n }

    /** At the end of each sample: Auto doubles the connections while the speed is still rising (by at least 10 %), staying below a number the server refused. A fixed setting
     *  slowly returns to its number after a back-off. */
    @Synchronized fun tick(now: Long, running: Int) {
        if (now - sampleStart < SAMPLE_MS) return
        val rate = bytes * 1000.0 / maxOf(1L, now - sampleStart)
        bytes = 0; sampleStart = now
        if (now < quietUntil) return
        if (!auto) { if (allowed < ceiling) allowed = minOf(ceiling, allowed + maxOf(1, allowed / 4)); return }
        if (running < allowed) return  // not every allowed connection is busy yet: the sample says nothing
        if (rate > bestRate * 1.10) { bestRate = rate; allowed = maxOf(allowed, minOf(ceiling, refusedAt - 1, allowed * 2)) }
    }

    /** The server refused a connection: halve (one always stays) and wait as long as it asked (30 s when it did not say,
     *  at most 10 minutes). */
    @Synchronized fun refused(now: Long, retryAfterMs: Long?) {
        refusedAt = minOf(refusedAt, maxOf(2, allowed))
        allowed = maxOf(1, allowed / 2)
        val wait = retryAfterMs?.coerceIn(1_000L, 600_000L) ?: 30_000L
        if (now + wait > quietUntil) quietUntil = now + wait
        bestRate = 0.0
    }
}

/** The server asked this connection to stop (429/503): its part goes back to the waiting list, nothing is lost. */
class RefusedException(val retryAfterMs: Long?, code: Int) : IOException("The server is busy (HTTP $code)")

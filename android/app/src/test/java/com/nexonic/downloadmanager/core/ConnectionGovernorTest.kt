package com.nexonic.downloadmanager.core

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.util.Random
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

/** Adaptive connections and busy servers: the same rules as the desktop apps; nothing is lost or corrupted. */
class ConnectionGovernorTest {
    @Test fun autoGrowsWhileTheSpeedRisesAndStops() {
        val g = ConnectionGovernor(auto = true, maximum = 64, now = 0)
        assertEquals(ConnectionGovernor.AUTO_START, g.allowedNow)
        var t = 0L
        var speed = 1_000_000L
        repeat(3) {                      // speed rises with each step: more connections
            g.received(speed * 2); t += 2_000; g.tick(t, g.allowedNow); speed *= 2
        }
        val grown = g.allowedNow
        assertTrue("grew to $grown", grown > ConnectionGovernor.AUTO_START)
        repeat(4) { g.received(speed * 2); t += 2_000; g.tick(t, g.allowedNow) }  // flat speed: no more growth
        assertEquals(minOf(64, grown * 2), g.allowedNow)                          // only the first flat sample still counted as a gain
        assertTrue(g.allowedNow <= 64)
    }

    @Test fun refusalHalvesAndWaitsAsAsked() {
        val g = ConnectionGovernor(auto = false, maximum = 16, now = 0)
        assertEquals(16, g.allowedNow)
        g.refused(1_000, 5_000)
        assertEquals(8, g.allowedNow)
        assertEquals(6_000, g.quietUntilMs)
        g.tick(4_000, 8)                  // still waiting: no growth
        assertEquals(8, g.allowedNow)
        g.tick(8_000, 8)                  // after the wait a fixed setting climbs back
        assertEquals(10, g.allowedNow)
        g.refused(9_000, null)
        assertEquals(39_000, g.quietUntilMs)  // 30 s when the server does not say
        g.refused(9_000, 3_600_000)
        assertEquals(609_000, g.quietUntilMs) // at most 10 minutes
        assertEquals(256, ConnectionGovernor(false, 999, 0).allowedNow)
    }

    @Test fun autoStaysBelowTheNumberAServerRefused() {
        val g = ConnectionGovernor(auto = true, maximum = 64, now = 0)
        var t = 0L
        var speed = 1_000_000L
        fun rising() { t += 1_000; g.received(speed); g.tick(t, g.allowedNow); speed *= 2 }
        rising()
        assertEquals(8, g.allowedNow)
        g.refused(t, 1_000)
        assertEquals(4, g.allowedNow)
        t += 2_000
        repeat(6) { rising() }
        assertEquals(7, g.allowedNow)
    }

    @Test fun readsRetryAfter() {
        assertEquals(120_000L, ConnectionGovernor.retryAfterMs("120", 0))
        val now = 1_700_000_000_000L
        val date = java.time.format.DateTimeFormatter.RFC_1123_DATE_TIME.format(java.time.Instant.ofEpochMilli(now + 30_000).atZone(java.time.ZoneOffset.UTC))
        assertEquals(30_000L, ConnectionGovernor.retryAfterMs(date, now))
        assertNull(ConnectionGovernor.retryAfterMs("soon", now))
        assertTrue(ConnectionGovernor.isRefusal(429) && ConnectionGovernor.isRefusal(503) && !ConnectionGovernor.isRefusal(500))
    }

    @Test fun aBusyServerNeverCorruptsTheFile() {
        val content = ByteArray(5 * 1024 * 1024 + 77).also { Random(3).nextBytes(it) }
        val requests = AtomicInteger()
        val refused = AtomicInteger()
        val server = MiniHttpServer()
        server.createContext("/f") { ex ->
            val range = ex.requestHeaders.getFirst("Range")
            // Every third request for a part is refused with 429 and a short Retry-After.
            if (range != null && requests.incrementAndGet() % 3 == 0) {
                refused.incrementAndGet()
                ex.responseHeaders.add("Retry-After", "1"); ex.sendResponseHeaders(429, -1); ex.close(); return@createContext
            }
            ex.responseHeaders.add("ETag", "\"x\"")
            var start = 0; var end = content.size - 1; var code = 200
            if (range != null) {
                val m = Regex("bytes=(\\d+)-(\\d*)").find(range)!!
                start = m.groupValues[1].toInt(); if (m.groupValues[2].isNotEmpty()) end = minOf(end, m.groupValues[2].toInt())
                code = 206; ex.responseHeaders.add("Content-Range", "bytes $start-$end/${content.size}")
            }
            ex.sendResponseHeaders(code, (end - start + 1).toLong())
            runCatching { ex.responseBody.use { it.write(content, start, end - start + 1) } }
        }
        server.start()
        val dir = kotlin.io.path.createTempDirectory("ndm-busy").toFile()
        try {
            val f = File(dir, "f.bin")
            val done = CountDownLatch(1)
            var error: String? = null
            val listener = object : TransferListener {
                override fun progress(state: TransferState, bytesPerSecond: Long) {}
                override fun finished(state: TransferState) { done.countDown() }
                override fun failed(message: String, state: TransferState) { error = message; done.countDown() }
                override fun paused(state: TransferState) {}
            }
            Transfer(DownloadRequest("http://127.0.0.1:${server.port}/f"), f, 16, null, listener, auto = true).start()
            assertTrue("timed out", done.await(90, TimeUnit.SECONDS))
            assertNull(error)
            assertTrue("the server refused some parts", refused.get() > 0)
            assertArrayEquals(content, f.readBytes())
        } finally {
            server.stop(); dir.deleteRecursively()
        }
    }
}

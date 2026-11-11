package com.nexonic.downloadmanager.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ProfileBatchTest {
    private fun e(id: String, title: String = "Clip", seconds: Double? = 120.0, kind: String = "video", ok: Boolean = true) =
        BatchEntry("https://v.test/watch?v=$id", title, seconds, 1_793_700_000_000L, kind, ok)

    @Test fun readsTheEnginesLines() {
        val (entry, owner) = ProfileBatch.parseLine("""{"url": "https://v.test/a.mp4#x", "title": "Episode two", "timestamp": 1793700000, "playlist_title": "Test Channel", "duration": 61}""")!!
        assertEquals("Test Channel", owner)
        assertEquals(61.0, entry.duration!!, 0.0)
        assertEquals(1_793_700_000_000L, entry.date)
        val yt = ProfileBatch.parseLine("""{"url": "abc", "id": "abc", "ie_key": "Youtube", "title": "[Private video]"}""")!!.first
        assertEquals("https://www.youtube.com/watch?v=abc", yt.url)
        assertFalse(yt.available)
        assertEquals("shorts", ProfileBatch.parseLine("""{"url": "https://www.youtube.com/shorts/x"}""")!!.first.kind)
        assertEquals(null, ProfileBatch.parseLine("WARNING: x"))
    }

    @Test fun choosesAndCountsHonestly() {
        val found = listOf(e("1", "Trip"), e("2", "Tiny", 20.0), e("3", "Private", ok = false), e("4", "Have"), e("1", "Trip"),
            e("5", "Unknown length", null), e("6", "Short", kind = "shorts"))
        val (take, c) = ProfileBatch.choose(found, BatchFilter(minSeconds = 60, kind = "videos"), { it.endsWith("=4") })
        assertEquals(listOf("Trip", "Unknown length"), take.map { it.title })
        assertEquals(BatchCounts(6, 2, 1, 2, 1), c)
        assertFalse(ProfileBatch.choose(found, BatchFilter(), { false }, setOf(ProfileBatch.key(e("1").url))).first.any { it.title == "Trip" })
        assertEquals(listOf("Trip"), ProfileBatch.choose(found, BatchFilter(keywords = "trip !tiny"), { false }).first.map { it.title })
    }

    @Test fun exportQualitiesProfilesAndNoLogin() {
        val csv = ProfileBatch.export(listOf(e("1", "Say \"hi\", world")), "csv").split("\r\n")
        assertEquals("https://v.test/watch?v=1,\"Say \"\"hi\"\", world\",2026-11-03,120,video,yes", csv[1])
        assertEquals("https://v.test/watch?v=1\n", ProfileBatch.export(listOf(e("1")), "txt"))
        assertTrue(ProfileBatch.quality("audio").audio)
        assertTrue(ProfileBatch.quality("4k").selector.contains("height<=2160"))
        val opts = ProfileBatch.options(41, 60).toMap()
        assertEquals("100", opts["--playlist-end"])
        assertFalse(opts.containsKey("--cookies") || opts.containsKey("--username"))
        val p = SavedProfile("i", "https://v.test/c", "C", "720", BatchFilter(keywords = "a"), 6, 0, "", setOf("k"))
        assertEquals(listOf(p), ProfileBatch.profilesFrom(ProfileBatch.profilesToJson(listOf(p))))
        assertTrue(p.due())
        assertFalse(p.copy(lastChecked = System.currentTimeMillis()).due())
    }
}

package com.nexonic.downloadmanager.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class EverydayTest {
    @Test fun downloadLimitPeriods() {
        var cap = DataCap().rolled(1_000_000, 24)
        cap = cap.plus(600L shl 20)
        assertFalse(cap.reached(1024))
        cap = cap.plus(500L shl 20)
        assertTrue(cap.reached(1024))
        assertEquals(cap, cap.rolled(1_000_000 + 23 * 3_600_000L, 24))
        assertFalse(cap.rolled(1_000_000 + 24 * 3_600_000L, 24).reached(1024))
        assertFalse(DataCap().plus(5L shl 30).reached(0))
    }

    @Test fun linkGrabberGroupsSplitArchives() {
        val links = LinkGrabber.find("get https://h.test/Movie.part1.rar, https://h.test/song.mp3 and https://h.test/Movie.part2.rar.")
        assertEquals(3, links.size)
        val packages = LinkGrabber.group(links)
        assertEquals(listOf("Movie", LinkGrabber.OTHER_FILES), packages.map { it.name })
        assertEquals(2, packages[0].links.size)
        assertEquals("Data", LinkGrabber.setName("Data.7z.001"))
        assertNull(LinkGrabber.setName("video.mp4"))
    }

    @Test fun crashReportsLeaveOutLinks() {
        val text = CrashReports.text(IllegalStateException("failed https://h.test/f?token=abc"), "1.0", "test")
        assertTrue(text.contains("[link]"))
        assertFalse(text.contains("token=abc"))
    }
}

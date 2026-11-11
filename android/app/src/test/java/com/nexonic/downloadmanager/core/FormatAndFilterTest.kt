package com.nexonic.downloadmanager.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.TimeZone

class FormatAndFilterTest {
    @Test fun sizesAndSpeedsFollowTheChosenUnits() {
        assertEquals("1.5 MB", Format.size(1_572_864, "binary"))
        assertEquals("1.5 MiB", Format.size(1_572_864, "iec"))
        assertEquals("1.6 MB", Format.size(1_572_864, "decimal"))
        assertEquals("999 B", Format.size(999, "decimal"))
        assertEquals("2.0 MB/s", Format.speed(2L * 1024 * 1024, "bytes", "binary"))
        assertEquals("16.8 Mbit/s", Format.speed(2L * 1024 * 1024, "bits", "binary"))
        assertEquals("", Format.speed(0, "bits", "binary"))
    }

    @Test fun datesCanBeRelative() {
        val utc = TimeZone.getTimeZone("UTC")
        val now = 1_800_000_000_000L // 15 Jan 2027, 08:00 UTC
        assertEquals("Just now", Format.date(now - 20_000, "relative", now, utc))
        assertEquals("5 min ago", Format.date(now - 300_000, "relative", now, utc))
        assertEquals("3 h ago", Format.date(now - 3 * 3_600_000L, "relative", now, utc))
        assertTrue(Format.date(now - 86_400_000L, "relative", now, utc).startsWith("Yesterday "))
        assertEquals("4 days ago", Format.date(now - 4 * 86_400_000L, "relative", now, utc))
        assertEquals(Format.date(now - 30 * 86_400_000L, "absolute", now, utc), Format.date(now - 30 * 86_400_000L, "relative", now, utc))
        assertEquals(Format.date(now + 3_600_000L, "absolute", now, utc), Format.date(now + 3_600_000L, "relative", now, utc))
    }

    @Test fun moreFileTypes() {
        assertEquals(FileCategory.MODELS, FileCategory.of("llama.Q4_K_M.gguf"))
        assertEquals(FileCategory.IMAGES, FileCategory.of("IMG_1.CR3"))
        assertEquals(FileCategory.DOCUMENTS, FileCategory.of("bracket.dwg"))
        assertEquals(FileCategory.COMPRESSED, FileCategory.of("a.lz4"))
        assertEquals("Models", FileCategory.MODELS.folder)
    }

    @Test fun batchListsAndFilters() {
        assertEquals(listOf("https://x.test/p1.jpg", "https://x.test/p1.png", "https://x.test/p2.jpg", "https://x.test/p2.png"),
            BatchPattern.expand("https://x.test/p[1-2].{jpg, png}"))
        val all = listOf("https://x.test/a.jpg?s=1", "https://x.test/a-thumb.jpg", "https://x.test/b.PNG", "https://x.test/c.zip")
        assertEquals(listOf("https://x.test/a.jpg?s=1"), LinkFilter.apply(all, "*.jpg !*thumb*"))
        assertEquals(listOf("https://x.test/c.zip"), LinkFilter.apply(all, "zip"))
        assertEquals(all, LinkFilter.apply(all, " "))
    }

    @Test fun soundConversionAndQuietHours() {
        assertEquals(listOf("-x", "--audio-format", "flac"), AudioConversion.arguments("flac"))
        assertEquals(emptyList<String>(), AudioConversion.arguments("original"))
        assertTrue(QuietHours.active("22:00-07:00", 23 * 60))
        assertTrue(QuietHours.active("22:00-07:00", 6 * 60 + 59))
        assertFalse(QuietHours.active("22:00-07:00", 7 * 60))
        assertTrue(QuietHours.active("13:00-14:00", 13 * 60 + 30))
        assertFalse(QuietHours.active("", 0))
        assertNull(QuietHours.parse("25:00-07:00"))
    }
}

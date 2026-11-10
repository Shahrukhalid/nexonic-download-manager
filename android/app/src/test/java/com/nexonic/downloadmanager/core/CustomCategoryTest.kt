package com.nexonic.downloadmanager.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class CustomCategoryTest {
    @Test fun sitesAndIconsRoundTripThroughTheSettingsText() {
        val text = "📚 Books: epub pdf > Books\nReleases: zip @ github.com/*/releases > Releases\n🎨 Art: @ *.deviantart.com pixiv.net"
        val (list, problem) = CustomCategory.parse(text)
        assertNull(problem)
        assertEquals(3, list.size)
        assertEquals("📚" to "Books", list[0].icon to list[0].name)
        assertEquals("github.com/*/releases", list[1].sites)
        assertEquals("", list[2].extensions)
        assertEquals(text, CustomCategory.toLines(list))
        assertEquals("" to "C# Notes", CustomCategory.splitIcon("C# Notes"))
    }

    @Test fun firstMatchingCategoryInOrderWins() {
        val list = CustomCategory.parse("Releases: zip @ https://GitHub.com/*/releases > Releases\nZips: zip > Zips\nArt: @ *.deviantart.com pixiv.net > Pictures/Art").first
        assertEquals("Releases", CustomCategory.subfolderFor(list, "a.zip", "https://github.com/o/r/releases/download/v1/a.zip", false))
        assertEquals("Zips", CustomCategory.subfolderFor(list, "a.zip", "https://github.com/o/r/archive/a.zip", false))
        assertEquals("Pictures/Art", CustomCategory.subfolderFor(list, "x.png", "https://images.deviantart.com/x.png", false))
        assertEquals("Images", CustomCategory.subfolderFor(list, "x.png", "https://notpixiv.net/x.png", true))
        assertNull(CustomCategory.subfolderFor(list, "x.png", "https://notpixiv.net/x.png", false))
        assertEquals("Bad", CustomCategory("Bad", "x", "../up").subfolder)
    }

    @Test fun badLinesAreReported() {
        assertTrue(CustomCategory.parse("no colon here").second!!.contains("Line 1"))
        assertTrue(CustomCategory.parse("Odd: @ ").second!!.contains("Line 1"))
        assertFalse(CustomCategory.siteMatches(listOf("example.com"), "file:///example.com/x"))
        assertTrue(CustomCategory.siteMatches(listOf("example.com"), "https://cdn.example.com/f"))
        assertFalse(CustomCategory.siteMatches(listOf("example.com"), "https://badexample.com/f"))
    }
}

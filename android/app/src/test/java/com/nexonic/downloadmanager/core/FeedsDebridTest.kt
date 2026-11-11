package com.nexonic.downloadmanager.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException

class FeedsDebridTest {
    private val rss = """<?xml version="1.0"?><rss xmlns:torrent="http://xmlns.ezrss.it/0.1/"><channel>
<item><title>Show S01E01 1080p</title><link>https://example.com/1</link><guid>a1</guid><enclosure url="https://example.com/1.torrent" length="1048576000"/></item>
<item><title>Show S01E01 720p</title><torrent:magnetURI>magnet:?xt=urn:btih:abc</torrent:magnetURI><guid>a2</guid></item>
<item><title>Other 1080p</title><link>https://example.com/3.zip</link></item></channel></rss>""".toByteArray()

    @Test fun parseAndRules() {
        val items = Feeds.parse(rss)
        assertEquals(listOf("https://example.com/1.torrent", "magnet:?xt=urn:btih:abc", "https://example.com/3.zip"), items.map { it.link })
        assertEquals(1_048_576_000L, items[0].size)
        assertEquals("https://example.com/a", Feeds.parse("""<feed xmlns="http://www.w3.org/2005/Atom"><entry><title>A</title><id>x</id><link href="https://example.com/a"/></entry></feed>""".toByteArray())[0].link)
        val f = FeedSubscription(include = "show", exclude = "cam", quality = "1080p 720p", oneOfEachEpisode = true)
        assertTrue(Feeds.wanted(f, items[0]))
        assertFalse(Feeds.wanted(f, items[2]))
        assertEquals(listOf("a1"), Feeds.choose(f, items).map { it.id })
        assertNotNull(Feeds.ruleProblem("/[/"))
        assertNull(Feeds.ruleProblem("/S01E0[12]/"))
        assertEquals("show name|s01e02", Feeds.episodeKey("[Grp] Show Name - S01E02 1080p"))
        assertEquals("2160p", Feeds.qualityOf("Movie 4K HDR"))
    }

    @Test fun firstCheckOnlyRemembers() {
        val f = FeedSubscription(url = "https://example.com/feed")
        assertTrue(Feeds.check(f, rss).isEmpty())
        f.seen.removeAt(0)
        assertEquals(1, Feeds.check(f, rss).size)
        assertTrue(Feeds.check(f, rss).isEmpty())
        val copy = Feeds.fromJson(Feeds.toJson(listOf(f)))[0]
        assertEquals(f.seen, copy.seen)
        assertFalse(Feeds.due(f))
        assertTrue(Feeds.due(f, System.currentTimeMillis() + 31 * 60_000L))
    }

    @Test(expected = Exception::class) fun refusesDoctype() {
        Feeds.parse("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><rss/>".toByteArray())
    }

    @Test fun debrid() {
        assertTrue(Debrid.applies("https://www.filehost.example/f/1", DebridService.REAL_DEBRID, "k", "filehost.example, other.example"))
        assertFalse(Debrid.applies("https://example.com/x", DebridService.REAL_DEBRID, "k", "filehost.example"))
        assertFalse(Debrid.applies("https://filehost.example/x", DebridService.NONE, "k", "filehost.example"))
        assertEquals("https://dl.example/x.bin", Debrid.parseAnswer(DebridService.REAL_DEBRID, """{"download":"https://dl.example/x.bin"}"""))
        assertEquals("https://ad.example/x", Debrid.parseAnswer(DebridService.ALL_DEBRID, """{"status":"success","data":{"link":"https://ad.example/x"}}"""))
        assertEquals("https://pm.example/x", Debrid.parseAnswer(DebridService.PREMIUMIZE, """{"status":"success","content":[{"link":"https://pm.example/x"}]}"""))
        val r = Debrid.request(DebridService.REAL_DEBRID, "k", "https://filehost.example/f")
        assertEquals("k", r.bearer)
        assertEquals("link=https%3A%2F%2Ffilehost.example%2Ff", Debrid.formBody(r.form))
        try { Debrid.parseAnswer(DebridService.REAL_DEBRID, """{"error":"bad_token"}"""); throw AssertionError() } catch (e: IOException) { assertTrue(e.message!!.contains("bad_token")) }
    }

    @Test fun colorSchemes() {
        assertEquals(listOf("Ocean", "Forest", "Midnight", "High Contrast"), ColorSchemeChoice.builtIn.map { it.name })
        assertNull(ColorSchemeChoice.named("Nope"))
    }
}

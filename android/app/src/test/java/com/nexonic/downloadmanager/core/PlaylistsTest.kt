package com.nexonic.downloadmanager.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Locale

class PlaylistsTest {
    @Test fun knowsPlaylistLinks() {
        assertTrue(Playlists.isPlaylist("https://www.youtube.com/playlist?list=PL123"))
        assertTrue(Playlists.isPlaylist("https://m.youtube.com/@channel"))
        assertFalse(Playlists.isPlaylist("https://www.youtube.com/watch?v=abc&list=PL123"))
        assertFalse(Playlists.isPlaylist("https://youtu.be/abc"))
        assertTrue(Playlists.isPlaylist("https://soundcloud.com/artist/sets/album"))
        assertTrue(Playlists.isPlaylist("https://artist.bandcamp.com/album/name"))
        assertFalse(Playlists.isPlaylist("magnet:?xt=urn:btih:abc"))
        assertFalse(Playlists.isPlaylist("not a link"))
        assertEquals("https://www.youtube.com/@channel/videos", Playlists.normalize("https://www.youtube.com/@channel"))
        assertEquals("https://www.youtube.com/playlist?list=PL1", Playlists.normalize("https://www.youtube.com/playlist?list=PL1"))
    }

    @Test fun readsEntriesWithoutDuplicates() {
        val json = """{"title":"Mix","entries":[{"ie_key":"Youtube","id":"abc","url":"abc","title":"One","duration":61},""" +
            """{"url":"https://www.youtube.com/watch?v=def","title":"Two"},{"url":"https://www.youtube.com/watch?v=def"},{"title":"no link"}]}"""
        val r = Playlists.parse(json)!!
        assertEquals("Mix", r.title)
        assertEquals(listOf("https://www.youtube.com/watch?v=abc", "https://www.youtube.com/watch?v=def"), r.entries.map { it.url })
        assertEquals(61.0, r.entries.first().duration!!, 0.0)
        assertNull(Playlists.parse("not json"))
        assertEquals("1:01", Playlists.duration(61.0))
        assertEquals("1:02:03", Playlists.duration(3723.0))
    }

    @Test fun subtitlesNeverFailTheVideo() {
        assertEquals("ur.*,en.*", VideoSubtitles.languages(Locale("ur", "PK")))
        assertEquals("en.*", VideoSubtitles.languages(Locale.US))
        assertTrue(VideoSubtitles.arguments(Locale.US).contains("--ignore-errors"))
    }
}

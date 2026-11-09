package com.nexonic.downloadmanager.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.util.zip.GZIPOutputStream

class TorrentBlocklistTest {
    @Test fun formats() {
        val text = "# c\nBad:1.2.3.0-1.2.3.255\n001.002.004.000 - 001.002.004.010 , 000 , x\n10.0.0.0/8\n5.6.7.8\n2001:db8::/32\nnot a line\n9.9.9.9-1.1.1.1\n"
        assertEquals(listOf("1.2.3.0" to "1.2.3.255", "1.2.4.0" to "1.2.4.10", "10.0.0.0" to "10.255.255.255", "5.6.7.8" to "5.6.7.8",
            "2001:db8:0:0:0:0:0:0" to "2001:db8:ffff:ffff:ffff:ffff:ffff:ffff"), TorrentBlocklist.parse(text.byteInputStream()))
    }

    @Test fun gzip() {
        val bytes = ByteArrayOutputStream().also { o -> GZIPOutputStream(o).use { it.write("x:9.9.9.0-9.9.9.9\n".toByteArray()) } }.toByteArray()
        assertEquals(listOf("9.9.9.0" to "9.9.9.9"), TorrentBlocklist.parse(bytes.inputStream()))
    }

    @Test fun setting() {
        assertNull(TorrentBlocklist.problem(""))
        assertNull(TorrentBlocklist.problem("https://example.com/list.gz"))
        assertNotNull(TorrentBlocklist.problem("http://example.com/list"))
    }
}

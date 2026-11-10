package com.nexonic.downloadmanager.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** cURL import (browser "Copy as cURL" for bash and Windows) and export: the same rules as the desktop apps. */
class CurlCommandTest {
    @Test fun readsABrowserBashCommand() {
        val text = "curl 'https://cdn.test/files/a%20b.zip?sig=1' \\\n  -H 'accept: */*' \\\n  -H 'referer: https://site.test/page' \\\n" +
            "  -H \$'cookie: sid=1; theme=it\\'s' \\\n  -H 'user-agent: Agent/1' \\\n  -H 'x-token: abc' \\\n  --compressed"
        val p = CurlCommand.parse(text)
        assertNull(p.problem)
        val r = p.request!!
        assertEquals("https://cdn.test/files/a%20b.zip?sig=1", r.url)
        assertEquals("https://site.test/page", r.referrer)
        assertEquals("sid=1; theme=it's", r.cookies)
        assertEquals("Agent/1", r.userAgent)
        assertEquals(listOf("x-token"), r.ignored)
    }

    @Test fun readsAWindowsCmdCommandAndBasicLogin() {
        val text = "curl ^\"https://h.test/f.iso^\" ^\r\n  -H ^\"Authorization: Basic dXNlcjpwYXNzOndvcmQ=^\" ^\r\n  -b ^\"a=1^\""
        val r = CurlCommand.parse(text).request!!
        assertEquals("https://h.test/f.iso", r.url)
        assertEquals("user", r.username)
        assertEquals("pass:word", r.password)
        assertEquals("a=1", r.cookies)
    }

    @Test fun refusesWhatADownloadCannotRepeat() {
        assertTrue(CurlCommand.parse("curl -X POST https://a.test/x").problem!!.contains("POST"))
        assertTrue(CurlCommand.parse("curl https://a.test/x --data-raw 'a=1'").problem!!.contains("sends data"))
        assertTrue(CurlCommand.parse("curl 'https://a.test/x").problem!!.contains("not closed"))
        assertTrue(CurlCommand.parse("curl file:///etc/passwd").problem!!.contains("no web address"))
        assertTrue(CurlCommand.parse("wget https://a.test/x").problem!!.contains("not a cURL"))
        assertTrue(CurlCommand.looks("  curl https://a.test"))
        assertFalse(CurlCommand.looks("https://a.test/curl x"))
    }

    @Test fun exportsACommandThatReadsBackTheSame() {
        val cmd = CurlCommand.command("https://h.test/it's.zip", "it's.zip", "https://h.test/", "Agent/1", "a=1; b=2", "me", "p")
        assertTrue(cmd.startsWith("curl -L -C - -o 'it'\\''s.zip'"))
        val r = CurlCommand.parse(cmd).request!!
        assertEquals(listOf("https://h.test/it's.zip", "https://h.test/", "a=1; b=2", "Agent/1", "me", "p"),
            listOf(r.url, r.referrer, r.cookies, r.userAgent, r.username, r.password))
    }
}

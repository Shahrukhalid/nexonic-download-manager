package com.nexonic.downloadmanager.core

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.File

/** The Profile Batch behaviour every app shares (tests/profile-batch-vectors.json). */
class ProfileBatchParityTest {
    private fun vectors(): JSONObject {
        var d: File? = File("").absoluteFile
        while (d != null) {
            val f = File(d, "tests/profile-batch-vectors.json")
            if (f.exists()) return JSONObject(f.readText())
            d = d.parentFile
        }
        error("tests/profile-batch-vectors.json not found")
    }

    @Test fun keysMatchEveryApp() {
        val keys = vectors().getJSONArray("keys")
        for (i in 0 until keys.length()) {
            val pair = keys.getJSONArray(i)
            assertEquals(pair.getString(0), pair.getString(1).trimEnd('/'), ProfileBatch.key(pair.getString(0)).trimEnd('/'))
        }
    }

    @Test fun duplicatesMatchEveryApp() {
        val c = vectors().getJSONObject("choose")
        val f = c.getJSONArray("found")
        val found = (0 until f.length()).map { BatchEntry(f.getString(it), "Clip") }
        val d = c.getJSONArray("downloaded")
        val done = (0 until d.length()).map { ProfileBatch.key(d.getString(it)) }.toSet()
        val (take, counts) = ProfileBatch.choose(found, BatchFilter(max = 100), { done.contains(ProfileBatch.key(it)) })
        assertEquals(c.getInt("discovered"), counts.discovered)
        assertEquals(c.getInt("queued"), counts.queued)
        assertEquals(c.getInt("duplicates"), counts.duplicates)
        assertEquals(take.size, counts.queued)
    }
}

package com.nexonic.downloadmanager.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDateTime

class SchedulesTest {
    @Test fun speedScheduleRules() {
        val p = SpeedSchedule.parse("# office\nMon-Fri 09:00-17:00 300\nweekends 23:00-07:00 0\nSat 10:00-12:00 50")
        assertNull(p.problem)
        assertEquals(3, p.rules.size)
        assertEquals(300, SpeedSchedule.limitAt(p.rules, LocalDateTime.of(2026, 11, 2, 10, 0)))  // Monday
        assertNull(SpeedSchedule.limitAt(p.rules, LocalDateTime.of(2026, 11, 2, 18, 0)))
        assertEquals(0, SpeedSchedule.limitAt(p.rules, LocalDateTime.of(2026, 11, 7, 23, 30)))   // Saturday night
        assertEquals(0, SpeedSchedule.limitAt(p.rules, LocalDateTime.of(2026, 11, 9, 6, 0)))     // Monday morning, from Sunday
        assertNull(SpeedSchedule.limitAt(p.rules, LocalDateTime.of(2026, 11, 3, 6, 0)))         // Tuesday morning: Monday is not a weekend day
        assertEquals(50, SpeedSchedule.limitAt(p.rules, LocalDateTime.of(2026, 11, 7, 11, 0)))
        assertTrue(SpeedSchedule.parse("Mon 9-17 300").problem!!.startsWith("Line 1"))
        assertTrue(SpeedSchedule.parse("Funday 09:00-10:00 1").problem != null)
        assertEquals(1, SpeedSchedule.parse("Fri-Mon 22:00-23:00 5").rules.size)
    }

    @Test fun duplicateLinks() {
        assertTrue(Duplicates.same("HTTPS://Example.COM/a%20b.zip#x", "https://example.com/a%20b.zip"))
        assertFalse(Duplicates.same("https://example.com/a.zip?v=1", "https://example.com/a.zip?v=2"))
        assertEquals("https://h.test/", Duplicates.normalize("https://H.test"))
    }
}

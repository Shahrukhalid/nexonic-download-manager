package com.nexonic.downloadmanager.core

import java.net.URI
import java.time.LocalDateTime
import java.time.LocalTime

/**
 * Speed limits by time of day, one rule per line ("Mon-Fri 09:00-17:00 300" = 300 KB/s on weekdays during office
 * hours). The first rule that applies wins; outside every rule the normal limit applies. Same format as the desktop apps.
 */
object SpeedSchedule {
    data class Rule(val days: BooleanArray, val start: LocalTime, val end: LocalTime, val kbps: Int)
    data class Parsed(val rules: List<Rule>, val problem: String?)

    private val dayNames = listOf("sun", "mon", "tue", "wed", "thu", "fri", "sat")

    fun parse(text: String?): Parsed {
        val rules = mutableListOf<Rule>()
        var n = 0
        for (raw in (text ?: "").split('\n')) {
            n++
            val line = raw.trim()
            if (line.isEmpty() || line.startsWith("#")) continue
            val p = line.split(Regex("\\s+"))
            val days = if (p.size == 3) days(p[0]) else null
            val times = if (p.size == 3) times(p[1]) else null
            val kb = if (p.size == 3) p[2].toIntOrNull() else null
            if (days == null || times == null || kb == null || kb < 0)
                return Parsed(rules, "Line $n: write it as days start-end KB/s, for example Mon-Fri 09:00-17:00 300.")
            rules += Rule(days, times.first, times.second, minOf(kb, 10_000_000))
            if (rules.size == 50) break
        }
        return Parsed(rules, null)
    }

    private fun days(s: String): BooleanArray? {
        val d = BooleanArray(7)
        for (part in s.lowercase().split(',')) {
            when (part) {
                "daily", "every", "all" -> { d.fill(true); continue }
                "weekdays" -> { for (i in 1..5) d[i] = true; continue }
                "weekends" -> { d[0] = true; d[6] = true; continue }
            }
            val range = part.split('-')
            if (range.size > 2) return null
            val a = dayNames.indexOf(range[0].take(3).takeIf { range[0].length >= 3 } ?: "")
            val b = if (range.size == 2) dayNames.indexOf(range[1].take(3).takeIf { range[1].length >= 3 } ?: "") else a
            if (a < 0 || b < 0) return null
            var i = a
            while (true) { d[i] = true; if (i == b) break; i = (i + 1) % 7 }
        }
        return d
    }

    private fun times(s: String): Pair<LocalTime, LocalTime>? {
        val p = s.split('-')
        if (p.size != 2) return null
        fun t(x: String) = Regex("^(\\d{2}):(\\d{2})$").find(x)?.let {
            val h = it.groupValues[1].toInt(); val m = it.groupValues[2].toInt()
            if (h < 24 && m < 60) LocalTime.of(h, m) else null
        }
        val a = t(p[0]) ?: return null
        val b = t(p[1]) ?: return null
        return if (a != b) a to b else null
    }

    /** The limit the schedule sets at this local time (KB/s, 0 = no limit), or null when no rule applies. */
    fun limitAt(rules: List<Rule>, local: LocalDateTime): Int? {
        val day = local.dayOfWeek.value % 7  // Sunday = 0
        val t = local.toLocalTime()
        for (r in rules) {
            if (r.start < r.end) { if (r.days[day] && t >= r.start && t < r.end) return r.kbps }
            else if ((r.days[day] && t >= r.start) || (r.days[(day + 6) % 7] && t < r.end)) return r.kbps  // passes midnight
        }
        return null
    }
}

/**
 * Downloads that would repeat one you have: the same link in the list (still to do, or finished with its file still
 * there). The same address written differently (case of the scheme and host, a #fragment) counts as the same.
 */
object Duplicates {
    fun normalize(url: String): String {
        val u = runCatching { URI(url.trim()) }.getOrNull() ?: return url.trim()
        if (u.scheme == null || u.host == null) return url.trim()
        val auth = (u.rawUserInfo?.let { "$it@" } ?: "") + u.host.lowercase() + (if (u.port >= 0) ":${u.port}" else "")
        return u.scheme.lowercase() + "://" + auth + (u.rawPath?.ifEmpty { "/" } ?: "/") + (u.rawQuery?.let { "?$it" } ?: "")
    }

    fun same(a: String, b: String) = normalize(a) == normalize(b)
}

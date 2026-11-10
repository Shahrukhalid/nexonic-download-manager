package com.nexonic.downloadmanager.core

import java.net.URI
import java.util.Base64

/**
 * cURL commands in and out: a browser's "Copy as cURL" (bash or Windows cmd) becomes a download with the same address,
 * referrer, cookies, browser name and login; a download can be copied as a cURL command to fetch it elsewhere.
 * The same rules as the desktop apps.
 */
object CurlCommand {
    data class Request(
        val url: String, val referrer: String?, val cookies: String?, val userAgent: String?,
        val username: String?, val password: String?,
        /** Parts of the command a download cannot repeat (named for the user). */
        val ignored: List<String>,
    )

    /** Either a request or why the command cannot be repeated. */
    data class Parsed(val request: Request?, val problem: String?)

    private class Problem(message: String) : Exception(message)

    fun looks(text: String?): Boolean {
        val t = text?.trimStart()?.lowercase() ?: return false
        return t.startsWith("curl ") || t.startsWith("curl.exe ")
    }

    fun parse(text: String): Parsed {
        val args = try { split(text) } catch (e: Problem) { return Parsed(null, e.message) }
        if (args.isEmpty() || args[0].lowercase() !in setOf("curl", "curl.exe")) return Parsed(null, "This is not a cURL command.")
        var url: String? = null; var referrer: String? = null; var agent: String? = null
        var user: String? = null; var pass: String? = null
        val cookies = mutableListOf<String>(); val ignored = mutableListOf<String>()
        var i = 1
        fun next(a: String): String { i++; if (i >= args.size) throw Problem("$a needs a value."); return args[i] }
        try {
            while (i < args.size) {
                val a = args[i]
                when (a) {
                    "-H", "--header" -> {
                        val h = next(a)
                        val colon = h.indexOf(':')
                        if (colon > 0) {
                            val name = h.substring(0, colon).trim()
                            val value = h.substring(colon + 1).trim()
                            when (name.lowercase()) {
                                "referer" -> referrer = value
                                "cookie" -> cookies += value
                                "user-agent" -> agent = value
                                "authorization" -> {
                                    val pair = if (value.lowercase().startsWith("basic "))
                                        runCatching { String(Base64.getDecoder().decode(value.substring(6).trim())) }.getOrNull() else null
                                    val c = pair?.indexOf(':') ?: -1
                                    if (pair != null && c > 0) { user = pair.substring(0, c); pass = pair.substring(c + 1) } else ignored += name
                                }
                                // Sent by every request anyway, or describing the browser rather than the file.
                                "accept", "accept-language", "accept-encoding", "connection", "cache-control", "pragma",
                                "upgrade-insecure-requests", "dnt", "priority", "te", "range" -> {}
                                else -> if (!name.lowercase().startsWith("sec-")) ignored += name
                            }
                        }
                    }
                    "-b", "--cookie" -> { val c = next(a); if ('=' in c) cookies += c else ignored += "cookie file" }
                    "-A", "--user-agent" -> agent = next(a)
                    "-e", "--referer" -> referrer = next(a).split(";auto")[0]
                    "-u", "--user" -> {
                        val up = next(a); val k = up.indexOf(':')
                        if (k >= 0) { user = up.substring(0, k); pass = up.substring(k + 1) } else { user = up; pass = "" }
                    }
                    "--url" -> url = next(a)
                    "-d", "--data", "--data-raw", "--data-binary", "--data-urlencode", "-F", "--form", "--json" ->
                        return Parsed(null, "This command sends data (a form or POST request); downloads can only repeat plain requests.")
                    "-X", "--request" -> {
                        val m = next(a)
                        if (!m.equals("GET", true)) return Parsed(null, "This command uses ${m.uppercase()}; downloads can only repeat GET requests.")
                    }
                    // Options that take a value but do not change what is downloaded.
                    "-o", "--output", "--max-time", "-m", "--connect-timeout", "--retry", "-w", "--write-out",
                    "--limit-rate", "-x", "--proxy", "-r", "--range", "-c", "--cookie-jar" -> next(a)
                    else -> if (!a.startsWith("-") && url == null) url = a  // switches such as --compressed, -L, -s are skipped
                }
                i++
            }
        } catch (e: Problem) { return Parsed(null, e.message) }
        val u = url?.let { runCatching { URI(it) }.getOrNull() }
        if (u == null || u.scheme?.lowercase() !in setOf("http", "https", "ftp") || u.host.isNullOrEmpty())
            return Parsed(null, "The cURL command has no web address.")
        val seen = HashSet<String>()
        return Parsed(Request(url!!, referrer, cookies.takeIf { it.isNotEmpty() }?.joinToString("; "), agent, user, pass,
            ignored.filter { seen.add(it.lowercase()) }), null)
    }

    /** A bash cURL command that fetches the same file (with its cookies and login: share it with care). */
    fun command(url: String, fileName: String, referrer: String?, userAgent: String?, cookies: String?, user: String?, password: String?): String {
        val s = StringBuilder("curl -L -C - -o ").append(quote(fileName.ifEmpty { "download" }))
        if (!referrer.isNullOrEmpty()) s.append(" -e ").append(quote(referrer))
        if (!userAgent.isNullOrEmpty()) s.append(" -A ").append(quote(userAgent))
        if (!cookies.isNullOrEmpty()) s.append(" -b ").append(quote(cookies))
        if (!user.isNullOrEmpty()) s.append(" -u ").append(quote(user + ":" + password.orEmpty()))
        return s.append(' ').append(quote(url)).toString()
    }

    private fun quote(s: String) = "'" + s.replace("'", "'\\''") + "'"

    /** Words of a command as a shell reads them: bash quoting ('…', "…", $'…', \ and line continuations) and Windows cmd
     *  quoting ("…", ^ escapes and ^ line continuations, as browsers copy it). */
    fun split(text: String): List<String> {
        val words = mutableListOf<String>()
        val cur = StringBuilder()
        var inWord = false
        val cmd = "^\n" in text || "^\r\n" in text || "^\"" in text
        var i = 0
        fun skipNewline(): Boolean {  // at text[i] = escape char: a line continuation follows?
            if (i + 1 < text.length && (text[i + 1] == '\r' || text[i + 1] == '\n')) {
                i++; if (text[i] == '\r' && i + 1 < text.length && text[i + 1] == '\n') i++
                return true
            }
            return false
        }
        while (i < text.length) {
            var ch = text[i]
            if (cmd && ch == '^') {
                if (skipNewline()) { i++; continue }
                if (i + 1 < text.length && text[i + 1] == '"') { i++; ch = '"' }
                else { if (i + 1 < text.length) { cur.append(text[++i]); inWord = true }; i++; continue }
            }
            if (!cmd && ch == '\\') {
                if (skipNewline()) { i++; continue }
                if (i + 1 < text.length) { cur.append(text[++i]); inWord = true }
                i++; continue
            }
            if (ch.isWhitespace()) {
                if (inWord) { words += cur.toString(); cur.clear(); inWord = false }
                i++; continue
            }
            inWord = true
            if (!cmd && ch == '$' && i + 1 < text.length && text[i + 1] == '\'') {
                i += 2
                while (i < text.length && text[i] != '\'') {
                    if (text[i] == '\\' && i + 1 < text.length) {
                        cur.append(when (val e = text[++i]) { 'n' -> '\n'; 't' -> '\t'; 'r' -> '\r'; else -> e })
                    } else cur.append(text[i])
                    i++
                }
                if (i >= text.length) throw Problem("A quote is not closed.")
                i++; continue
            }
            if (!cmd && ch == '\'') {
                val end = text.indexOf('\'', i + 1)
                if (end < 0) throw Problem("A quote is not closed.")
                cur.append(text, i + 1, end)
                i = end + 1; continue
            }
            if (ch == '"') {
                i++
                var closed = false
                while (i < text.length) {
                    if (text[i] == '"') { closed = true; break }
                    if (cmd && text[i] == '^' && i + 1 < text.length) {
                        if (text[i + 1] == '"') { i++; closed = true; break }  // ^" closes what ^" opened
                        cur.append(text[++i]); i++; continue
                    }
                    if (!cmd && text[i] == '\\' && i + 1 < text.length && text[i + 1] in "\"\\$`") { cur.append(text[++i]); i++; continue }
                    cur.append(text[i]); i++
                }
                if (!closed) throw Problem("A quote is not closed.")
                i++; continue
            }
            cur.append(ch); i++
        }
        if (inWord) words += cur.toString()
        return words
    }
}

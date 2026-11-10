package com.nexonic.downloadmanager.core

import java.net.URI
import java.net.URLDecoder

/**
 * A category of the user's own (Settings → Folders by type): a name, an icon, file types ("cbz cbr"), sites whose files
 * belong here, and a folder inside Downloads/Nexonic (empty = the category's name). The user's categories are checked in
 * their order, before the built-in types.
 */
data class CustomCategory(val name: String, val extensions: String = "", val folder: String = "", val icon: String = "", val sites: String = "") {

    /** The file types in the order written, lowercase, without dots. */
    val typeList: List<String>
        get() = extensions.lowercase().split(' ', ',', ';').map { it.trimStart('.', '*') }.filter { it.isNotEmpty() }.distinct()

    private val siteList: List<String> get() = sites.split(Regex("\\s+")).map(::sitePattern).filter { it.isNotEmpty() }

    /** True when a file of this type (lowercase, no dot) from this address belongs here. With sites and no types, every
     *  file from those sites does. */
    fun takes(ext: String, url: String?): Boolean {
        val types = typeList
        if (types.isNotEmpty() && (ext.isEmpty() || ext !in types)) return false
        val list = siteList
        if (list.isEmpty()) return types.isNotEmpty()
        return url != null && siteMatches(list, url)
    }

    /** The subfolder of Downloads/Nexonic for this category. */
    val subfolder: String
        get() {
            val f = folder.trim().trim('/')
            if (f.split('/').any { it == ".." || it == "." }) return FileNames.sanitize(name)
            return f.split('/').filter { it.isNotBlank() }.map(FileNames::sanitize).ifEmpty { listOf(FileNames.sanitize(name)) }.joinToString("/")
        }

    companion object {
        /** True when the address matches a site pattern ("example.com" also covers its subdomains, * matches anything). */
        fun siteMatches(patterns: List<String>, url: String): Boolean {
            val u = runCatching { URI(url) }.getOrNull() ?: return false
            if (u.scheme?.lowercase() !in setOf("http", "https", "ftp")) return false
            val host = u.host?.lowercase()?.trimEnd('.') ?: return false
            val rawPath = u.rawPath ?: ""
            val path = runCatching { URLDecoder.decode(rawPath.replace("+", "%2B"), "UTF-8") }.getOrDefault(rawPath).lowercase()
            for (p in patterns) {
                if (p.isEmpty()) continue
                val slash = p.indexOf('/')
                val hp = if (slash < 0) p else p.substring(0, slash)
                val pp = if (slash < 0) null else p.substring(slash)
                val hostOk = if ('*' in hp) glob(hp, host) else host == hp || host.endsWith(".$hp")
                if (hostOk && (pp == null || glob(pp, path) || glob(pp.trimEnd('/') + "/*", path))) return true
            }
            return false
        }

        /** A pattern as kept: lowercase, without "https://" and without a query; "" when it cannot be used. */
        fun sitePattern(raw: String): String {
            var p = raw.trim().lowercase()
            val scheme = p.indexOf("://")
            if (scheme >= 0) p = p.substring(scheme + 3)
            val q = p.indexOfFirst { it == '?' || it == '#' }
            if (q >= 0) p = p.substring(0, q)
            return if (p.length <= 200 && p.all { it > ' ' && it != '"' && it != '\\' }) p else ""
        }

        private fun glob(pattern: String, text: String): Boolean =
            Regex("^" + pattern.split('*').joinToString(".*") { Regex.escape(it) } + "$").matches(text)

        /** A leading symbol of a name ("📚 Books" → "📚", "Books"): one emoji or sign, not a letter or digit. */
        fun splitIcon(name: String): Pair<String, String> {
            val n = name.trim()
            val space = n.indexOf(' ')
            if (space <= 0 || space > 8) return "" to n
            val first = n.substring(0, space)
            val singleSymbol = first.codePointCount(0, first.length).let { it in 1..4 } && first.none { it.isLetterOrDigit() }
            return if (singleSymbol) first to n.substring(space + 1).trim() else "" to n
        }

        /** The Settings text: one category per line, "[icon] Name: types @ sites > folder". */
        fun toLines(list: List<CustomCategory>): String = list.joinToString("\n") { c ->
            (if (c.icon.isNotEmpty()) c.icon + " " else "") + "${c.name}: ${c.extensions}" +
                (if (c.sites.isNotEmpty()) (if (c.extensions.isNotEmpty()) " @ " else "@ ") + c.sites else "") +
                (if (c.folder.isNotEmpty()) " > " + c.folder else "")
        }

        /** Reads the Settings text: the categories, or a message naming the first line that cannot be read. */
        fun parse(text: String?): Pair<List<CustomCategory>, String?> {
            val list = mutableListOf<CustomCategory>()
            for ((i, raw) in (text ?: "").split('\n').withIndex()) {
                val line = raw.trim()
                if (line.isEmpty() || line.startsWith("#")) continue
                val colon = line.indexOf(": ").takeIf { it > 0 } ?: (line.length - 1).takeIf { line.endsWith(":") && it > 0 }
                    ?: return list to "Line ${i + 1}: write it as Name: types > folder (for example Comics: cbz cbr > Comics)."
                val rest = line.substring(colon + 1)
                val arrow = rest.indexOf('>')
                val match = if (arrow >= 0) rest.substring(0, arrow) else rest
                val at = match.indexOf('@')
                val (icon, name) = splitIcon(line.substring(0, colon))
                val c = CustomCategory(
                    name = name, icon = icon,
                    extensions = (if (at >= 0) match.substring(0, at) else match).trim(),
                    sites = if (at >= 0) match.substring(at + 1).trim() else "",
                    folder = if (arrow >= 0) rest.substring(arrow + 1).trim() else "",
                )
                if (c.typeList.isEmpty() && c.sites.isEmpty()) return list to "Line ${i + 1}: list at least one file type or site for ${c.name}."
                if (c.sites.split(Regex("\\s+")).any { it.isNotEmpty() && sitePattern(it).isEmpty() })
                    return list to "Line ${i + 1}: a site of ${c.name} cannot be read (write it like example.com or github.com/*/releases)."
                list += c
            }
            return clean(list) to null
        }

        /** At most 50 categories with a name and a type or site; a type goes to the first category that lists it unless
         *  the category is limited to some sites. */
        fun clean(list: List<CustomCategory>): List<CustomCategory> {
            val seen = mutableSetOf<String>()
            val result = mutableListOf<CustomCategory>()
            for (c in list) {
                val sites = c.siteList.distinct().take(20)
                val types = c.typeList.filter { it.length <= 16 && it.all(Char::isLetterOrDigit) && (sites.isNotEmpty() || seen.add(it)) }
                if (c.name.isBlank() || (types.isEmpty() && sites.isEmpty())) continue
                result += CustomCategory(c.name.trim(), types.joinToString(" "), c.folder.trim(), splitIcon(c.icon + " x").first, sites.joinToString(" "))
                if (result.size == 50) break
            }
            return result
        }

        /** The subfolder of Downloads/Nexonic for a file: the user's first matching category, else the built-in type
         *  folder when sorting is on, else none. */
        fun subfolderFor(categories: List<CustomCategory>, fileName: String, url: String?, sortBuiltIn: Boolean): String? {
            val ext = FileNames.extensionOf(fileName)
            categories.firstOrNull { it.takes(ext, url) }?.let { return it.subfolder }
            return if (sortBuiltIn) FileCategory.of(fileName).folder else null
        }
    }
}

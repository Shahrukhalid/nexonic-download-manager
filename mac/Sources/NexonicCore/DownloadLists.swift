import Foundation

/// Reads download lists saved by other download managers, so switching keeps the downloads you had (the same rules as
/// DownloadLists.cs on Windows and Linux). Recognises angle-bracket lists ("<", the address, "name: value" lines, ">"),
/// input files (an address line followed by indented "option=value" lines), JSON lists of objects with url/link/href,
/// web pages and bookmark files (every link), and anything else (every address in the text).
/// Folders from the other computer are not used, file names never carry a path, and only web addresses are kept.
public enum DownloadLists {
    public static let maxEntries = 20000
    public static let maxBytes = 32 * 1024 * 1024

    public enum Format: Equatable { case blocks, inputFile, json, html, text }

    public static func read(_ input: String) -> (jobs: [DownloadJob], format: Format) {
        var text = input
        if text.utf8.count > maxBytes { text = String(text.prefix(maxBytes)) }
        let t = String(text.drop(while: { $0 == "\u{FEFF}" || $0.isWhitespace }))
        let format = detect(t)
        let found: [DownloadJob]
        switch format {
        case .blocks: found = blocks(t)
        case .inputFile: found = inputFile(t)
        case .json: found = json(t)
        case .html: found = html(t)
        case .text: found = Links.split(t).map { DownloadJob(url: $0) }
        }
        var seen = Set<String>()
        var kept: [DownloadJob] = []
        for job in found where isWeb(job.url) && !seen.contains(job.url) {
            seen.insert(job.url)
            kept.append(job)
            if kept.count >= maxEntries { break }
        }
        return (kept, format)
    }

    static func detect(_ t: String) -> Format {
        if t.hasPrefix("<"), matches(t, #"^<\s*\r?\n\s*\S+://"#) { return .blocks }
        if t.hasPrefix("[") || t.hasPrefix("{") { return .json }
        if matches(t, #"(?i)<a\s[^>]*href\s*="#) { return .html }
        if matches(t, #"(?m)^[ \t]+[a-z][a-z0-9-]*="#) { return .inputFile }
        return .text
    }

    static func matches(_ text: String, _ pattern: String) -> Bool {
        text.range(of: pattern, options: .regularExpression) != nil
    }

    static func isWeb(_ s: String) -> Bool {
        guard let u = URL(string: s), let scheme = u.scheme?.lowercased(), let host = u.host, !host.isEmpty else { return false }
        return scheme == "http" || scheme == "https"
    }

    static func value(_ s: Substring) -> String? {
        let v = s.trimmingCharacters(in: .whitespaces)
        return v.isEmpty ? nil : v
    }

    /// A safe file name: the last part of whatever path the other app stored, cleaned like any other name.
    static func name(_ raw: String?) -> String? {
        guard let raw = raw?.trimmingCharacters(in: .whitespaces), !raw.isEmpty else { return nil }
        let unified = raw.replacingOccurrences(of: "\\", with: "/")
        let parts = unified.split(separator: "/", omittingEmptySubsequences: true)
        guard let last = parts.last else { return nil }
        let clean = FileNames.sanitize(String(last))
        if clean == "." || clean == ".." { return nil }
        return clean
    }

    static func lines(_ text: String) -> [Substring] {
        text.split(separator: "\n", omittingEmptySubsequences: false).map { line in
            line.hasSuffix("\r") ? line.dropLast() : line
        }
    }

    static func blocks(_ text: String) -> [DownloadJob] {
        var jobs: [DownloadJob] = []
        var block: [Substring]? = nil
        for line in lines(text) {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed == "<" { block = []; continue }
            if trimmed == ">" {
                if let b = block, !b.isEmpty { jobs.append(fromBlock(b)) }
                block = nil
                continue
            }
            block?.append(line)
        }
        return jobs
    }

    static func fromBlock(_ lines: [Substring]) -> DownloadJob {
        var fields: [String: String] = [:]
        for line in lines.dropFirst() {
            guard let colon = line.firstIndex(of: ":") else { continue }
            let key = line[..<colon].trimmingCharacters(in: .whitespaces).lowercased()
            if key.isEmpty { continue }
            if let v = value(line[line.index(after: colon)...]) { fields[key] = v }
        }
        let url = lines[0].trimmingCharacters(in: .whitespaces)
        var job = DownloadJob(url: url, fileName: name(fields["filename"] ?? fields["name"]),
                              referrer: fields["referer"] ?? fields["referrer"], cookies: fields["cookie"], userAgent: fields["user-agent"])
        if let user = fields["username"] {
            job.username = user
            job.password = fields["password"] ?? ""
        }
        return job
    }

    static func inputFile(_ text: String) -> [DownloadJob] {
        var jobs: [DownloadJob] = []
        var urls: [String] = []
        var options: [String: String] = [:]
        var cookies: [String] = []
        func flush() {
            if let first = urls.first {
                var job = DownloadJob(url: first, fileName: name(options["out"]), referrer: options["referer"],
                                      cookies: cookies.isEmpty ? nil : cookies.joined(separator: "; "), userAgent: options["user-agent"])
                if let user = options["http-user"], !user.isEmpty {
                    job.username = user
                    job.password = options["http-passwd"] ?? ""
                }
                if let sum = options["checksum"], let hex = sha256(sum) { job.checksum = hex }
                jobs.append(job)
            }
            urls = []; options = [:]; cookies = []
        }
        for line in lines(text) {
            if line.isEmpty || line.trimmingCharacters(in: .whitespaces).hasPrefix("#") { continue }
            if let c = line.first, c == " " || c == "\t" {
                if urls.isEmpty { continue }
                guard let eq = line.firstIndex(of: "=") else { continue }
                let key = line[..<eq].trimmingCharacters(in: .whitespaces).lowercased()
                let val = line[line.index(after: eq)...].trimmingCharacters(in: .whitespaces)
                if key == "header" {
                    if let colon = val.firstIndex(of: ":") {
                        let h = val[..<colon].trimmingCharacters(in: .whitespaces).lowercased()
                        let v = val[val.index(after: colon)...].trimmingCharacters(in: .whitespaces)
                        if h == "cookie" { cookies.append(v) } else if h == "referer" { options["referer"] = v }
                    }
                } else {
                    options[key] = val
                }
                continue
            }
            flush()
            urls = line.split(separator: "\t").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        }
        flush()
        return jobs
    }

    /// "sha-256=<64 hex digits>" (or "sha256=…") → the lowercase digest.
    static func sha256(_ s: String) -> String? {
        let lower = s.lowercased()
        for prefix in ["sha-256=", "sha256="] where lower.hasPrefix(prefix) {
            let hex = String(lower.dropFirst(prefix.count))
            if hex.count == 64, hex.allSatisfy({ $0.isHexDigit }) { return hex }
        }
        return nil
    }

    static let urlKeys = ["url", "link", "href", "uri", "address", "source"]
    static let nameKeys = ["filename", "file", "name", "title"]

    static func json(_ text: String) -> [DownloadJob] {
        guard let data = text.data(using: .utf8), let root = try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed]) else {
            return Links.split(text).map { DownloadJob(url: $0) }
        }
        var jobs: [DownloadJob] = []
        walk(root, &jobs, 0)
        return jobs
    }

    static func string(_ object: [String: Any], _ keys: [String]) -> String? {
        for (k, v) in object {
            guard keys.contains(k.lowercased()), let s = v as? String else { continue }
            let t = s.trimmingCharacters(in: .whitespaces)
            if !t.isEmpty { return t }
        }
        return nil
    }

    static func walk(_ node: Any, _ jobs: inout [DownloadJob], _ depth: Int) {
        if depth > 8 || jobs.count >= maxEntries { return }
        if let array = node as? [Any] {
            for x in array { walk(x, &jobs, depth + 1) }
            return
        }
        guard let object = node as? [String: Any] else { return }
        if let url = string(object, urlKeys) {
            let referrer = string(object, ["referrer", "referer"])
            jobs.append(DownloadJob(url: url, fileName: name(string(object, nameKeys)), referrer: referrer))
            return
        }
        for (_, v) in object where v is [Any] || v is [String: Any] { walk(v, &jobs, depth + 1) }
    }

    static func html(_ text: String) -> [DownloadJob] {
        guard let re = try? NSRegularExpression(pattern: #"<a\s[^>]*?href\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))"#, options: [.caseInsensitive]) else { return [] }
        let ns = text as NSString
        var jobs: [DownloadJob] = []
        for m in re.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            var link = ""
            for g in 1...3 where m.range(at: g).location != NSNotFound {
                link = ns.substring(with: m.range(at: g))
                break
            }
            jobs.append(DownloadJob(url: decodeEntities(link).trimmingCharacters(in: .whitespaces)))
        }
        return jobs
    }

    static func decodeEntities(_ s: String) -> String {
        s.replacingOccurrences(of: "&quot;", with: "\"").replacingOccurrences(of: "&#39;", with: "'")
            .replacingOccurrences(of: "&lt;", with: "<").replacingOccurrences(of: "&gt;", with: ">")
            .replacingOccurrences(of: "&amp;", with: "&")
    }
}

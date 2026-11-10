import Foundation

/// cURL commands in and out: a browser's "Copy as cURL" (bash or Windows cmd) becomes a download with the same address,
/// referrer, cookies, browser name and login; a download can be copied as a cURL command to fetch it elsewhere.
/// The same rules as the Windows and Linux apps.
public enum CurlCommand {
    public struct Request: Equatable, Sendable {
        public var url: URL
        public var referrer: String?
        public var cookies: String?
        public var userAgent: String?
        public var username: String?
        public var password: String?
        /// Parts of the command a download cannot repeat (named for the user).
        public var ignored: [String]
    }

    public enum Failure: Error, Equatable, CustomStringConvertible {
        case problem(String)
        public var description: String { if case let .problem(p) = self { return p }; return "" }
    }

    public static func looks(_ text: String?) -> Bool {
        guard let t = text?.drop(while: { $0.isWhitespace }).lowercased() else { return false }
        return t.hasPrefix("curl ") || t.hasPrefix("curl.exe ")
    }

    /// Reads a cURL command; the failure says why it is not one a download can repeat.
    public static func parse(_ text: String) -> Result<Request, Failure> {
        let args: [String]
        do { args = try split(text) } catch let f as Failure { return .failure(f) } catch { return .failure(.problem("This is not a cURL command.")) }
        guard let first = args.first, ["curl", "curl.exe"].contains(first.lowercased()) else { return .failure(.problem("This is not a cURL command.")) }
        var url: String?, referrer: String?, agent: String?, user: String?, pass: String?
        var cookies: [String] = [], ignored: [String] = []
        var i = 1
        func next(_ a: String) throws -> String {
            i += 1
            guard i < args.count else { throw Failure.problem("\(a) needs a value.") }
            return args[i]
        }
        do {
            while i < args.count {
                let a = args[i]
                switch a {
                case "-H", "--header":
                    let h = try next(a)
                    guard let colon = h.firstIndex(of: ":"), colon != h.startIndex else { break }
                    let name = h[..<colon].trimmingCharacters(in: .whitespaces)
                    let value = h[h.index(after: colon)...].trimmingCharacters(in: .whitespaces)
                    switch name.lowercased() {
                    case "referer": referrer = value
                    case "cookie": cookies.append(value)
                    case "user-agent": agent = value
                    case "authorization" where value.lowercased().hasPrefix("basic "):
                        if let data = Data(base64Encoded: String(value.dropFirst(6)).trimmingCharacters(in: .whitespaces)),
                           let pair = String(data: data, encoding: .utf8), let c = pair.firstIndex(of: ":"), c != pair.startIndex {
                            user = String(pair[..<c]); pass = String(pair[pair.index(after: c)...])
                        } else { ignored.append(name) }
                    // Sent by every request anyway, or describing the browser rather than the file.
                    case "accept", "accept-language", "accept-encoding", "connection", "cache-control", "pragma",
                         "upgrade-insecure-requests", "dnt", "priority", "te", "range":
                        break
                    default:
                        if !name.lowercased().hasPrefix("sec-") { ignored.append(name) }
                    }
                case "-b", "--cookie":
                    let c = try next(a)
                    if c.contains("=") { cookies.append(c) } else { ignored.append("cookie file") }
                case "-A", "--user-agent": agent = try next(a)
                case "-e", "--referer": referrer = try next(a).components(separatedBy: ";auto")[0]
                case "-u", "--user":
                    let up = try next(a)
                    if let k = up.firstIndex(of: ":") { user = String(up[..<k]); pass = String(up[up.index(after: k)...]) } else { user = up; pass = "" }
                case "--url": url = try next(a)
                case "-d", "--data", "--data-raw", "--data-binary", "--data-urlencode", "-F", "--form", "--json":
                    return .failure(.problem("This command sends data (a form or POST request); downloads can only repeat plain requests."))
                case "-X", "--request":
                    let method = try next(a)
                    if method.uppercased() != "GET" {
                        return .failure(.problem("This command uses \(method.uppercased()); downloads can only repeat GET requests."))
                    }
                // Options that take a value but do not change what is downloaded.
                case "-o", "--output", "--max-time", "-m", "--connect-timeout", "--retry", "-w", "--write-out",
                     "--limit-rate", "-x", "--proxy", "-r", "--range", "-c", "--cookie-jar":
                    _ = try next(a)
                default:
                    if !a.hasPrefix("-"), url == nil { url = a }  // switches such as --compressed, -L, -s are skipped
                }
                i += 1
            }
        } catch let f as Failure { return .failure(f) } catch { return .failure(.problem("This is not a cURL command.")) }
        guard let u = url, let parsed = URL(string: u), let scheme = parsed.scheme?.lowercased(), ["http", "https", "ftp"].contains(scheme),
              parsed.host != nil else {
            return .failure(.problem("The cURL command has no web address."))
        }
        var seen = Set<String>()
        let unique = ignored.filter { seen.insert($0.lowercased()).inserted }
        return .success(Request(url: parsed, referrer: referrer, cookies: cookies.isEmpty ? nil : cookies.joined(separator: "; "),
                                userAgent: agent, username: user, password: pass, ignored: unique))
    }

    /// A bash cURL command that fetches the same file (with its cookies and login: share it with care).
    public static func command(for r: DownloadRecord) -> String {
        var s = "curl -L -C - -o " + quote(r.fileName.isEmpty ? "download" : r.fileName)
        if let v = r.referrer, !v.isEmpty { s += " -e " + quote(v) }
        if let v = r.userAgent, !v.isEmpty { s += " -A " + quote(v) }
        if let v = r.cookies, !v.isEmpty { s += " -b " + quote(v) }
        if let v = r.username, !v.isEmpty { s += " -u " + quote(v + ":" + (r.password ?? "")) }
        return s + " " + quote(r.url.absoluteString)
    }

    static func quote(_ s: String) -> String { "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }

    /// Words of a command as a shell reads them: bash quoting ('…', "…", $'…', \ and line continuations) and Windows cmd
    /// quoting ("…", ^ escapes and ^ line continuations, as browsers copy it).
    public static func split(_ text: String) throws -> [String] {
        let t = Array(text)
        var words: [String] = [], cur = "", inWord = false
        let cmd = text.contains("^\n") || text.contains("^\r\n") || text.contains("^\"")
        var i = 0
        func isNewline(_ c: Character) -> Bool { c == "\n" || c == "\r" || c == "\r\n" }
        while i < t.count {
            var ch = t[i]
            if cmd && ch == "^" {
                if i + 1 < t.count, isNewline(t[i + 1]) { i += 2; continue }
                if i + 1 < t.count, t[i + 1] == "\"" { i += 1; ch = "\"" } else {
                    if i + 1 < t.count { i += 1; cur.append(t[i]); inWord = true }
                    i += 1; continue
                }
            }
            if !cmd && ch == "\\" {
                if i + 1 < t.count, isNewline(t[i + 1]) { i += 2; continue }
                if i + 1 < t.count { i += 1; cur.append(t[i]); inWord = true }
                i += 1; continue
            }
            if ch.isWhitespace {
                if inWord { words.append(cur); cur = ""; inWord = false }
                i += 1; continue
            }
            inWord = true
            if !cmd && ch == "$", i + 1 < t.count, t[i + 1] == "'" {
                i += 2
                while i < t.count && t[i] != "'" {
                    if t[i] == "\\" && i + 1 < t.count {
                        i += 1
                        switch t[i] { case "n": cur.append("\n"); case "t": cur.append("\t"); case "r": cur.append("\r"); default: cur.append(t[i]) }
                    } else { cur.append(t[i]) }
                    i += 1
                }
                if i >= t.count { throw Failure.problem("A quote is not closed.") }
                i += 1; continue
            }
            if !cmd && ch == "'" {
                guard let end = t[(i + 1)...].firstIndex(of: "'") else { throw Failure.problem("A quote is not closed.") }
                cur += String(t[(i + 1)..<end])
                i = end + 1; continue
            }
            if ch == "\"" {
                i += 1
                var closed = false
                while i < t.count {
                    if t[i] == "\"" { closed = true; break }
                    if cmd && t[i] == "^" && i + 1 < t.count {
                        if t[i + 1] == "\"" { i += 1; closed = true; break }  // ^" closes what ^" opened
                        i += 1; cur.append(t[i]); i += 1; continue
                    }
                    if !cmd && t[i] == "\\" && i + 1 < t.count && "\"\\$`".contains(t[i + 1]) { i += 1; cur.append(t[i]); i += 1; continue }
                    cur.append(t[i]); i += 1
                }
                if !closed { throw Failure.problem("A quote is not closed.") }
                i += 1; continue
            }
            cur.append(ch); i += 1
        }
        if inWord { words.append(cur) }
        return words
    }
}

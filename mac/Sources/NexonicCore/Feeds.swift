import Foundation

/// An RSS or Atom feed whose new items are downloaded when they match the rules (same rules as the Windows and Linux apps).
public struct FeedSubscription: Codable, Equatable, Identifiable, Sendable {
    public var id = UUID()
    public var name = ""
    public var url = ""
    public var enabled = true
    /// Minutes between checks (15 or more).
    public var intervalMinutes = 30
    /// Words that must all be in the title (empty = every item), or /regular expression/.
    public var include = ""
    /// Items whose title has any of these words (or matches /regular expression/) are skipped.
    public var exclude = ""
    /// Save folder ("" = the usual folder).
    public var folder = ""
    /// Only items published after the feed was added are downloaded; earlier ones are only remembered.
    public var skipExisting = true
    public var lastChecked: Date?
    public var lastResult = ""
    public var seen: [String] = []
    /// Size limits in MB (0 = none), preferred qualities ("1080p 720p"), one download per episode.
    public var minSizeMB = 0
    public var maxSizeMB = 0
    public var quality = ""
    public var oneOfEachEpisode = false
    public var episodes: [String] = []
    public init() {}

    enum CodingKeys: String, CodingKey {
        case id, name, url, enabled, intervalMinutes, include, exclude, folder, skipExisting, lastChecked, lastResult, seen,
             minSizeMB, maxSizeMB, quality, oneOfEachEpisode, episodes
    }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = FeedSubscription()
        id = try c.decodeIfPresent(UUID.self, forKey: .id) ?? UUID()
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? ""
        url = try c.decodeIfPresent(String.self, forKey: .url) ?? ""
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? true
        intervalMinutes = try c.decodeIfPresent(Int.self, forKey: .intervalMinutes) ?? d.intervalMinutes
        include = try c.decodeIfPresent(String.self, forKey: .include) ?? ""
        exclude = try c.decodeIfPresent(String.self, forKey: .exclude) ?? ""
        folder = try c.decodeIfPresent(String.self, forKey: .folder) ?? ""
        skipExisting = try c.decodeIfPresent(Bool.self, forKey: .skipExisting) ?? true
        lastChecked = try c.decodeIfPresent(Date.self, forKey: .lastChecked)
        lastResult = try c.decodeIfPresent(String.self, forKey: .lastResult) ?? ""
        seen = try c.decodeIfPresent([String].self, forKey: .seen) ?? []
        minSizeMB = try c.decodeIfPresent(Int.self, forKey: .minSizeMB) ?? 0
        maxSizeMB = try c.decodeIfPresent(Int.self, forKey: .maxSizeMB) ?? 0
        quality = try c.decodeIfPresent(String.self, forKey: .quality) ?? ""
        oneOfEachEpisode = try c.decodeIfPresent(Bool.self, forKey: .oneOfEachEpisode) ?? false
        episodes = try c.decodeIfPresent([String].self, forKey: .episodes) ?? []
    }
}

public struct FeedItem: Equatable, Sendable {
    public let id: String
    public let title: String
    public let link: String
    public let size: Int64?
}

public enum Feeds {
    public static let maxSeen = 5000
    public static var file: URL { AppPaths.support.appendingPathComponent("feeds.json") }

    public static func load() -> [FeedSubscription] {
        guard let data = try? Data(contentsOf: file) else { return [] }
        let decoder = JSONDecoder(); decoder.dateDecodingStrategy = .iso8601
        return (try? decoder.decode([FeedSubscription].self, from: data)) ?? []
    }

    public static func save(_ feeds: [FeedSubscription]) {
        let encoder = JSONEncoder(); encoder.dateEncodingStrategy = .iso8601; encoder.outputFormatting = .prettyPrinted
        try? FileManager.default.createDirectory(at: AppPaths.support, withIntermediateDirectories: true)
        if let data = try? encoder.encode(feeds) { try? data.write(to: file, options: .atomic) }
    }

    /// Reads RSS 2.0, RSS 1.0 and Atom (no DTDs or external entities). The link is the enclosure, else a magnet, else the item's link.
    final class Reader: NSObject, XMLParserDelegate {
        var items: [FeedItem] = []
        private var inItem = false, text = ""
        private var fields: [String: String] = [:]
        private var enclosure: String?, enclosureSize: Int64?, atomLink: String?, atomEnclosure: String?

        func parser(_ parser: XMLParser, didStartElement name: String, namespaceURI: String?, qualifiedName: String?, attributes a: [String: String]) {
            let local = name.split(separator: ":").last.map(String.init) ?? name
            if local == "item" || local == "entry" {
                inItem = true; fields = [:]; enclosure = nil; enclosureSize = nil; atomLink = nil; atomEnclosure = nil
            } else if inItem && local == "enclosure" {
                enclosure = a["url"]; enclosureSize = a["length"].flatMap { Int64($0) }
            } else if inItem && local == "link", let href = a["href"] {
                if a["rel"] == "enclosure" { atomEnclosure = href; enclosureSize = a["length"].flatMap { Int64($0) } }
                else if a["rel"] == nil || a["rel"] == "alternate" { atomLink = href }
            }
            text = ""
        }
        func parser(_ parser: XMLParser, foundCharacters string: String) { text += string }
        func parser(_ parser: XMLParser, foundCDATA data: Data) { text += String(decoding: data, as: UTF8.self) }
        func parser(_ parser: XMLParser, didEndElement name: String, namespaceURI: String?, qualifiedName: String?) {
            let local = name.split(separator: ":").last.map(String.init) ?? name
            if local == "item" || local == "entry" {
                inItem = false
                func web(_ s: String?) -> Bool {
                    guard let s, !s.isEmpty else { return false }
                    return s.lowercased().hasPrefix("magnet:?") || ["http", "https"].contains(URL(string: s)?.scheme?.lowercased() ?? "")
                }
                guard let link = [enclosure, atomEnclosure, fields["magnetURI"], atomLink, fields["link"]].first(where: web) ?? nil else { return }
                let size = enclosureSize ?? fields["contentLength"].flatMap { Int64($0) } ?? fields["size"].flatMap(Feeds.parseSize)
                items.append(FeedItem(id: (fields["guid"] ?? fields["id"] ?? link).trimmingCharacters(in: .whitespacesAndNewlines),
                                      title: fields["title"] ?? "", link: link.trimmingCharacters(in: .whitespacesAndNewlines), size: size))
            } else if inItem {
                fields[local] = text.trimmingCharacters(in: .whitespacesAndNewlines)
            }
            text = ""
        }
    }

    public static func parse(_ data: Data) throws -> [FeedItem] {
        let parser = XMLParser(data: data)
        parser.shouldResolveExternalEntities = false
        parser.shouldProcessNamespaces = false
        let reader = Reader()
        parser.delegate = reader
        guard parser.parse() else { throw parser.parserError ?? CocoaError(.fileReadCorruptFile) }
        return reader.items
    }

    /// "1.4 GiB", "700 MB", "123456" → bytes.
    public static func parseSize(_ text: String) -> Int64? {
        let t = text.trimmingCharacters(in: .whitespaces).uppercased().replacingOccurrences(of: ",", with: ".")
        let number = t.prefix { $0.isNumber || $0 == "." }
        guard let v = Double(number), v > 0 else { return nil }
        let unit = t.dropFirst(number.count).trimmingCharacters(in: .whitespaces)
        let mult: Double = unit.hasPrefix("K") ? 1024 : unit.hasPrefix("M") ? 1048576 : unit.hasPrefix("G") ? 1073741824 : unit.hasPrefix("T") ? 1099511627776 : 1
        return Int64(v * mult)
    }

    static func matches(_ title: String, _ rule: String, any: Bool) -> Bool {
        let r = rule.trimmingCharacters(in: .whitespaces)
        if r.count > 2, r.hasPrefix("/"), r.hasSuffix("/") {
            return title.range(of: String(r.dropFirst().dropLast()), options: [.regularExpression, .caseInsensitive]) != nil
        }
        let words = r.split(whereSeparator: \.isWhitespace).map(String.init)
        return any ? words.contains { title.localizedCaseInsensitiveContains($0) } : words.allSatisfy { title.localizedCaseInsensitiveContains($0) }
    }

    /// A problem with a rule (an invalid regular expression), or nil.
    public static func ruleProblem(_ rule: String) -> String? {
        let r = rule.trimmingCharacters(in: .whitespaces)
        guard r.count > 2, r.hasPrefix("/"), r.hasSuffix("/") else { return nil }
        return (try? NSRegularExpression(pattern: String(r.dropFirst().dropLast()))) == nil ? "The rule's regular expression is not valid." : nil
    }

    public static func wanted(_ f: FeedSubscription, _ item: FeedItem) -> Bool {
        (f.include.trimmingCharacters(in: .whitespaces).isEmpty || matches(item.title, f.include, any: false))
            && (f.exclude.trimmingCharacters(in: .whitespaces).isEmpty || !matches(item.title, f.exclude, any: true))
    }

    /// "show name|s01e02" (or "show name|2026-11-10"), or nil when the title names no episode.
    public static func episodeKey(_ title: String) -> String? {
        let ns = title as NSString
        let se = try! NSRegularExpression(pattern: #"\b[Ss](\d{1,2})[ ._-]?[Ee](\d{1,4})\b|\b(\d{1,2})x(\d{2,3})\b"#)
        let date = try! NSRegularExpression(pattern: #"\b(20\d{2})[ ._-](\d{2})[ ._-](\d{2})\b"#)
        var ep: String?, at = 0
        if let m = se.firstMatch(in: title, range: NSRange(location: 0, length: ns.length)) {
            func g(_ i: Int) -> String? { m.range(at: i).location == NSNotFound ? nil : ns.substring(with: m.range(at: i)) }
            let season = Int(g(1) ?? g(3) ?? "0") ?? 0, episode = Int(g(2) ?? g(4) ?? "0") ?? 0
            ep = String(format: "s%02de%02d", season, episode); at = m.range.location
        } else if let m = date.firstMatch(in: title, range: NSRange(location: 0, length: ns.length)) {
            ep = "\(ns.substring(with: m.range(at: 1)))-\(ns.substring(with: m.range(at: 2)))-\(ns.substring(with: m.range(at: 3)))"; at = m.range.location
        }
        guard let ep else { return nil }
        var show = ns.substring(to: at).replacingOccurrences(of: #"\[[^\]]*\]|\([^)]*\)"#, with: " ", options: .regularExpression)
        show = show.lowercased().replacingOccurrences(of: #"[^\p{L}\p{Nd}]+"#, with: " ", options: .regularExpression).trimmingCharacters(in: .whitespaces)
        return show.isEmpty ? nil : show + "|" + ep
    }

    /// The quality named in a title ("1080p"; 4k/uhd count as 2160p), or "".
    public static func qualityOf(_ title: String) -> String {
        guard let r = title.range(of: #"\b(4320p|2160p|1440p|1080p|720p|576p|480p|360p|4k|8k|uhd)\b"#, options: [.regularExpression, .caseInsensitive]) else { return "" }
        let q = title[r].lowercased()
        return q == "4k" || q == "uhd" ? "2160p" : q == "8k" ? "4320p" : q
    }

    /// The new items to download: rules, size limits, preferred qualities and one per episode (best quality wins).
    public static func choose(_ f: FeedSubscription, _ items: [FeedItem]) -> [FeedItem] {
        let prefs = f.quality.lowercased().split(whereSeparator: \.isWhitespace).map { $0 == "4k" || $0 == "uhd" ? "2160p" : String($0) }
        func rank(_ i: FeedItem) -> Int { prefs.isEmpty ? 0 : prefs.firstIndex(of: qualityOf(i.title)) ?? Int.max }
        var chosen: [String: FeedItem] = [:], out: [FeedItem] = []
        for i in items {
            guard wanted(f, i) else { continue }
            if f.minSizeMB > 0, let s = i.size, s < Int64(f.minSizeMB) * 1048576 { continue }
            if f.maxSizeMB > 0, let s = i.size, s > Int64(f.maxSizeMB) * 1048576 { continue }
            if !prefs.isEmpty && rank(i) == Int.max { continue }
            if f.oneOfEachEpisode, let key = episodeKey(i.title) {
                if f.episodes.contains(key) { continue }
                if let best = chosen[key], rank(best) <= rank(i) { continue }
                chosen[key] = i
                continue
            }
            out.append(i)
        }
        return out + items.filter { i in chosen.values.contains(i) }
    }

    /// Reads the feed and returns the new items to download, marking every new item as seen. The first check of a feed
    /// with skipExisting only remembers what is already there.
    public static func check(_ f: inout FeedSubscription, session: URLSession) async throws -> [FeedItem] {
        guard let url = URL(string: f.url.trimmingCharacters(in: .whitespaces)), ["http", "https"].contains(url.scheme?.lowercased() ?? "") else {
            throw Debrid.Failure(message: "The feed address must start with http:// or https://")
        }
        var request = URLRequest(url: url); request.setValue("NexonicDownloadManager", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else { throw Debrid.Failure(message: "The feed answered with an error.") }
        guard data.count <= 16 * 1024 * 1024 else { throw Debrid.Failure(message: "The feed is too large.") }
        let items = try parse(data)
        let first = f.lastChecked == nil
        let fresh = items.filter { !f.seen.contains($0.id) }
        f.seen += fresh.map(\.id)
        if f.seen.count > maxSeen { f.seen.removeFirst(f.seen.count - maxSeen) }
        f.lastChecked = Date()
        let wanted = first && f.skipExisting ? [] : choose(f, fresh)
        if f.oneOfEachEpisode { f.episodes += wanted.compactMap { episodeKey($0.title) } }
        f.lastResult = "\(items.count) items, \(wanted.count) new to download"
        return wanted
    }

    public static func due(_ f: FeedSubscription, now: Date = Date()) -> Bool {
        f.enabled && (f.lastChecked.map { now.timeIntervalSince($0) >= Double(max(15, f.intervalMinutes) * 60) } ?? true)
    }
}

/// Colour schemes (the same built-in ones as the Windows and Linux apps): a light or dark base and an accent.
public struct ColorSchemeChoice: Equatable, Sendable {
    public let name: String
    public let dark: Bool
    public let accent: String
    public let background: String

    public static let builtIn: [ColorSchemeChoice] = [
        .init(name: "Ocean", dark: true, accent: "#3AA0F2", background: "#0E1A24"),
        .init(name: "Forest", dark: false, accent: "#2E7D32", background: "#F4F8F3"),
        .init(name: "Midnight", dark: true, accent: "#8B7CF6", background: "#0B0B10"),
        .init(name: "High Contrast", dark: true, accent: "#FFD400", background: "#000000"),
    ]

    public static func named(_ name: String) -> ColorSchemeChoice? { builtIn.first { $0.name == name } ?? custom(name) }

    /// The user's own scheme, saved as "custom:dark:#RRGGBB:#RRGGBB" (light or dark, accent, background).
    public static func custom(_ code: String) -> ColorSchemeChoice? {
        let p = code.split(separator: ":").map(String.init)
        guard p.count == 4, p[0] == "custom", ["dark", "light"].contains(p[1]), rgb(p[2]) != nil, rgb(p[3]) != nil else { return nil }
        return .init(name: code, dark: p[1] == "dark", accent: p[2].uppercased(), background: p[3].uppercased())
    }

    public static func customCode(dark: Bool, accent: String, background: String) -> String {
        "custom:\(dark ? "dark" : "light"):\(accent.uppercased()):\(background.uppercased())"
    }

    /// WCAG contrast ratio of two colours (1 to 21); 3 or more keeps the accent readable on the background.
    public static func contrast(_ a: String, _ b: String) -> Double? {
        guard let x = rgb(a), let y = rgb(b) else { return nil }
        func lum(_ c: (Double, Double, Double)) -> Double {
            func ch(_ v: Double) -> Double { v <= 0.03928 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4) }
            return 0.2126 * ch(c.0) + 0.7152 * ch(c.1) + 0.0722 * ch(c.2)
        }
        let l1 = lum(x), l2 = lum(y)
        return (max(l1, l2) + 0.05) / (min(l1, l2) + 0.05)
    }

    /// (r, g, b) in 0…1 from "#RRGGBB".
    public static func rgb(_ hex: String) -> (Double, Double, Double)? {
        let h = hex.trimmingCharacters(in: CharacterSet(charactersIn: "# "))
        guard h.count == 6, let v = UInt32(h, radix: 16) else { return nil }
        return (Double((v >> 16) & 0xFF) / 255, Double((v >> 8) & 0xFF) / 255, Double(v & 0xFF) / 255)
    }
}

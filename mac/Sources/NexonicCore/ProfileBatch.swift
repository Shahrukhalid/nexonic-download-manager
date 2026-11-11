import Foundation

/// One video found on a profile, channel, playlist or page.
public struct BatchEntry: Codable, Equatable, Sendable, Identifiable {
    public var url: String
    public var title: String
    public var duration: Double?
    public var date: Date?
    public var kind: String = "video"  // video, shorts or live
    public var available = true
    public var id: String { url }
}

/// Which found videos to keep. Values a site does not report (a date, a length) never exclude a video.
public struct BatchFilter: Codable, Equatable, Sendable {
    public var from: Date?
    public var to: Date?
    public var keywords = ""
    public var minSeconds: Int?
    public var maxSeconds: Int?
    public var kind = "all"  // all, videos, shorts, live
    public var max = ProfileBatch.defaultMax
    public init() {}
}

/// A profile checked again on a schedule for videos that were not there before.
public struct SavedProfile: Codable, Equatable, Sendable, Identifiable {
    public var id = UUID().uuidString
    public var url = ""
    public var title = ""
    public var folder = ""
    public var quality = "best"
    public var filter = BatchFilter()
    public var checkHours = 0
    public var lastChecked: Date?
    public var lastResult = ""
    public var seen: [String] = []
    public init(url: String) { self.url = url }
    public func due(_ now: Date = Date()) -> Bool {
        checkHours > 0 && (lastChecked.map { now.timeIntervalSince($0) >= Double(checkHours) * 3600 } ?? true)
    }
}

public struct BatchCounts: Equatable, Sendable {
    public var discovered = 0, queued = 0, duplicates = 0, filtered = 0, inaccessible = 0
    public var text: String {
        String(format: String(localized: "%d found · %d added · %d already downloaded · %d left out by filters · %d not accessible"),
               discovered, queued, duplicates, filtered, inaccessible)
    }
}

/// Profile Batch: every video the video engine can list at one link (it follows the site's pages itself), read without any
/// login, cookies or keys. Nothing behind a login, paywall or CAPTCHA is reached; such videos are counted as not accessible.
public enum ProfileBatch {
    public static let defaultMax = 500, limit = 5000

    public static let qualities: [(key: String, title: String, height: Int?, audio: Bool)] = [
        ("best", "Best", nil, false), ("8k", "8K", 4320, false), ("4k", "4K", 2160, false), ("1080", "1080p", 1080, false),
        ("720", "720p", 720, false), ("480", "480p", 480, false), ("original", "Original (no conversion)", SiteVideo.originalQuality, false),
        ("audio", "Sound only", nil, true),
    ]

    public static func quality(_ key: String) -> (height: Int?, audio: Bool) {
        qualities.first { $0.key == key }.map { ($0.height, $0.audio) } ?? (nil, false)
    }

    static let fields = "%(.{url,webpage_url,original_url,title,fulltitle,duration,timestamp,upload_date,release_timestamp,live_status,was_live,availability,ie_key,id,playlist_title,playlist_uploader,uploader,channel})j"

    public static func arguments(_ link: String, quickJS: String?, start: Int = 1, max: Int = defaultMax) -> [String] {
        let max = Swift.max(1, Swift.min(max, limit))
        var a = ["--ignore-config", "--flat-playlist", "--ignore-errors", "--no-colors", "--js-runtimes", "node",
                 "--extractor-retries", "3", "--retry-sleep", "extractor:exp=2:30", "--print", fields,
                 "--playlist-start", String(start), "--playlist-end", String(start + max - 1)]
        if let quickJS { a += ["--js-runtimes", "quickjs:\(quickJS)"] }
        if let plugins = EnginePlugins.folder { a += ["--plugin-dirs", plugins] }
        return a + ["--", Playlists.normalize(link)]
    }

    public static func parseLine(_ line: String) -> (entry: BatchEntry, owner: String?)? {
        let t = line.trimmingCharacters(in: .whitespacesAndNewlines)
        guard t.hasPrefix("{"), let o = (try? JSONSerialization.jsonObject(with: Data(t.utf8))) as? [String: Any] else { return nil }
        func s(_ k: String) -> String? { (o[k] as? String).flatMap { $0.isEmpty ? nil : $0 } }
        func n(_ k: String) -> Double? { (o[k] as? NSNumber)?.doubleValue }
        let id = s("id") ?? ""
        var url = s("webpage_url") ?? s("url") ?? s("original_url")
        if !(url?.lowercased().hasPrefix("http") ?? false), !id.isEmpty, (s("ie_key") ?? "").hasPrefix("Youtube") { url = "https://www.youtube.com/watch?v=\(id)" }
        guard let u = url, u.lowercased().hasPrefix("http") else { return nil }
        var date: Date?
        if let ts = n("timestamp") ?? n("release_timestamp") { date = Date(timeIntervalSince1970: ts) }
        else if let d = s("upload_date"), d.count == 8 {
            let f = DateFormatter(); f.dateFormat = "yyyyMMdd"; f.locale = Locale(identifier: "en_US_POSIX"); date = f.date(from: d)
        }
        let liveStatus: String = s("live_status") ?? ""
        let wasLive: Bool = (o["was_live"] as? Bool) ?? false
        let lower = u.lowercased()
        var kind = "video"
        if ["is_live", "was_live", "is_upcoming"].contains(liveStatus) || wasLive { kind = "live" }
        else if lower.contains("/shorts/") || lower.contains("/reel/") { kind = "shorts" }
        let title: String = s("title") ?? s("fulltitle") ?? u
        let availability: String = s("availability") ?? ""
        var available = !["private", "premium_only", "subscriber_only", "needs_auth"].contains(availability)
        if title.hasPrefix("[Private video]") || title.hasPrefix("[Deleted video]") { available = false }
        return (BatchEntry(url: u, title: title, duration: n("duration"), date: date, kind: kind, available: available),
                s("playlist_title") ?? s("playlist_uploader") ?? s("channel") ?? s("uploader"))
    }

    /// The address without fragments and trailing slashes, lowercased host, to recognise the same video.
    public static func key(_ url: String) -> String {
        let base = url.split(separator: "#", maxSplits: 1).first.map(String.init) ?? url
        guard var c = URLComponents(string: base.trimmingCharacters(in: .whitespaces)) else { return base }
        c.host = c.host?.lowercased(); c.scheme = c.scheme?.lowercased()
        return c.string ?? base
    }

    public static func matches(_ e: BatchEntry, _ f: BatchFilter) -> Bool {
        let cal = Calendar.current
        if let d = e.date {
            if let from = f.from, cal.startOfDay(for: d) < cal.startOfDay(for: from) { return false }
            if let to = f.to, cal.startOfDay(for: d) > cal.startOfDay(for: to) { return false }
        }
        if let s = e.duration {
            if let min = f.minSeconds, s < Double(min) { return false }
            if let max = f.maxSeconds, s > Double(max) { return false }
        }
        if f.kind == "videos" && e.kind != "video" { return false }
        if (f.kind == "shorts" || f.kind == "live") && e.kind != f.kind { return false }
        let words = f.keywords.split(separator: " ").map(String.init)
        let include = words.filter { !$0.hasPrefix("!") }
        let exclude = words.filter { $0.hasPrefix("!") && $0.count > 1 }.map { String($0.dropFirst()) }
        if !include.isEmpty && !include.contains(where: { e.title.localizedCaseInsensitiveContains($0) }) { return false }
        return !exclude.contains { e.title.localizedCaseInsensitiveContains($0) }
    }

    public static func choose(_ found: [BatchEntry], _ filter: BatchFilter, alreadyDownloaded: (String) -> Bool,
                              seen: Set<String>? = nil) -> (take: [BatchEntry], counts: BatchCounts) {
        var c = BatchCounts(), keys = Set<String>(), take: [BatchEntry] = []
        for e in found {
            guard keys.insert(key(e.url)).inserted else { continue }
            c.discovered += 1
            if !e.available { c.inaccessible += 1; continue }
            if alreadyDownloaded(e.url) || seen?.contains(key(e.url)) == true { c.duplicates += 1; continue }
            if !matches(e, filter) || take.count >= Swift.max(1, Swift.min(filter.max, limit)) { c.filtered += 1; continue }
            take.append(e)
        }
        c.queued = take.count
        return (take, c)
    }

    public static func folder(base: URL, owner: String, url: String) -> URL {
        var name = owner.trimmingCharacters(in: .whitespaces).isEmpty ? "" : FileNames.sanitize(owner.trimmingCharacters(in: .whitespaces))
        if name.isEmpty, let u = URL(string: url) { name = FileNames.sanitize((u.host ?? "Profile") + u.path.replacingOccurrences(of: "/", with: " ")).trimmingCharacters(in: .whitespaces) }
        if name.count > 80 { name = String(name.prefix(80)).trimmingCharacters(in: .whitespaces) }
        return base.appendingPathComponent(name.isEmpty ? "Profile" : name, isDirectory: true)
    }

    public static func export(_ entries: [BatchEntry], format: String) -> String {
        let df = DateFormatter(); df.dateFormat = "yyyy-MM-dd"; df.locale = Locale(identifier: "en_US_POSIX")
        switch format {
        case "csv":
            func c(_ v: String) -> String { v.contains(where: { ",\"\n\r".contains($0) }) ? "\"" + v.replacingOccurrences(of: "\"", with: "\"\"") + "\"" : v }
            var out = "url,title,date,duration_seconds,type,accessible\r\n"
            for e in entries {
                let date: String = e.date.map { df.string(from: $0) } ?? ""
                let seconds: String = e.duration.map { String(Int($0)) } ?? ""
                let fields: [String] = [c(e.url), c(e.title), date, seconds, e.kind, e.available ? "yes" : "no"]
                out += fields.joined(separator: ",") + "\r\n"
            }
            return out
        case "json":
            var list: [[String: Any]] = []
            for e in entries {
                var item: [String: Any] = ["url": e.url, "title": e.title, "type": e.kind, "accessible": e.available]
                item["date"] = e.date.map { df.string(from: $0) as Any } ?? NSNull()
                item["duration_seconds"] = e.duration.map { $0 as Any } ?? NSNull()
                list.append(item)
            }
            let data = (try? JSONSerialization.data(withJSONObject: list, options: [.prettyPrinted, .withoutEscapingSlashes, .sortedKeys])) ?? Data("[]".utf8)
            return String(decoding: data, as: UTF8.self)
        default:
            return entries.map(\.url).joined(separator: "\n") + (entries.isEmpty ? "" : "\n")
        }
    }

    // MARK: interrupted scans

    public struct Scan: Codable, Sendable {
        public var url = ""
        public var owner = ""
        public var entries: [BatchEntry] = []
        public var complete = false
        /// A limit the site puts on what visitors see, shown with the result.
        public var note: String?
    }

    /// What a site's lister said about the list itself (for example that visitors see only the latest posts).
    public static func note(_ errors: String) -> String {
        var seen: [String] = []
        for line in errors.split(whereSeparator: \.isNewline) where line.hasPrefix("WARNING:") {
            guard let r = line.range(of: EnginePlugins.marker) else { continue }
            let text = line[r.upperBound...].trimmingCharacters(in: .whitespaces)
            if !seen.contains(text) { seen.append(text) }
        }
        return seen.joined(separator: " ")
    }

    static func scanFile(_ url: String) -> URL {
        let safe = key(url).unicodeScalars.reduce(into: UInt64(1469598103934665603)) { $0 = ($0 ^ UInt64($1.value)) &* 1099511628211 }
        return AppPaths.parts.appendingPathComponent("profile-scans/\(String(safe, radix: 16)).json")
    }

    public static func loadScan(_ url: String) -> Scan? {
        (try? Data(contentsOf: scanFile(url))).flatMap { try? JSONDecoder().decode(Scan.self, from: $0) }
    }

    static func saveScan(_ s: Scan) {
        let f = scanFile(s.url)
        try? FileManager.default.createDirectory(at: f.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? JSONEncoder().encode(s).write(to: f, options: .atomic)
    }

    public static func forgetScan(_ url: String) { try? FileManager.default.removeItem(at: scanFile(url)) }

    /// Lists the videos with the video engine, calling `found` for each as it comes (on this thread). An interrupted earlier
    /// scan continues after the videos it found. `stop` is checked between videos. Throws a readable reason on failure.
    public static func discover(_ link: String, proxy: ProxySettings?, max: Int, fresh: Bool = false,
                                stop: () -> Bool = { false }, found: (BatchEntry, Int) -> Void = { _, _ in }) throws -> Scan {
        var scan = fresh ? Scan() : (loadScan(link) ?? Scan())
        if scan.complete { scan = Scan() }
        scan.url = link
        for (i, e) in scan.entries.enumerated() { found(e, i + 1) }
        if scan.entries.count >= max { scan.complete = true; return scan }
        SiteVideoTools.prepare()
        guard SiteVideoTools.ensureInstalled() else { throw ProfileBatchError(SiteVideoTools.missing) }
        let prepared = try SiteVideo.prepare(arguments(link, quickJS: SiteVideoTools.quickJS, start: scan.entries.count + 1, max: max - scan.entries.count), proxy: proxy)
        try FileManager.default.createDirectory(at: AppPaths.parts, withIntermediateDirectories: true)
        let errorFile = AppPaths.parts.appendingPathComponent("profile-\(UUID().uuidString).log")
        FileManager.default.createFile(atPath: errorFile.path, contents: nil)
        defer {
            if let f = prepared.configFile { try? FileManager.default.removeItem(at: f) }
            try? FileManager.default.removeItem(at: errorFile)
        }
        let p = Process()
        p.executableURL = SiteVideoTools.ytdlp
        p.arguments = prepared.args
        let out = Pipe()
        p.standardOutput = out
        p.standardError = try FileHandle(forWritingTo: errorFile)
        try p.run()
        var buffer = Data(), lastSave = Date()
        let reader = out.fileHandleForReading
        while true {
            if stop() { p.terminate(); saveScan(scan); throw CancellationError() }
            let chunk = reader.availableData
            if chunk.isEmpty { break }
            buffer.append(chunk)
            while let nl = buffer.firstIndex(of: 0x0A) {
                let line = String(decoding: buffer[buffer.startIndex..<nl], as: UTF8.self)
                buffer.removeSubrange(buffer.startIndex...nl)
                guard let parsed = parseLine(line) else { continue }
                if scan.owner.isEmpty, let o = parsed.owner { scan.owner = o }
                scan.entries.append(parsed.entry)
                found(parsed.entry, scan.entries.count)
                if Date().timeIntervalSince(lastSave) > 2 { saveScan(scan); lastSave = Date() }
            }
        }
        p.waitUntilExit()
        if scan.entries.isEmpty {
            forgetScan(link)
            let errors = (try? String(contentsOf: errorFile, encoding: .utf8)) ?? ""
            throw ProfileBatchError(p.terminationStatus != 0 || errors.contains("ERROR") ? SiteVideo.error(errors) : "No videos were found at this link")
        }
        scan.complete = true
        scan.note = note((try? String(contentsOf: errorFile, encoding: .utf8)) ?? "")
        forgetScan(link)
        return scan
    }
}

/// The profile listers Nexonic adds to the video engine (engine/nexonic in the app's resources): they only read what a
/// site shows visitors who are not signed in. Handed to the engine with --plugin-dirs.
public enum EnginePlugins {
    /// Messages from the listers start with this, so the app shows them as they are.
    public static let marker = "Nexonic:"
    /// Copied out of the signed app (the engine writes compiled files next to plugins) when missing or changed.
    public static let folder: String? = {
        let rel = "nexonic/yt_dlp_plugins/extractor/nexonic_profiles.py"
        guard let source = Bundle.main.resourceURL?.appendingPathComponent("engine-plugins/" + rel),
              let bytes = try? Data(contentsOf: source) else { return nil }
        let root = SiteVideoTools.folder.appendingPathComponent("plugins", isDirectory: true)
        let file = root.appendingPathComponent(rel)
        if (try? Data(contentsOf: file)) != bytes {
            do {
                try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                try bytes.write(to: file, options: .atomic)
            } catch { return nil }
        }
        return root.path
    }()
}

public struct ProfileBatchError: LocalizedError {
    public let message: String
    public var errorDescription: String? { message }
    public init(_ message: String) { self.message = message }
}

/// Saved profiles (profiles.json in the data folder).
public enum SavedProfiles {
    static var file: URL { AppPaths.support.appendingPathComponent("profiles.json") }
    public static func load() -> [SavedProfile] { (try? Data(contentsOf: file)).flatMap { try? JSONDecoder().decode([SavedProfile].self, from: $0) } ?? [] }
    public static func save(_ list: [SavedProfile]) {
        try? FileManager.default.createDirectory(at: AppPaths.support, withIntermediateDirectories: true)
        try? JSONEncoder().encode(list).write(to: file, options: .atomic)
    }
}

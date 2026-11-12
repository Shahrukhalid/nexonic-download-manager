import Foundation

/// One video found on a profile, channel, playlist or page.
public struct BatchEntry: Codable, Equatable, Sendable, Identifiable {
    public var url: String
    public var title: String
    public var duration: Double?
    public var date: Date?
    public var kind: String = "video"  // video, shorts, live or photo
    public var available = true
    /// Added in 0.31.0: the file itself (a gallery engine listing), downloaded as it is rather than by the video engine.
    public var direct: Bool?
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
        // The sites' picture and video servers sign every address anew; the file is the same without the signature.
        if let h = c.host, ["cdninstagram.com", "fbcdn.net", "tiktokcdn.com", "tiktokcdn-us.com", "twimg.com", "pinimg.com"].contains(where: { h.hasSuffix($0) }) { c.query = nil }
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

/// The profile listers Nexonic adds to the video engine (engine/nexonic_profiles.py in the app's resources): they only read what a
/// site shows visitors who are not signed in. Handed to the engine with --plugin-dirs.
public enum EnginePlugins {
    /// Messages from the listers start with this, so the app shows them as they are.
    public static let marker = "Nexonic:"
    /// Copied out of the signed app (the engine writes compiled files next to plugins) when missing or changed.
    public static let folder: String? = {
        let rel = "nexonic/yt_dlp_plugins/extractor/nexonic_profiles.py"
        guard let source = Bundle.main.resourceURL?.appendingPathComponent("engine-plugins/nexonic_profiles.py"),
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

/// Instagram shows visitors who are not signed in the latest posts of a profile, but only through its own page scripts.
/// The app opens the profile page in a private web view (no cookies, no sign-in), like a visitor's browser, runs
/// `script` and reads the video links the page shows. Nothing else of Instagram's is called.
public enum InstagramPage {
    static let reserved: Set<String> = ["p", "reel", "reels", "tv", "stories", "explore", "accounts", "direct", "about", "legal",
                                        "developer", "api", "graphql", "web", "challenge", "emails", "session", "privacy", "terms"]

    /// The user name of an Instagram profile link (instagram.com/name, /name/reels), or nil.
    public static func user(_ link: String) -> String? {
        let pattern = #"^https?://(?:www\.)?instagram\.com/([A-Za-z0-9._]{1,30})/?(?:reels/?|videos/?)?(?:[?#].*)?$"#
        let text = link.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let re = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive),
              let m = re.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)),
              let r = Range(m.range(at: 1), in: text) else { return nil }
        let name = String(text[r])
        return reserved.contains(name.lowercased()) ? nil : name
    }

    /// Run in the loaded page: {"posts": links to posts shown, "videos": [{"u": link, "t": picture text}]}.
    public static let script = #"""
(() => {
  const all = [...document.querySelectorAll('a[href*="/reel/"], a[href*="/p/"]')], seen = new Set(), videos = [];
  for (const a of all) {
    const link = new URL(a.getAttribute('href'), location.href);
    const m = link.pathname.match(/\/(p|reel)\/([A-Za-z0-9_-]+)/);
    if (!m) continue;
    const alt = (a.querySelector('img') || {}).alt || '';
    const video = m[1] === 'reel' || /^Video by /.test(alt) || !!a.querySelector('svg[aria-label="Clip"], svg[aria-label="Video"], svg[aria-label="Reel"]');
    const u = 'https://www.instagram.com/' + m[1] + '/' + m[2] + '/';
    if (video && !seen.has(u)) { seen.add(u); videos.push({ u, t: alt }); }
  }
  return JSON.stringify({ posts: all.length, videos });
})()
"""#

    /// What `script` returned: the number of posts the page shows and its videos.
    public static func read(_ json: String?) -> (posts: Int, videos: [BatchEntry]) {
        guard let json, let o = (try? JSONSerialization.jsonObject(with: Data(json.utf8))) as? [String: Any] else { return (0, []) }
        let posts = (o["posts"] as? NSNumber)?.intValue ?? 0
        var videos: [BatchEntry] = []
        let dates = DateFormatter()
        dates.locale = Locale(identifier: "en_US_POSIX")
        dates.dateFormat = "MMMM d, yyyy"
        for v in o["videos"] as? [[String: Any]] ?? [] {
            guard let url = v["u"] as? String, url.hasPrefix("https://www.instagram.com/") || url.hasPrefix("https://www.facebook.com/") else { continue }
            let text = (v["t"] as? String) ?? ""
            var date: Date?
            if let r = text.range(of: #" on [A-Z][a-z]+ \d{1,2}, \d{4}"#, options: .regularExpression) {
                date = dates.date(from: String(text[r].dropFirst(4)))
            }
            var title = text.split(whereSeparator: \.isWhitespace).joined(separator: " ")
            if title.count > 120 { title = String(title.prefix(120)) + "…" }
            videos.append(BatchEntry(url: url, title: title.isEmpty ? url : title, duration: nil, date: date,
                                     kind: url.contains("/reel/") ? "shorts" : "video"))
        }
        return (posts, videos)
    }

    /// Shown with the result: Instagram shows visitors only the latest posts.
    public static func note(_ posts: Int) -> String {
        "Instagram shows visitors who are not signed in only the latest \(posts) posts of a profile; Nexonic took the videos among them."
    }

    /// The result for Profile Batch, as if the engine had listed it.
    public static func scan(url: String, user: String, posts: Int, videos: [BatchEntry]) -> ProfileBatch.Scan {
        var s = ProfileBatch.Scan()
        s.url = url; s.owner = user; s.entries = videos; s.complete = true; s.note = note(posts)
        return s
    }

    public static let nothing = "Instagram did not show any posts of this profile to a visitor who is not signed in (it may be private or not exist)."

    public static func noVideos(_ posts: Int) -> String {
        "None of the latest \(posts) posts Instagram shows visitors of this profile is a video."
    }
}

/// Instagram and Facebook profiles in Profile Batch. Their profile pages show visitors who are not signed in only the
/// latest posts; the rest come through the user's own signed-in browser and the Nexonic extension (see more(_:)).
public struct SocialSite: Sendable, Equatable {
    public let name: String, loginURL: String, domain: String, sessionCookie: String, script: String
}

public enum SocialPages {
    /// The address the gallery engine lists a profile from: a Facebook page's own address rather than one of its tabs
    /// (reels, videos, photos), which it doesn't read; other sites' addresses as they are.
    public static func listable(_ url: String) -> String {
        guard var c = URLComponents(string: url), let host = c.host?.lowercased(), host == "facebook.com" || host.hasSuffix(".facebook.com") else { return url }
        let parts = c.path.split(separator: "/")
        if parts.count >= 2, ["reels", "videos", "photos", "photos_by", "reels_tab"].contains(parts[1].lowercased()) { c.path = "/" + parts[0] + "/" }
        c.queryItems = c.queryItems?.filter { $0.name == "id" }
        if c.queryItems?.isEmpty == true { c.queryItems = nil }
        return c.string ?? url
    }

    /// Added under what a visitor sees: how to get the rest. The Nexonic extension's "Download all" on the profile, in a
    /// browser signed in to the site, hands the whole profile to the gallery engine with the browser's cookies (read-only, a person's pace) and sends every
    /// video and photo here. Nexonic itself never signs in.
    public static func more(_ site: SocialSite) -> String {
        "To get every video and photo: open this profile in Chrome, Edge or Firefox where you are signed in to \(site.name), click the Nexonic extension and choose Download all. Nexonic then saves the whole profile with that sign-in, a request every 1-2 seconds like a person, and only reads; it never sees your password."
    }

    public static let instagram = SocialSite(name: "Instagram", loginURL: "https://www.instagram.com/accounts/login/", domain: "instagram.com",
                                             sessionCookie: "sessionid", script: InstagramPage.script)
    public static let facebook = SocialSite(name: "Facebook", loginURL: "https://www.facebook.com/login/", domain: "facebook.com",
                                            sessionCookie: "c_user", script: facebookScript)

    /// The site, the profile's name and the pages that list its videos; nil for other links.
    public static func find(_ link: String) -> (site: SocialSite, owner: String, pages: [String])? {
        if let user = InstagramPage.user(link) { return (instagram, user, ["https://www.instagram.com/\(user)/"]) }
        let pattern = #"^https?://(?:www\.|m\.|web\.)?facebook\.com/(?:profile\.php\?id=(\d+)|(?!(?:watch|reel|videos|groups|events|share|story\.php|photo|permalink\.php|login|marketplace|gaming|people|pages)\b)([A-Za-z0-9.\-]{2,80}))"#
        let text = link.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let re = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive),
              let m = re.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) else { return nil }
        if let r = Range(m.range(at: 1), in: text) {
            let id = String(text[r])
            return (facebook, id, ["https://www.facebook.com/profile.php?id=\(id)&sk=reels_tab", "https://www.facebook.com/profile.php?id=\(id)&sk=videos"])
        }
        guard let r = Range(m.range(at: 2), in: text) else { return nil }
        let name = String(text[r])
        return (facebook, name, ["https://www.facebook.com/\(name)/reels/", "https://www.facebook.com/\(name)/videos/"])
    }

    /// Run in a loaded Facebook page: its reels and videos, as InstagramPage.script reports them.
    public static let facebookScript = #"""
(() => {
  const seen = new Set(), videos = [];
  for (const a of document.querySelectorAll('a[href*="/reel/"], a[href*="/videos/"], a[href*="watch/?v="], a[href*="watch?v="]')) {
    const link = new URL(a.getAttribute('href'), location.href);
    let id = null, reel = false, m;
    if ((m = link.pathname.match(/\/reel\/(\d+)/))) { id = m[1]; reel = true; }
    else if ((m = link.pathname.match(/\/videos\/(?:[^/]+\/)?(\d{6,})/))) id = m[1];
    else if (/^\d{6,}$/.test(link.searchParams.get('v') || '')) id = link.searchParams.get('v');
    if (!id || seen.has(id)) continue;
    seen.add(id);
    const u = reel ? 'https://www.facebook.com/reel/' + id + '/' : 'https://www.facebook.com/watch/?v=' + id;
    videos.push({ u, t: (a.getAttribute('aria-label') || a.innerText || '').trim().slice(0, 200) });
  }
  return JSON.stringify({ posts: videos.length, videos });
})()
"""#


    /// Adds what a page shows now to what was found before; true when anything new came. Stops at max.
    public static func merge(_ found: inout [String: BatchEntry], order: inout [String], _ now: [BatchEntry], max: Int) -> Bool {
        var grew = false
        for e in now where found.count < max && found[e.url] == nil { found[e.url] = e; order.append(e.url); grew = true }
        return grew
    }

    /// The result for Profile Batch.
    public static func scan(url: String, owner: String, entries: [BatchEntry], note: String) -> ProfileBatch.Scan {
        var s = ProfileBatch.Scan()
        s.url = url; s.owner = owner; s.entries = entries; s.complete = true; s.note = note
        return s
    }
}

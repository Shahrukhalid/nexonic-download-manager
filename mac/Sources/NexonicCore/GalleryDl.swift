import CryptoKit
import Foundation

/// The gallery engine (gallery-dl, GPL-2.0, run as a separate program): saves every picture and clip of a gallery, profile
/// or album on sites it knows. It is not shipped with the app: the official build of the gallery-dl project is downloaded
/// the first time it is used, checked against the SHA-256 GitHub lists for it, and refreshed after two weeks.
public enum GalleryDl {
    static let api = URL(string: "https://api.github.com/repos/gdl-org/builds/releases/latest")!
    static let asset = "gallery-dl_macos"
    private static let installing = NSLock()

    public static var program: URL { SiteVideoTools.folder.appendingPathComponent("gallery-dl") }
    public static let missing = "The gallery engine could not be downloaded. Check the internet connection and try again."

    /// The download address and SHA-256 of `asset` in a GitHub release (API answer), only for checksummed GitHub files.
    public static func pick(_ releaseJSON: Data, asset: String) -> (url: URL, sha256: String)? {
        guard let root = try? JSONSerialization.jsonObject(with: releaseJSON) as? [String: Any],
              let assets = root["assets"] as? [[String: Any]] else { return nil }
        for a in assets where a["name"] as? String == asset {
            guard let digest = a["digest"] as? String, digest.hasPrefix("sha256:"),
                  let link = a["browser_download_url"] as? String, link.hasPrefix("https://github.com/"),
                  let url = URL(string: link) else { return nil }
            return (url, String(digest.dropFirst(7)).lowercased())
        }
        return nil
    }

    static var fresh: Bool {
        guard let date = (try? FileManager.default.attributesOfItem(atPath: program.path))?[.modificationDate] as? Date else { return false }
        return Date().timeIntervalSince(date) < 14 * 24 * 3600
    }

    /// Makes sure the engine is there (downloading it the first time). False when it could not be had.
    @discardableResult
    public static func ensureInstalled(status: ((String) -> Void)? = nil) -> Bool {
        if fresh { return true }
        installing.lock()
        defer { installing.unlock() }
        if fresh { return true }
        let fm = FileManager.default
        let had = fm.isExecutableFile(atPath: program.path)
        status?(had ? "Updating the gallery engine…" : "Getting the gallery engine (only the first time)…")
        var request = URLRequest(url: api)
        request.setValue("NexonicDownloadManager", forHTTPHeaderField: "User-Agent")
        if let release = SiteVideoTools.fetch(request)?.data, let picked = pick(release, asset: asset),
           let bytes = SiteVideoTools.fetch(URLRequest(url: picked.url))?.data,
           SHA256.hash(data: bytes).map({ String(format: "%02x", $0) }).joined() == picked.sha256 {
            try? fm.createDirectory(at: SiteVideoTools.folder, withIntermediateDirectories: true)
            let tmp = program.appendingPathExtension("tmp")
            if (try? bytes.write(to: tmp)) != nil {
                try? fm.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tmp.path)
                _ = SiteVideoTools.run("/usr/bin/xattr", ["-d", "com.apple.quarantine", tmp.path])
                try? fm.removeItem(at: program)
                try? fm.moveItem(at: tmp, to: program)
            }
        } else if had {
            // Could not refresh: keep the one we have and try again tomorrow.
            try? fm.setAttributes([.modificationDate: Date().addingTimeInterval(-13 * 24 * 3600)], ofItemAtPath: program.path)
        }
        return fm.isExecutableFile(atPath: program.path)
    }

    /// The engine's arguments: everything goes straight into `folder`; files already there are skipped.
    /// The site login, cookies and proxy are never here: they go in the private settings file (see `settings`).
    public static let pace = "extractor.sleep-request=1.0-2.0"

    public static func arguments(url: String, folder: String, speedLimitKBps: Int, referrer: String?, settingsFile: String? = nil) -> [String] {
        // A person's pace on every site: 1-2 seconds between the requests that list a profile or gallery.
        var args = ["--directory", folder, "-o", pace]
        if let settingsFile { args += ["--config", settingsFile] }
        if speedLimitKBps > 0 { args += ["--limit-rate", "\(speedLimitKBps)k"] }
        if let referrer, URL(string: referrer)?.scheme?.hasPrefix("http") == true { args += ["-o", "extractor.referer=" + referrer] }
        return args + ["--", url]
    }

    /// The private settings for one gallery (the engine's JSON settings file): the browser's cookies for the site, its
    /// browser name, the site login and the proxy. "" when there is nothing private. Lets galleries that need you signed in work.
    public static func settings(cookies: String?, userAgent: String?, username: String?, password: String?, proxyURL: String?) -> String {
        var extractor: [String: Any] = [:]
        let jar = cookieJar(cookies)
        if !jar.isEmpty { extractor["cookies"] = jar }
        if let userAgent, !userAgent.isEmpty { extractor["user-agent"] = userAgent }
        if let username, !username.isEmpty { extractor["username"] = username; extractor["password"] = password ?? "" }
        if let proxyURL, !proxyURL.isEmpty { extractor["proxy"] = proxyURL }
        guard !extractor.isEmpty,
              let data = try? JSONSerialization.data(withJSONObject: ["extractor": extractor], options: [.sortedKeys]) else { return "" }
        return String(decoding: data, as: UTF8.self)
    }

    /// "a=1; b=2" (a Cookie header) as name → value.
    public static func cookieJar(_ header: String?) -> [String: String] {
        var jar: [String: String] = [:]
        for part in (header ?? "").split(separator: ";") {
            guard let eq = part.firstIndex(of: "=") else { continue }
            let name = part[..<eq].trimmingCharacters(in: .whitespaces)
            if !name.isEmpty { jar[name] = part[part.index(after: eq)...].trimmingCharacters(in: .whitespaces) }
        }
        return jar
    }

    /// Writes the private settings, readable only by this user, next to the download parts (delete it when the engine is
    /// done); nil when there are none. Throws when it can't be written: never falls back to secrets on the command line.
    public static func writeSettings(_ settings: String) throws -> URL? {
        guard !settings.isEmpty else { return nil }
        try FileManager.default.createDirectory(at: AppPaths.parts, withIntermediateDirectories: true)
        let file = AppPaths.parts.appendingPathComponent("gallery-\(UUID().uuidString).json")
        guard FileManager.default.createFile(atPath: file.path, contents: Data(settings.utf8), attributes: [.posixPermissions: 0o600])
        else { throw CocoaError(.fileWriteUnknown, userInfo: [NSFilePathErrorKey: file.path]) }
        return file
    }

    /// A line of the engine's output: a saved file (true), a file it already had ("# path", false), or neither (nil).
    public static func savedFile(_ line: String) -> (saved: Bool, path: String)? {
        let t = line.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.hasPrefix("# /") { return (false, String(t.dropFirst(2))) }
        return t.hasPrefix("/") ? (true, t) : nil
    }
}

/// Profile Batch with the gallery engine: the photos and videos of a profile, listed (not downloaded) with the browser's
/// cookies for the site, at a person's pace. The user then picks Download Now or Add to Queue.
public extension GalleryDl {
    static let videoExtensions: Set<String> = ["mp4", "webm", "mov", "m4v", "mkv"]

    /// One line per file while listing: its address and the details used for its name, as a JSON array (the engine
    /// writes them as it finds them, so the count can be shown).
    static let listFormat = "[{_url!j},{extension!j},{post_shortcode!j},{shortcode!j},{post_id!j},{id!j},{filename!j},{description!j},{content!j},{title!j},{desc!j},{num!j},{count!j},{date!j},{duration!j},{username!j}]"

    /// The engine's arguments for listing: a line per file (nothing downloaded), at most `max` of them.
    static func listArguments(url: String, max: Int, settingsFile: String?, browser: String? = nil) -> [String] {
        var args = ["-N", listFormat, "--range", "1-\(Swift.max(1, max))", "-o", pace]
        if let settingsFile { args += ["--config", settingsFile] }
        if let browser { args += ["--cookies-from-browser", browser] }
        // TikTok: its own listing first through the video engine, then the gallery engine's own way.
        if URL(string: url)?.host?.lowercased().hasSuffix("tiktok.com") == true { args += ["-o", "extractor.tiktok.ytdl=true"] }
        // Instagram videos as one file each (not as separate picture and sound streams), so they are listed and saved
        // like the photos.
        if URL(string: url)?.host?.lowercased().hasSuffix("instagram.com") == true { args += ["-o", "extractor.instagram.videos=merged"] }
        return args + ["--", url]
    }

    /// Sites whose profiles the engine lists completely (photos and videos) with a browser's sign-in. Facebook pages
    /// are not among them: the engine can't list them.
    static let profileSites = ["instagram.com", "tiktok.com", "x.com", "twitter.com", "threads.net", "threads.com", "pinterest.com",
                               "reddit.com", "tumblr.com", "bsky.app", "vk.com", "flickr.com", "deviantart.com"]

    /// A profile (not a single post) on one of those sites.
    static func isProfile(_ url: String) -> Bool {
        guard let u = URL(string: url), let host = u.host?.lowercased(), profileSites.contains(where: { host == $0 || host.hasSuffix("." + $0) })
        else { return false }
        let first = u.pathComponents.dropFirst().first?.lowercased() ?? ""
        return !first.isEmpty && !["p", "reel", "tv", "stories", "explore", "accounts", "direct", "video", "status", "i", "home", "search",
                                   "settings", "messages", "notifications", "login", "share", "hashtag", "tag", "watch"].contains(first)
    }

    /// The engine's name for the browser that opens web links on this Mac (its sign-ins are used), nil when it can't read it.
    static func browserName(bundleID: String?) -> String? {
        switch bundleID?.lowercased() {
        case "com.google.chrome", "com.google.chrome.beta", "com.google.chrome.canary": return "chrome"
        case "com.microsoft.edgemac", "com.microsoft.edgemac.beta": return "edge"
        case "com.brave.browser": return "brave"
        case "org.mozilla.firefox", "org.mozilla.firefoxdeveloperedition": return "firefox"
        case "com.operasoftware.opera": return "opera"
        case "com.vivaldi.vivaldi": return "vivaldi"
        case "org.chromium.chromium": return "chromium"
        case "com.apple.safari": return "safari"
        default: return nil
        }
    }

    /// The files in the engine's listing (its lines): the picture or video address, named "description [post id].ext";
    /// and the account they belong to (their folder), "" when it doesn't say.
    static func entries(_ text: String, owner fallback: String = "") -> (entries: [BatchEntry], owner: String) {
        let iso = ISO8601DateFormatter()
        let df = DateFormatter(); df.dateFormat = "yyyy-MM-dd HH:mm:ss"; df.locale = Locale(identifier: "en_US_POSIX")
        df.timeZone = TimeZone(identifier: "UTC")
        var list: [BatchEntry] = [], seen = Set<String>(), owner = ""
        for line in text.split(whereSeparator: \.isNewline) {
            guard let f = (try? JSONSerialization.jsonObject(with: Data(line.utf8))) as? [Any], f.count >= 16,
                  let url = f[0] as? String, url.hasPrefix("https://"), seen.insert(url).inserted else { continue }
            let text = { (i: Int) -> String in (f[i] as? String) ?? (f[i] as? NSNumber)?.stringValue ?? "" }
            if owner.isEmpty { owner = text(15) }
            let ext = text(1).lowercased().isEmpty ? "jpg" : text(1).lowercased()
            let post = [2, 3, 4, 5, 6].map(text).first { !$0.isEmpty } ?? ""
            let num = (f[11] as? Int) ?? 0, count = (f[12] as? Int) ?? 0
            var caption = ([7, 8, 9, 10].map(text).first { !$0.isEmpty } ?? "").replacingOccurrences(of: "\n", with: " ").trimmingCharacters(in: .whitespaces)
            if caption.count > 100 { caption = String(caption.prefix(100)).trimmingCharacters(in: .whitespaces) }
            let label = caption.isEmpty ? (owner.isEmpty ? fallback : owner) : caption
            let name = FileNames.sanitize("\(label) [\(post)\(count > 1 && num > 0 ? "-\(num)" : "")].\(ext)")
            list.append(BatchEntry(url: url, title: name, duration: (f[14] as? NSNumber)?.doubleValue,
                                   date: df.date(from: text(13)) ?? iso.date(from: text(13) + (text(13).contains("+") || text(13).hasSuffix("Z") ? "" : "Z")),
                                   kind: videoExtensions.contains(ext) ? "video" : "photo", available: true, direct: true,
                                   post: [2, 3, 4].map(text).first { !$0.isEmpty }))
        }
        return (list, owner)
    }

    /// Lists a profile with the engine. `found(new, owner)` hands over the files as the engine finds them, so they can be
    /// shown (and downloaded) at once. Throws `GalleryListError` with the engine's own words when it found nothing.
    static func list(_ url: String, cookies: String?, browser: String? = nil, userAgent: String?, proxy: ProxySettings?, max: Int,
                     found: @escaping ([BatchEntry], String) -> Void = { _, _ in }, waiting: @escaping (Int) -> Void = { _ in },
                     stop: @escaping () -> Bool) throws -> (entries: [BatchEntry], owner: String) {
        guard ensureInstalled() else { throw GalleryListError(message: missing) }
        let settingsFile = try writeSettings(settings(cookies: cookies, userAgent: userAgent, username: nil, password: nil,
                                                      proxyURL: proxy.flatMap(SiteVideo.proxyURL)))
        defer { if let settingsFile { try? FileManager.default.removeItem(at: settingsFile) } }
        var all: [BatchEntry] = [], seen = Set<String>(), owner = "", lastError = ""
        for part in listParts(url) {
            var cursor: String?, tries = 0
            repeat {
                var args = listArguments(url: part, max: max, settingsFile: settingsFile?.path, browser: cookies == nil ? browser : nil)
                if let cursor { args.insert(contentsOf: ["-o", "cursor=\(cursor)"], at: args.count - 2) }
                let errors = try runList(args) { listed in
                    if owner.isEmpty { owner = listed.owner }
                    let fresh = listed.entries.filter { seen.insert(ProfileBatch.key($0.url)).inserted }
                    if !fresh.isEmpty { all += fresh; found(fresh, owner) }
                } stop: { stop() || all.count >= max }
                lastError = errors
                note(part, cursor: cursor, found: all.count, errors)
                // The site stopped answering partway (it limits how much one account may read in a row): the engine says
                // where it got to; after a pause the search continues from there, a few times, longer each time.
                cursor = resumeCursor(errors)
                guard cursor != nil, tries < 6, all.count < max else { break }
                tries += 1
                let pause = 60 * (1 << (tries - 1))  // 1, 2, 4, 8, 16, 32 minutes
                waiting(pause)
                for _ in 0..<pause { if stop() { throw CancellationError() }; Thread.sleep(forTimeInterval: 1) }
            } while true
            if stop() { throw CancellationError() }
        }
        if all.isEmpty {
            let said = lastError.split(whereSeparator: \.isNewline).last { $0.contains("error") || $0.contains("Error") } ?? lastError.split(whereSeparator: \.isNewline).last
            throw GalleryListError(message: said.map { String($0).replacingOccurrences(of: "[error] ", with: "") } ?? "")
        }
        return (all, owner)
    }

    /// The parts of a profile that are listed one after the other: an Instagram profile's posts, then its reels (videos
    /// that are not on the posts grid); anything else as it is.
    static func listParts(_ url: String) -> [String] {
        guard isProfile(url), let u = URL(string: url), u.host?.lowercased().hasSuffix("instagram.com") == true,
              let user = u.pathComponents.dropFirst().first else { return [url] }
        return ["https://www.instagram.com/\(user)/posts/", "https://www.instagram.com/\(user)/reels/"]
    }

    /// What each search said, kept in the data folder (the last 200 KB) so a search that finds less than expected can be
    /// looked into. No cookies or passwords are in it: those go to the engine in a private settings file.
    public static var searchLog: URL { AppPaths.support.appendingPathComponent("profile-search.log") }

    static func note(_ url: String, cursor: String?, found: Int, _ errors: String) {
        let text = "\(ISO8601DateFormatter().string(from: Date()))  \(url)\(cursor == nil ? "" : " (continued)")  \(found) found so far\n"
            + errors.split(whereSeparator: \.isNewline).suffix(40).map { "    " + $0 }.joined(separator: "\n") + "\n"
        var log = (try? Data(contentsOf: searchLog)) ?? Data()
        log.append(Data(text.utf8))
        if log.count > 200_000 { log = log.suffix(200_000) }
        try? log.write(to: searchLog, options: .atomic)
    }

    /// Where an interrupted listing got to, as the engine says it ("Use '-o cursor=…' to continue").
    static func resumeCursor(_ errors: String) -> String? {
        guard let r = errors.range(of: #"-o cursor=([^'"\s]+)"#, options: .regularExpression) else { return nil }
        return String(errors[r].dropFirst("-o cursor=".count))
    }

    /// Runs one listing, handing over what it lists as it arrives; returns what the engine said on its error output.
    private static func runList(_ args: [String], found: @escaping ((entries: [BatchEntry], owner: String)) -> Void, stop: @escaping () -> Bool) throws -> String {
        let p = Process()
        p.executableURL = program
        p.arguments = args
        var env = ProcessInfo.processInfo.environment; env["PYTHONIOENCODING"] = "utf-8"; p.environment = env
        let out = Pipe(), err = Pipe()
        p.standardOutput = out
        p.standardError = err
        try p.run()
        final class Box: @unchecked Sendable { let lock = NSLock(); var data = Data(); var errors = Data(); var done = false }
        let box = Box(), handle = out.fileHandleForReading, errHandle = err.fileHandleForReading
        DispatchQueue.global(qos: .utility).async {
            let e = errHandle.readDataToEndOfFile()
            box.lock.lock(); box.errors = e.suffix(4000); box.lock.unlock()
        }
        DispatchQueue.global(qos: .userInitiated).async {
            while true {
                let chunk = handle.availableData
                box.lock.lock()
                if chunk.isEmpty { box.done = true; box.lock.unlock(); break }
                box.data.append(chunk); box.lock.unlock()
            }
        }
        var handed = 0, owner = ""
        func handOver(final: Bool) {
            box.lock.lock(); let data = box.data; box.lock.unlock()
            // Only whole lines; the last one may still be arriving.
            guard let end = final ? data.endIndex : data.lastIndex(of: 10).map({ data.index(after: $0) }), end > handed else { return }
            let listed = entries(String(decoding: data[handed..<end], as: UTF8.self), owner: owner)
            handed = end
            if owner.isEmpty { owner = listed.owner }
            found(listed)
        }
        while true {
            Thread.sleep(forTimeInterval: 0.3)
            if stop() { p.terminate(); p.waitUntilExit(); handOver(final: true); return "" }
            handOver(final: false)
            box.lock.lock(); let finished = box.done; box.lock.unlock()
            if finished { break }
        }
        p.waitUntilExit()
        handOver(final: true)
        // The error output is read to its end once the engine has exited.
        for _ in 0..<20 { box.lock.lock(); let ready = !box.errors.isEmpty; box.lock.unlock(); if ready { break }; Thread.sleep(forTimeInterval: 0.05) }
        box.lock.lock(); defer { box.lock.unlock() }
        return String(decoding: box.errors, as: UTF8.self)
    }
}

/// The gallery engine found nothing: its own words (a site it doesn't know, a sign-in it wants …), shown to the user.
public struct GalleryListError: LocalizedError {
    public let message: String
    public var errorDescription: String? { message }
}

/// Runs the gallery engine for one gallery: the list row counts files and bytes; pausing stops it and resuming continues
/// (files already saved are skipped).
public final class GalleryTransfer: Downloader, @unchecked Sendable {
    public var onChange: (@Sendable (DownloadRecord) -> Void)?
    public let ownLimiter = SpeedLimiter()
    private var rec: DownloadRecord
    private let lock = NSLock()
    private var process: Process?
    private var stopping = false
    private var errors = ""
    private var buffer = ""
    private let sortIntoCategories: Bool
    private let speedLimitKBps: Int
    private let proxy: ProxySettings?

    public init(record: DownloadRecord, sortIntoCategories: Bool = false, speedLimitKBps: Int = 0, proxy: ProxySettings? = nil) {
        rec = record
        self.proxy = proxy
        self.sortIntoCategories = sortIntoCategories
        self.speedLimitKBps = [record.speedLimitKBps ?? 0, speedLimitKBps].filter { $0 > 0 }.min() ?? 0
    }

    private func change(_ f: (inout DownloadRecord) -> Void) { lock.lock(); f(&rec); lock.unlock() }
    private func notify() { lock.lock(); let copy = rec; lock.unlock(); onChange?(copy) }

    public func start() { DispatchQueue.global().async { self.run() } }

    public func pause() {
        lock.lock(); stopping = true; let p = process; lock.unlock()
        change { $0.status = .paused }
        p?.terminate()
        notify()
    }

    public func discard() {
        lock.lock(); stopping = true; let p = process; lock.unlock()
        p?.terminate()
    }

    private func fail(_ message: String) {
        change { $0.status = .failed; $0.error = message }
        notify()
    }

    private func run() {
        change { $0.status = .probing; $0.error = nil; $0.videoStep = nil }
        notify()
        guard GalleryDl.ensureInstalled(status: { [weak self] step in self?.change { $0.videoStep = step }; self?.notify() })
        else { return fail(GalleryDl.missing) }
        change { $0.videoStep = nil }
        let r = rec
        var folder: URL
        if let earlier = r.savedPath, FileManager.default.fileExists(atPath: earlier) {
            folder = URL(fileURLWithPath: earlier, isDirectory: true)
        } else {
            folder = CategoryRules.targetFolder(r.folder, fileName: "picture.jpg", url: r.url.absoluteString, sortBuiltIn: sortIntoCategories)
            folder.appendPathComponent(FileNames.sanitize(r.fileName.isEmpty ? "Gallery" : r.fileName), isDirectory: true)
        }
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        change { $0.savedPath = folder.path }
        let settingsFile: URL?
        do {
            settingsFile = try GalleryDl.writeSettings(GalleryDl.settings(cookies: r.cookies, userAgent: r.userAgent, username: r.username,
                                                                          password: r.password, proxyURL: proxy.flatMap(SiteVideo.proxyURL)))
        } catch { return fail("The gallery engine's private settings could not be written: \(error.localizedDescription)") }
        defer { if let settingsFile { try? FileManager.default.removeItem(at: settingsFile) } }
        let p = Process()
        p.executableURL = GalleryDl.program
        p.arguments = GalleryDl.arguments(url: r.url.absoluteString, folder: folder.path, speedLimitKBps: speedLimitKBps,
                                          referrer: r.referrer, settingsFile: settingsFile?.path)
        let out = Pipe(), err = Pipe()
        p.standardOutput = out
        p.standardError = err
        out.fileHandleForReading.readabilityHandler = { [weak self] h in self?.read(h.availableData) }
        err.fileHandleForReading.readabilityHandler = { [weak self] h in
            let d = h.availableData
            guard let self, !d.isEmpty else { return }
            self.lock.lock(); self.errors = String((self.errors + String(decoding: d, as: UTF8.self)).suffix(4000)); self.lock.unlock()
        }
        lock.lock()
        if stopping { lock.unlock(); return }
        process = p
        lock.unlock()
        do { try p.run() } catch { return fail("The gallery engine could not start: \(error.localizedDescription)") }
        change { $0.status = .downloading }
        notify()
        p.waitUntilExit()
        out.fileHandleForReading.readabilityHandler = nil
        err.fileHandleForReading.readabilityHandler = nil
        read(out.fileHandleForReading.readDataToEndOfFile())
        lock.lock(); let stopped = stopping; let errText = errors; let count = rec.streamDone ?? 0; lock.unlock()
        if stopped { return }
        let code = p.terminationStatus
        if code == 0 || (count > 0 && (code == 4 || code == 8)) {
            change { $0.status = .finished; $0.finished = Date(); $0.size = $0.streamBytes }
            notify()
            return
        }
        let last = errText.components(separatedBy: "\n").last(where: { $0.localizedCaseInsensitiveContains("error") })?
            .replacingOccurrences(of: #"^\[[^\]]*\]\[error\]\s*"#, with: "", options: .regularExpression)
            .trimmingCharacters(in: .whitespaces) ?? ""
        fail(!last.isEmpty ? last : code == 64 ? "The gallery engine does not know this site." : "The gallery engine stopped (code \(code)).")
    }

    private func read(_ data: Data) {
        guard !data.isEmpty else { return }
        lock.lock()
        buffer += String(decoding: data, as: UTF8.self)
        var lines = buffer.components(separatedBy: "\n")
        buffer = lines.removeLast()
        lock.unlock()
        for line in lines {
            guard let file = GalleryDl.savedFile(line) else { continue }
            let size = ((try? FileManager.default.attributesOfItem(atPath: file.path))?[.size] as? NSNumber)?.int64Value ?? 0
            change { $0.streamDone = ($0.streamDone ?? 0) + 1; $0.streamBytes = ($0.streamBytes ?? 0) + size }
            notify()
        }
    }
}

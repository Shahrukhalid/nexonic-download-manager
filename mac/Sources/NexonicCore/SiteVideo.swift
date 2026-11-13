import CryptoKit
import Foundation

/// Videos from YouTube, Instagram, Facebook, X, TikTok and 1,000+ other sites, saved with the bundled yt-dlp
/// in the best quality (picture and sound joined by the bundled ffmpeg).
public enum SiteVideo {
    /// Sites whose page links go to the video engine instead of being downloaded as files.
    public static let hosts = [
        "youtube.com", "youtu.be", "youtube-nocookie.com", "instagram.com", "facebook.com", "fb.watch", "x.com", "twitter.com",
        "tiktok.com", "vimeo.com", "dailymotion.com", "reddit.com", "redd.it", "twitch.tv", "soundcloud.com", "linkedin.com",
        "pinterest.com", "threads.net", "bilibili.com", "rumble.com", "streamable.com", "bandcamp.com", "mixcloud.com",
    ]

    public static func isSupported(_ url: URL) -> Bool {
        guard let host = url.host?.lowercased() else { return false }
        return hosts.contains { host == $0 || host.hasSuffix("." + $0) }
    }

    public struct Progress: Equatable {
        public var downloaded: Int64
        public var total: Int64?
        public var speed: Double?
        public var part: String? = nil  // format of the piece being downloaded (picture or sound)
        public var label: String? = nil  // "Picture 1080p" or "Sound"
    }

    /// Adds up the progress of the pieces of one video (picture and sound are downloaded one after the other).
    /// Each piece only ever grows, so the jumps of fragmented downloads can't inflate the total.
    public struct PartProgress {
        private var done: [String: Int64] = [:]
        private var totals: [String: Int64] = [:]
        private var order: [String] = []
        private var labels: [String: String] = [:]

        public init() {}

        /// Each piece in the order it started, for the progress window.
        public var pieces: [VideoPart] {
            order.enumerated().map { i, k in
                VideoPart(name: labels[k] ?? (order.count > 1 ? "Part \(i + 1)" : "Video"), done: done[k] ?? 0, total: totals[k])
            }
        }

        public mutating func add(_ p: Progress) -> (done: Int64, total: Int64?) {
            let key = p.part ?? ""
            if done[key] == nil { order.append(key) }
            if let l = p.label { labels[key] = l }
            done[key] = max(done[key] ?? 0, p.downloaded)
            if let t = p.total, t > 0 { totals[key] = t }
            let sum = order.reduce(Int64(0)) { $0 + (done[$1] ?? 0) }
            let known = order.reduce(Int64(0)) { $0 + max(totals[$1] ?? 0, done[$1] ?? 0) }
            return (sum, totals.isEmpty ? nil : max(known, sum))
        }
    }

    static let progressTag = "NDMP"
    static let fileTag = "NDMF"

    /// Reads a progress line printed by the template in `arguments` ("NDMP 1024 2048 NA 512.5").
    public static func progress(_ line: String) -> Progress? {
        let p = line.split(separator: " ").map(String.init)
        guard p.count >= 5, p[0] == progressTag, let done = Double(p[1]) else { return nil }
        let total = Double(p[2]) ?? Double(p[3])
        let part = p.count >= 6 && p[5] != "NA" ? p[5] : nil
        var label: String?
        if p.count >= 8 {
            let height = Int(p[6]).map { " \($0)p" } ?? ""
            label = p[7] == "none" ? "Sound" : "Picture" + height
        }
        return Progress(downloaded: Int64(done), total: total.map { Int64($0) }, speed: Double(p[4]), part: part, label: label)
    }

    /// What yt-dlp is doing after the pieces are downloaded ("[Merger] Merging formats into …").
    public static func step(_ line: String) -> String? {
        if line.hasPrefix("[Merger]") { return "Joining picture and sound…" }
        if line.hasPrefix("[ExtractAudio]") { return "Converting the sound…" }
        if line.hasPrefix("[Fixup") || line.hasPrefix("[VideoConvertor]") || line.hasPrefix("[VideoRemuxer]") { return "Finishing the file…" }
        if line.hasPrefix("[EmbedSubtitle]") { return "Adding subtitles…" }
        return nil
    }

    /// The video's real name from "[download] Destination: …/Title.f137.mp4" (the piece number is dropped).
    public static func destinationName(_ line: String) -> String? {
        let tag = "[download] Destination: "
        guard line.hasPrefix(tag) else { return nil }
        let name = (String(line.dropFirst(tag.count)) as NSString).lastPathComponent
        let clean = name.replacingOccurrences(of: #"\.f[0-9A-Za-z_-]+(\.[^.]+)$"#, with: "$1", options: .regularExpression)
        return clean.isEmpty ? nil : clean
    }

    /// The saved file's path, from the line printed after the final move.
    public static func savedFile(_ line: String) -> String? {
        line.hasPrefix(fileTag + " ") ? String(line.dropFirst(fileTag.count + 1)) : nil
    }

    /// The last error the tool printed, made readable.
    public static func error(_ text: String) -> String {
        let line = text.split(whereSeparator: \.isNewline).last { $0.contains("ERROR:") }.map(String.init) ?? ""
        var msg = line.components(separatedBy: "ERROR:").last?.trimmingCharacters(in: .whitespaces) ?? ""
        if let r = msg.range(of: #"^\[[^\]]+\] [^:]+: "#, options: .regularExpression) { msg.removeSubrange(r) }
        if msg.hasPrefix(EnginePlugins.marker) { return msg.dropFirst(EnginePlugins.marker.count).trimmingCharacters(in: .whitespaces) }
        // The engine's advice to report bugs on its own site and to update it is not for the app's users (the app updates it).
        for pattern in [#";?\s*please report this issue on.*$"#, #"\s*\(caused by .*$"#] {
            if let r = msg.range(of: pattern, options: [.regularExpression, .caseInsensitive]) { msg.removeSubrange(r) }
        }
        msg = msg.trimmingCharacters(in: .whitespaces)
        if msg.isEmpty { return "The video could not be downloaded" }
        if msg.localizedCaseInsensitiveContains("Unable to extract") {
            return "This site changed its pages, so they can't be read right now. Nexonic updates its video engine automatically; try again later"
        }
        if msg.localizedCaseInsensitiveContains("Unable to connect to proxy") || (msg.localizedCaseInsensitiveContains("Unable to download") && msg.contains("Errno")) {
            return "The site could not be reached. Check the connection (and the proxy, if one is set) and try again"
        }
        if msg.localizedCaseInsensitiveContains("DRM") { return "This video is copy-protected and can't be saved" }
        if msg.localizedCaseInsensitiveContains("anti-bot") || msg.localizedCaseInsensitiveContains("Cloudflare") {
            return "This site checks that a real browser is visiting, so the app can't open the page by itself. Play the video in your browser, then click Download this video: the browser hands the video over with your session"
        }
        let signIn = ["sign in", "sign-in", "login", "log in", "logged-in", "logged in", "account credentials", "--cookies"]
        if signIn.contains(where: { msg.localizedCaseInsensitiveContains($0) }) {
            return "The site only lets signed-in users download this video. Open it in your browser while signed in and click Download this video (your sign-in is sent along), or enter your account under Properties → Login"
        }
        // The engine's own name stays out of the app.
        return msg.replacingOccurrences(of: #"yt[-_]?dlp"#, with: "the video engine", options: [.regularExpression, .caseInsensitive])
    }

    /// What the download window shows for a video, read from the info the video engine looked up.
    public struct Info: Equatable {
        public var title: String
        public var ext: String
        public var size: Int64?
    }

    /// The command that looks a video up without downloading it (prints its details as JSON). The saved JSON is
    /// handed to `arguments(infoJSON:)`, so the download starts right away instead of looking the video up again.
    public static func infoArguments(url: URL, quickJS: String?, audioOnly: Bool, referrer: String?, cookies: String?,
                                     userAgent: String?, username: String?, password: String?, maxHeight: Int? = nil) -> [String] {
        var a = ["--ignore-config", "--no-playlist", "--no-colors", "-J", "--js-runtimes", "node"]
        if let quickJS { a += ["--js-runtimes", "quickjs:\(quickJS)"] }
        a += format(audioOnly: audioOnly, maxHeight: maxHeight)
        if let referrer, !referrer.isEmpty { a += ["--referer", referrer] }
        if let userAgent, !userAgent.isEmpty { a += ["--user-agent", userAgent] }
        if let cookies, !cookies.isEmpty { a += ["--add-header", "Cookie:\(cookies)"] }
        if let username, !username.isEmpty { a += ["--username", username, "--password", password ?? ""] }
        a += ["--", url.absoluteString]
        return a
    }

    /// Which picture and sound to fetch. Highest resolution and frame rate first (no higher than `maxHeight` when one
    /// was chosen); at equal quality prefer formats every player opens.
    /// maxHeight for "Original": the best picture and sound in the site's own formats (no preference for common formats).
    public static let originalQuality = -1

    public static func format(audioOnly: Bool, maxHeight: Int?) -> [String] {
        if audioOnly { return ["-f", "ba[ext=m4a]/ba/b"] }
        if maxHeight == originalQuality { return ["-f", "bv*+ba/b", "-S", "res,fps,hdr:12", "--merge-output-format", "mp4/mkv"] }
        let res = maxHeight.map { $0 > 0 ? "res:\($0)" : "res" } ?? "res"
        // A chosen quality is always saved as MP4 (the container every app opens); "Original" keeps MKV for codecs MP4 can't hold.
        return ["-f", "bv*+ba/b", "-S", "\(res),fps,hdr:12,vcodec:avc,acodec:m4a", "--merge-output-format", "mp4"]
    }

    /// Sites whose videos all have the same title ("Video by name"): the file is named after the caption instead, with the
    /// video's id so two videos never share a name (the engine would skip the second as "already downloaded").
    public static func naming(_ url: String) -> [String] {
        let host = URL(string: url)?.host?.lowercased() ?? ""
        let captioned = ["instagram.com", "facebook.com", "fb.watch", "x.com", "twitter.com", "snapchat.com", "threads.net", "threads.com"]
        return captioned.contains { host == $0 || host.hasSuffix("." + $0) }
            ? ["--parse-metadata", "%(description,title)s:(?s)(?P<title>.+)", "-o", "%(title).120B [%(id)s].%(ext)s"]
            : ["-o", "%(title).150B.%(ext)s"]
    }

    /// One picture quality a video is offered in.
    public struct Quality: Equatable, Sendable {
        public var height: Int  // the picture's shorter side: 1080 for 1080p, also for upright videos
        public var size: Int64?  // picture and sound together, approximate
        public init(height: Int, size: Int64?) { self.height = height; self.size = size }
    }

    /// The qualities a video is offered in (best first) and the size of its sound alone.
    public struct Choices: Equatable, Sendable {
        public var title: String
        public var video: [Quality]
        public var soundSize: Int64?
    }

    /// Reads the qualities on offer from the JSON printed by `infoArguments` (the video's own list of formats).
    public static func choices(_ data: Data) -> Choices? {
        guard let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return nil }
        let formats = (o["formats"] as? [[String: Any]]) ?? [o]
        let duration = (o["duration"] as? NSNumber)?.doubleValue ?? 0
        func num(_ f: [String: Any], _ k: String) -> Double? { (f[k] as? NSNumber)?.doubleValue }
        func size(_ f: [String: Any]) -> Int64? {
            if let s = num(f, "filesize") ?? num(f, "filesize_approx"), s > 0 { return Int64(s) }
            if let t = num(f, "tbr"), t > 0, duration > 0 { return Int64(t * 125 * duration) }  // kbit/s × seconds
            return nil
        }
        var best: [Int: (rank: Double, size: Int64?, withSound: Bool)] = [:]
        var soundRank = -1.0
        var soundSize: Int64?
        for f in formats {
            let v = f["vcodec"] as? String, a = f["acodec"] as? String
            let hasSound = a != nil && a != "none"
            if v == "none" {  // sound only (storyboard pictures have neither and are skipped)
                guard hasSound else { continue }
                let rank = (a!.hasPrefix("mp4a") ? 1e6 : 0) + (num(f, "abr") ?? num(f, "tbr") ?? 0)
                if rank > soundRank { soundRank = rank; soundSize = size(f) }
                continue
            }
            let h = Int(num(f, "height") ?? 0), w = Int(num(f, "width") ?? 0)
            let side = w > 0 && h > 0 ? min(w, h) : h
            guard side >= 100 else { continue }
            // The format the download picks at this height, in the same order as `format`: frame rate, HDR, widely
            // playable (H.264), then the highest bitrate.
            let hdr = ((f["dynamic_range"] as? String) ?? "SDR") != "SDR" ? 1.0 : 0
            let rank = (num(f, "fps") ?? 0).rounded() * 1e9 + hdr * 1e8 + ((v ?? "").hasPrefix("avc") ? 1e7 : 0) + min(num(f, "tbr") ?? 0, 9e6)
            if let b = best[side], b.rank >= rank { continue }
            best[side] = (rank, size(f), hasSound)
        }
        let video = best.keys.sorted(by: >).map { h -> Quality in
            let b = best[h]!
            return Quality(height: h, size: b.size.map { $0 + (b.withSound ? 0 : soundSize ?? 0) })
        }
        return Choices(title: (o["title"] as? String) ?? "", video: video, soundSize: soundSize)
    }

    /// "1080p", "2160p (4K)".
    public static func qualityName(_ height: Int) -> String {
        switch height {
        case 4320: return "4320p (8K)"
        case 2160: return "2160p (4K)"
        case 1440: return "1440p (2K)"
        default: return "\(height)p"
        }
    }

    /// What a video download is set to save, for lists and windows.
    public static func choiceText(audioOnly: Bool?, maxHeight: Int?) -> String {
        if audioOnly == true { return "sound only" }
        if let h = maxHeight, h > 0 { return "up to \(qualityName(h))" }
        return "best quality"
    }

    /// Title, file type and size from the JSON printed by `infoArguments`.
    public static func info(_ data: Data) -> Info? {
        guard let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any], let title = o["title"] as? String else { return nil }
        func size(_ d: [String: Any]) -> Int64? {
            ((d["filesize"] as? NSNumber) ?? (d["filesize_approx"] as? NSNumber)).map { $0.int64Value }
        }
        var total = size(o)
        if total == nil, let parts = o["requested_formats"] as? [[String: Any]] {
            let sizes = parts.compactMap(size)
            if sizes.count == parts.count, !sizes.isEmpty { total = sizes.reduce(0, +) }
        }
        return Info(title: title, ext: (o["ext"] as? String) ?? "mp4", size: total)
    }

    /// Video details older than this are looked up again (the links inside them expire).
    public static let infoMaxAge: TimeInterval = 2 * 3600

    /// Moves the cookie header, the login and the proxy out of the command line (where any program on the Mac can read
    /// them) into a private options file that yt-dlp loads with --config-locations. Returns the remaining arguments and
    /// the file's text ("" when there is nothing private).
    public static func privateOptions(_ args: [String], proxy: String?) -> (args: [String], config: String) {
        var rest: [String] = []
        var config: [String] = []
        var i = 0
        while i < args.count {
            let a = args[i]
            if a == "--" { rest += args[i...]; break }
            if i + 1 < args.count,
               a == "--username" || a == "--password" || (a == "--add-header" && args[i + 1].lowercased().hasPrefix("cookie:")) {
                config.append("\(a) \(quote(args[i + 1]))")
                i += 2
                continue
            }
            rest.append(a)
            i += 1
        }
        if let proxy { config.append("--proxy \(quote(proxy))") }
        return (rest, config.isEmpty ? "" : config.joined(separator: "\n") + "\n")
    }

    /// Quoting for yt-dlp option files (read like a POSIX shell): 'text' with ' written as '"'"'.
    public static func quote(_ s: String) -> String { "'" + s.replacingOccurrences(of: "'", with: "'\"'\"'") + "'" }

    /// The proxy for yt-dlp: "" = connect directly, nil = system settings.
    public static func proxyURL(_ p: ProxySettings) -> String? {
        let host = p.host.trimmingCharacters(in: .whitespaces)
        if p.mode == .none { return "" }
        guard p.mode == .http || p.mode == .socks, !host.isEmpty else { return nil }
        func esc(_ s: String) -> String { s.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? s }
        let login = p.username.isEmpty ? "" : "\(esc(p.username)):\(esc(p.password))@"
        return "\(p.mode == .socks ? "socks5" : "http")://\(login)\(host):\(p.port)"
    }

    /// The arguments to run, with the private options in a file readable only by this user (delete it when done).
    /// Throws when that file can't be written: never falls back to secrets on the command line.
    public static func prepare(_ args: [String], proxy: ProxySettings?) throws -> (args: [String], configFile: URL?) {
        // The video engine keeps to the chosen network adapter too; it stops if the adapter is down.
        let adapter = Adapters.required
        let source = adapter.isEmpty ? [] : ["--source-address", try Adapters.sourceAddress(adapter)]
        let (rest, config) = privateOptions(source + args, proxy: proxy.flatMap(proxyURL))
        guard !config.isEmpty else { return (rest, nil) }
        try FileManager.default.createDirectory(at: AppPaths.parts, withIntermediateDirectories: true)
        let file = AppPaths.parts.appendingPathComponent("engine-\(UUID().uuidString).conf")
        guard FileManager.default.createFile(atPath: file.path, contents: Data(config.utf8), attributes: [.posixPermissions: 0o600])
        else { throw CocoaError(.fileWriteUnknown, userInfo: [NSFilePathErrorKey: file.path]) }
        return (["--config-locations", file.path] + rest, file)
    }

    /// Kept inside the saved file (needs ffmpeg): title, artist, date and description, and the video's chapters.
    public static let mediaDetails = ["--embed-metadata", "--embed-chapters"]

    /// The command for one video. `folder` gets the finished file, `temp` the partial one (so it can resume).
    public static func arguments(url: URL, folder: String, temp: String, ffmpeg: String?, quickJS: String?, audioOnly: Bool,
                                 referrer: String?, cookies: String?, userAgent: String?, username: String?, password: String?,
                                 speedLimitKBps: Int, infoJSON: String? = nil, maxHeight: Int? = nil) -> [String] {
        var a = [
            "--ignore-config", "--no-playlist", "--progress", "--newline", "--no-colors", "--no-mtime", "--continue",
            "--progress-template", "download:\(progressTag) %(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s %(progress.speed)s %(info.format_id)s %(info.height)s %(info.vcodec)s",
            "--print", "after_move:\(fileTag) %(filepath)s",
            "-P", "home:\(folder)", "-P", "temp:\(temp)",
            "--concurrent-fragments", "4",
            "--js-runtimes", "node",
        ]
        a += naming(url.absoluteString)
        if let quickJS { a += ["--js-runtimes", "quickjs:\(quickJS)"] }
        if let ffmpeg { a += ["--ffmpeg-location", (ffmpeg as NSString).deletingLastPathComponent] }  // folder: ffmpeg and ffprobe
        a += format(audioOnly: audioOnly, maxHeight: maxHeight)
        if ffmpeg != nil { a += mediaDetails }
        if ffmpeg != nil, audioOnly { a += AudioConversion.arguments(AudioConversion.current) }
        if ffmpeg != nil, !audioOnly, UserDefaults.standard.bool(forKey: VideoSubtitles.key) { a += VideoSubtitles.arguments() }
        if let referrer, !referrer.isEmpty { a += ["--referer", referrer] }
        if let userAgent, !userAgent.isEmpty { a += ["--user-agent", userAgent] }
        if let cookies, !cookies.isEmpty { a += ["--add-header", "Cookie:\(cookies)"] }
        if let username, !username.isEmpty { a += ["--username", username, "--password", password ?? ""] }
        if speedLimitKBps > 0 { a += ["--limit-rate", "\(speedLimitKBps)K"] }
        if let infoJSON, let attrs = try? FileManager.default.attributesOfItem(atPath: infoJSON),
           let date = attrs[.modificationDate] as? Date, Date().timeIntervalSince(date) < infoMaxAge {
            a += ["--load-info-json", infoJSON]  // already looked up while the download window was open
        } else {
            a += ["--", url.absoluteString]
        }
        return a
    }
}

/// Where the video tools live: shipped inside the app, copied to a folder the app can keep up to date.
public enum SiteVideoTools {
    public static var bundled: URL? { Bundle.main.executableURL?.deletingLastPathComponent() }
    public static var folder: URL { AppPaths.support.appendingPathComponent("Tools", isDirectory: true) }

    /// The video engine is the unpacked edition (the one-file edition unpacked itself into a new temporary folder on
    /// every run, and macOS checked it each time: about 18 s before every video). It ships packed small in
    /// Resources/yt-dlp.tar.xz, is unpacked once into Tools/yt-dlp-engine-<version> (where it can be updated) and
    /// checked by macOS once.
    static let enginePrefix = "video-engine-"
    static let engineName = "nexonic-video-engine"  // the program inside the engine folder, renamed from the upstream name
    static let upstreamName = "yt-dlp_macos"       // the name in the official release, used only for updates
    static var resources: URL? { bundled?.deletingLastPathComponent().appendingPathComponent("Resources", isDirectory: true) }
    static var bundledArchive: URL? { resources?.appendingPathComponent("video-engine.tar.xz") }
    static var bundledVersion: String? {
        guard let f = resources?.appendingPathComponent("video-engine-version.txt") else { return nil }
        return (try? String(contentsOf: f, encoding: .utf8))?.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// The newest engine ready to use; before the first unpack, the launcher next to the app (unpacks on its own).
    public static var ytdlp: URL {
        if let own = EnginePaths.ytdlp { return URL(fileURLWithPath: own) }  // the user's own, never updated by the app
        if let best = engines().first { return best.dir.appendingPathComponent(engineName) }
        return bundled?.appendingPathComponent(engineName) ?? folder.appendingPathComponent(engineName)
    }

    public static var quickJS: String? {
        guard let p = bundled?.appendingPathComponent("qjs").path, FileManager.default.isExecutableFile(atPath: p) else { return nil }
        return p
    }

    /// "2026.10.26" or "2026.10.26.1" → comparable numbers.
    static func versionParts(_ v: String) -> [Int] { v.trimmingCharacters(in: .whitespacesAndNewlines).split(separator: ".").map { Int($0) ?? 0 } }
    static func newer(_ a: String, than b: String) -> Bool {
        let x = versionParts(a), y = versionParts(b)
        for i in 0..<max(x.count, y.count) {
            let l = i < x.count ? x[i] : 0, r = i < y.count ? y[i] : 0
            if l != r { return l > r }
        }
        return false
    }
    static func version(of dir: URL) -> String? {
        (try? String(contentsOf: dir.appendingPathComponent("VERSION"), encoding: .utf8))?.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Ready engine folders, newest first. A folder counts once its VERSION file is written (the last step).
    static func engines() -> [(dir: URL, version: String)] {
        let fm = FileManager.default
        let names = (try? fm.contentsOfDirectory(atPath: folder.path)) ?? []
        return names.filter { $0.hasPrefix(enginePrefix) && !$0.hasSuffix(".tmp") }.compactMap { n -> (dir: URL, version: String)? in
            let d = folder.appendingPathComponent(n, isDirectory: true)
            guard let v = version(of: d), fm.isExecutableFile(atPath: d.appendingPathComponent(engineName).path) else { return nil }
            return (d, v)
        }.sorted { newer($0.version, than: $1.version) }
    }

    private static let lock = NSLock()
    private static var updating = false

    /// Makes the engine ready (first run, or after an app update brings a newer one) and keeps it current once a day,
    /// because video sites change often.
    public static func prepare() {
        lock.lock()
        defer { lock.unlock() }
        let fm = FileManager.default
        try? fm.createDirectory(at: folder, withIntermediateDirectories: true)
        if let archive = bundledArchive, let bv = bundledVersion, !bv.isEmpty,
           !engines().contains(where: { !newer(bv, than: $0.version) }) {
            install(version: bv) { dir in
                (try? fm.createDirectory(at: dir, withIntermediateDirectories: true)) != nil
                    && run("/usr/bin/tar", ["-xJf", archive.path, "-C", dir.path]) != nil
            }
        }
        // The one-file edition and its stamp from earlier versions.
        // Older layouts: the one-file engine, its stamp, and engine folders under their earlier name.
        for old in ["yt-dlp", "bundled-date"] { try? fm.removeItem(at: folder.appendingPathComponent(old)) }
        for old in ((try? fm.contentsOfDirectory(atPath: folder.path)) ?? []) where old.hasPrefix("yt-dlp-engine-") {
            try? fm.removeItem(at: folder.appendingPathComponent(old))
        }
        let list = engines()
        for extra in list.dropFirst(2) { try? fm.removeItem(at: extra.dir) }  // keep the newest two (one may still be running)
        if let best = list.first { warm(best.dir) }
        let checked = folder.appendingPathComponent("last-update")
        let last = (try? fm.attributesOfItem(atPath: checked.path)[.modificationDate] as? Date) ?? nil
        guard !updating, last.map({ Date().timeIntervalSince($0) > 86_400 }) ?? true else { return }
        fm.createFile(atPath: checked.path, contents: Data())
        updating = true
        let current = list.first?.version ?? "0"
        DispatchQueue.global(qos: .utility).async {
            update(from: current)
            lock.lock(); updating = false; lock.unlock()
        }
    }

    private static let installing = NSLock()

    /// Why `ensureInstalled` failed, for the user.
    public static let missing = "The video engine could not be downloaded. Check the internet connection and try again."

    /// True when the video engine is ready. The app doesn't ship it: the first time a video is saved (or a video link is
    /// looked up), the official release is downloaded, checked against the release's SHA2-256SUMS and only then put in
    /// place; afterwards it keeps itself current. `status` hears about the one-time download.
    @discardableResult
    public static func ensureInstalled(status: ((String) -> Void)? = nil) -> Bool {
        if !engines().isEmpty { return true }
        installing.lock()
        defer { installing.unlock() }
        if !engines().isEmpty { return true }
        status?("Getting the video engine (only the first time)…")
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        update(from: "0")
        return !engines().isEmpty
    }

    /// Adds a new engine to Tools: `fill` puts its files into a new folder; the VERSION file is written last.
    @discardableResult
    static func install(version v: String, fill: (URL) -> Bool) -> Bool {
        let fm = FileManager.default
        let tmp = folder.appendingPathComponent(enginePrefix + v + ".tmp", isDirectory: true)
        let dest = folder.appendingPathComponent(enginePrefix + v, isDirectory: true)
        try? fm.removeItem(at: tmp)
        guard fill(tmp), fm.isExecutableFile(atPath: tmp.appendingPathComponent(engineName).path) else {
            try? fm.removeItem(at: tmp)
            return false
        }
        _ = run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", tmp.path])
        try? fm.removeItem(at: tmp.appendingPathComponent("VERSION"))
        try? fm.removeItem(at: dest)
        guard (try? fm.moveItem(at: tmp, to: dest)) != nil else { try? fm.removeItem(at: tmp); return false }
        return (try? v.write(to: dest.appendingPathComponent("VERSION"), atomically: true, encoding: .utf8)) != nil
    }

    /// The first run of a new engine is slow while macOS checks it; do that now instead of during a download.
    static func warm(_ dir: URL) {
        let mark = dir.appendingPathComponent(".checked")
        guard !FileManager.default.fileExists(atPath: mark.path) else { return }
        if run(dir.appendingPathComponent(engineName).path, ["--version"]) != nil { FileManager.default.createFile(atPath: mark.path, contents: Data()) }
    }

    /// Runs a program and returns what it printed, or nil when it failed.
    static func run(_ tool: String, _ args: [String]) -> String? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = args
        let out = Pipe()
        p.standardOutput = out
        p.standardError = FileHandle.nullDevice
        guard (try? p.run()) != nil else { return nil }
        let data = out.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return p.terminationStatus == 0 ? String(decoding: data, as: UTF8.self) : nil
    }

    /// Fetches a newer official engine (checked against the release's SHA2-256SUMS), unpacks and checks it, then
    /// adds it beside the current one. Any failure leaves the current engine as it is.
    static func update(from current: String) {
        let base = "https://github.com/yt-dlp/yt-dlp/releases"
        guard let latest = URL(string: base + "/latest") else { return }
        var head = URLRequest(url: latest, timeoutInterval: 30)
        head.httpMethod = "HEAD"
        guard let tag = fetch(head)?.response.url?.lastPathComponent, versionParts(tag).count >= 3,
              tag.allSatisfy({ $0.isNumber || $0 == "." }), newer(tag, than: current) else { return }
        let zipName = upstreamName + ".zip"
        guard let sumsURL = URL(string: base + "/download/" + tag + "/SHA2-256SUMS"),
              let zipURL = URL(string: base + "/download/" + tag + "/" + zipName),
              let sums = fetch(URLRequest(url: sumsURL, timeoutInterval: 30))?.data,
              let want = checksum(of: zipName, in: sums),
              let zip = fetch(URLRequest(url: zipURL, timeoutInterval: 300))?.data,
              hex(zip) == want else { return }
        let fm = FileManager.default
        let work = folder.appendingPathComponent("update-\(UUID().uuidString)", isDirectory: true)
        defer { try? fm.removeItem(at: work) }
        let file = work.appendingPathComponent("engine.zip"), out = work.appendingPathComponent("x", isDirectory: true)
        guard (try? fm.createDirectory(at: work, withIntermediateDirectories: true)) != nil, (try? zip.write(to: file)) != nil,
              run("/usr/bin/ditto", ["-x", "-k", file.path, out.path]) != nil,
              let found = findEngine(in: out) else { return }
        let exe = found.deletingLastPathComponent().appendingPathComponent(engineName)
        guard (try? fm.moveItem(at: found, to: exe)) != nil,
              run(exe.path, ["--version"])?.trimmingCharacters(in: .whitespacesAndNewlines) == tag else { return }
        lock.lock()
        let src = exe.deletingLastPathComponent()
        let ok = install(version: tag) { dir in (try? fm.copyItem(at: src, to: dir)) != nil }
        lock.unlock()
        if ok, let dir = engines().first(where: { $0.version == tag })?.dir { warm(dir) }
    }

    static func findEngine(in dir: URL) -> URL? {
        guard let items = FileManager.default.enumerator(at: dir, includingPropertiesForKeys: nil) else { return nil }
        for case let u as URL in items where u.lastPathComponent == upstreamName { return u }
        return nil
    }

    /// The SHA-256 for one file in a SHA2-256SUMS list ("<hash>  <name>" per line).
    static func checksum(of name: String, in sums: Data) -> String? {
        let text = String(decoding: sums, as: UTF8.self)
        for line in text.split(separator: "\n") {
            let parts: [Substring] = line.split(separator: " ", omittingEmptySubsequences: true)
            if parts.count == 2, String(parts[1]) == name { return String(parts[0]).lowercased() }
        }
        return nil
    }

    private static func hex(_ data: Data) -> String { SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined() }

    static func fetch(_ request: URLRequest) -> (data: Data, response: URLResponse)? {
        final class Box: @unchecked Sendable { var value: (data: Data, response: URLResponse)? }
        let done = DispatchSemaphore(value: 0)
        let box = Box()
        URLSession.shared.dataTask(with: request) { d, r, e in
            if e == nil, let d, let r, ((r as? HTTPURLResponse)?.statusCode ?? 0) == 200 { box.value = (d, r) }
            done.signal()
        }.resume()
        done.wait()
        return box.value
    }
}

/// Runs yt-dlp for one video and reports progress; pausing stops it and resuming continues the partial file.
public final class SiteVideoTransfer: Downloader, @unchecked Sendable {
    public var onChange: (@Sendable (DownloadRecord) -> Void)?
    public let ownLimiter = SpeedLimiter()
    private var rec: DownloadRecord
    private let lock = NSLock()
    private var process: Process?
    private var stopping = false
    private var errors = ""
    private var buffer = ""
    private var lastNotify = Date.distantPast
    private var parts = SiteVideo.PartProgress()  // picture and sound pieces added up
    private let sortIntoCategories: Bool
    private let speedLimitKBps: Int

    private var temp: URL { AppPaths.parts.appendingPathComponent(rec.id.uuidString, isDirectory: true) }

    private let proxy: ProxySettings?

    public init(record: DownloadRecord, sortIntoCategories: Bool = false, speedLimitKBps: Int = 0, proxy: ProxySettings? = nil) {
        rec = record
        self.proxy = proxy
        self.sortIntoCategories = sortIntoCategories
        let own = record.speedLimitKBps ?? 0
        self.speedLimitKBps = [own, speedLimitKBps].filter { $0 > 0 }.min() ?? 0
    }

    private func change(_ f: (inout DownloadRecord) -> Void) { lock.lock(); f(&rec); lock.unlock() }

    private func notify(force: Bool = false) {
        let now = Date()
        lock.lock()
        let due = force || now.timeIntervalSince(lastNotify) > 0.25
        if due { lastNotify = now }
        let copy = rec
        lock.unlock()
        if due { onChange?(copy) }
    }

    public func start() {
        DispatchQueue.global().async { self.run() }
    }

    public func pause() {
        lock.lock(); stopping = true; let p = process; lock.unlock()
        change { $0.status = .paused }
        p?.terminate()
        notify(force: true)
    }

    public func discard() {
        lock.lock(); stopping = true; let p = process; lock.unlock()
        p?.terminate()
        try? FileManager.default.removeItem(at: temp)
    }

    private func run() {
        SiteVideoTools.prepare()
        change { $0.status = .probing; $0.error = nil; $0.siteVideo = true; $0.videoStep = nil }
        notify(force: true)
        let r = rec
        guard SiteVideoTools.ensureInstalled(status: { [weak self] step in
            self?.change { $0.videoStep = step }
            self?.notify(force: true)
        }) else { return fail(SiteVideoTools.missing) }
        change { $0.videoStep = nil }
        let folder = CategoryRules.targetFolder(r.folder, fileName: r.audioOnly == true ? "audio.m4a" : "video.mp4",
                                                url: r.url.absoluteString, sortBuiltIn: sortIntoCategories)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try? FileManager.default.createDirectory(at: temp, withIntermediateDirectories: true)
        let p = Process()
        p.executableURL = SiteVideoTools.ytdlp
        let prepared: (args: [String], configFile: URL?)
        do {
            prepared = try SiteVideo.prepare(SiteVideo.arguments(url: r.url, folder: folder.path, temp: temp.path, ffmpeg: FFmpeg.path,
                                              quickJS: SiteVideoTools.quickJS, audioOnly: r.audioOnly == true, referrer: r.referrer,
                                              cookies: r.cookies, userAgent: r.userAgent, username: r.username, password: r.password,
                                              speedLimitKBps: speedLimitKBps, infoJSON: r.infoJSON,
                                              maxHeight: r.audioOnly == true ? nil : r.maxHeight), proxy: proxy)
        } catch {
            return fail("The video engine's private settings could not be written: \(error.localizedDescription)")
        }
        defer { if let f = prepared.configFile { try? FileManager.default.removeItem(at: f) } }
        p.arguments = prepared.args
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
        do { try p.run() } catch { return fail("The video engine could not start: \(error.localizedDescription)") }
        change { $0.status = .downloading }
        notify(force: true)
        p.waitUntilExit()
        out.fileHandleForReading.readabilityHandler = nil
        err.fileHandleForReading.readabilityHandler = nil
        read(out.fileHandleForReading.readDataToEndOfFile())
        lock.lock(); let stopped = stopping; let errText = errors; lock.unlock()
        if stopped { return }
        guard p.terminationStatus == 0, let path = rec.savedPath else { return fail(SiteVideo.error(errText)) }
        let size = (try? FileManager.default.attributesOfItem(atPath: path)[.size] as? Int64) ?? nil
        try? FileManager.default.removeItem(at: temp)
        change {
            $0.fileName = (path as NSString).lastPathComponent
            $0.size = size
            $0.streamBytes = size
            $0.status = .finished
            $0.videoStep = nil
            $0.finished = Date()
        }
        notify(force: true)
    }

    private func read(_ data: Data) {
        guard !data.isEmpty else { return }
        lock.lock()
        buffer += String(decoding: data, as: UTF8.self)
        var lines = buffer.components(separatedBy: "\n")
        buffer = lines.removeLast()
        lock.unlock()
        for line in lines {
            if let pr = SiteVideo.progress(line) {
                lock.lock()
                let sum = parts.add(pr)
                let pieces = parts.pieces
                lock.unlock()
                change {
                    $0.streamBytes = sum.done
                    if let t = sum.total { $0.size = t }
                    $0.videoParts = pieces
                }
                notify()
            } else if let s = SiteVideo.step(line) {
                change { $0.videoStep = s }
                notify(force: true)
            } else if let name = SiteVideo.destinationName(line.trimmingCharacters(in: .whitespacesAndNewlines)) {
                change { $0.fileName = name }
                notify(force: true)
            } else if let path = SiteVideo.savedFile(line.trimmingCharacters(in: .whitespacesAndNewlines)) {
                change { $0.savedPath = path }
            }
        }
    }

    private func fail(_ message: String) {
        change { $0.status = .failed; $0.error = message }
        notify(force: true)
    }
}

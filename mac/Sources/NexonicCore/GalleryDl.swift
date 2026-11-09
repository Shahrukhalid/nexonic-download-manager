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
    public static func arguments(url: String, folder: String, speedLimitKBps: Int, proxyURL: String?, referrer: String?) -> [String] {
        var args = ["--directory", folder]
        if speedLimitKBps > 0 { args += ["--limit-rate", "\(speedLimitKBps)k"] }
        if let proxyURL, !proxyURL.isEmpty { args += ["--proxy", proxyURL] }
        if let referrer, URL(string: referrer)?.scheme?.hasPrefix("http") == true { args += ["-o", "extractor.referer=" + referrer] }
        return args + ["--", url]
    }

    /// A line of the engine's output: a saved file (true), a file it already had ("# path", false), or neither (nil).
    public static func savedFile(_ line: String) -> (saved: Bool, path: String)? {
        let t = line.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.hasPrefix("# /") { return (false, String(t.dropFirst(2))) }
        return t.hasPrefix("/") ? (true, t) : nil
    }
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
            folder = r.folder
            if sortIntoCategories { folder.appendPathComponent(FileCategory.images.rawValue) }
            folder.appendPathComponent(FileNames.sanitize(r.fileName.isEmpty ? "Gallery" : r.fileName), isDirectory: true)
        }
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        change { $0.savedPath = folder.path }
        let p = Process()
        p.executableURL = GalleryDl.program
        p.arguments = GalleryDl.arguments(url: r.url.absoluteString, folder: folder.path, speedLimitKBps: speedLimitKBps,
                                          proxyURL: proxy.flatMap(SiteVideo.proxyURL), referrer: r.referrer)
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

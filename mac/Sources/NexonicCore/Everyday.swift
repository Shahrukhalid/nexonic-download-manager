import Foundation

/// Download limit (Settings → Connection): at most N MB every H hours. When the amount is reached, downloads stop and
/// wait for the next period; they start again by themselves. The amount used is kept across restarts.
public struct DataCap: Codable, Equatable, Sendable {
    public var periodStart: Date?
    public var used: Int64 = 0

    public init() {}

    static func length(_ hours: Int) -> TimeInterval { TimeInterval(min(max(hours, 1), 24 * 31)) * 3600 }

    /// Starts a new period when the old one is over. Returns true when it did.
    @discardableResult
    public mutating func roll(_ now: Date, hours: Int) -> Bool {
        if let start = periodStart, now >= start, now.timeIntervalSince(start) < Self.length(hours) { return false }
        periodStart = now
        used = 0
        return true
    }

    /// Counts received bytes; true when the limit is reached.
    public mutating func add(_ bytes: Int64, limitMB: Int) -> Bool {
        if bytes > 0 { used += bytes }
        return reached(limitMB: limitMB)
    }

    public func reached(limitMB: Int) -> Bool { limitMB > 0 && used >= Int64(limitMB) * 1024 * 1024 }

    public func nextPeriod(hours: Int) -> Date { (periodStart ?? Date()).addingTimeInterval(Self.length(hours)) }

    public static var file: URL { AppPaths.support.appendingPathComponent("data-limit.json") }

    public static func load() -> DataCap {
        (try? Data(contentsOf: file)).flatMap { try? JSONDecoder().decode(DataCap.self, from: $0) } ?? DataCap()
    }

    public func save() {
        try? FileManager.default.createDirectory(at: AppPaths.support, withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(self) { try? data.write(to: Self.file, options: .atomic) }
    }

    /// "512 MB of 2 GB used, resets Tue 14:00".
    public func describe(limitMB: Int, hours: Int) -> String {
        let f = DateFormatter()
        f.setLocalizedDateFormatFromTemplate("EEE HH:mm")
        let size = ByteCountFormatter.string(fromByteCount: used, countStyle: .file)
        let limit = ByteCountFormatter.string(fromByteCount: Int64(limitMB) * 1024 * 1024, countStyle: .file)
        return "\(size) of \(limit) used, resets \(f.string(from: nextPeriod(hours: hours)))"
    }
}

/// The download list's extra columns.
public enum ListColumns {
    /// Where the file is (or will be) saved.
    public static func folder(_ r: DownloadRecord) -> String {
        if let p = r.savedPath, !p.isEmpty {
            return r.gallery == true ? p : (p as NSString).deletingLastPathComponent
        }
        return r.folder.path
    }

    /// "Yes" when a stopped download continues where it left off, "No" when it starts again, "" before that is known.
    public static func resume(_ r: DownloadRecord) -> String {
        if r.status == .finished { return "" }
        if r.torrent != nil || r.siteVideo == true || r.isStream == true || r.gallery == true || r.acceptsRanges { return "Yes" }
        return r.size != nil || r.received > 0 ? "No" : ""
    }
}

/// Portable mode: a file named "portable.txt" next to the app keeps settings and the list in a "Nexonic Data" folder
/// beside it (for a USB drive), instead of the user's Library.
public enum Portable {
    public static func root(executable: URL? = Bundle.main.executableURL) -> URL? {
        guard var u = executable?.resolvingSymlinksInPath() else { return nil }
        while u.path != "/" && !u.path.isEmpty {
            if u.pathExtension == "app" {
                let dir = u.deletingLastPathComponent()
                return FileManager.default.fileExists(atPath: dir.appendingPathComponent("portable.txt").path)
                    ? dir.appendingPathComponent("Nexonic Data", isDirectory: true) : nil
            }
            u = u.deletingLastPathComponent()
        }
        return nil
    }
}

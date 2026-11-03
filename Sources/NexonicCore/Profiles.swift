import Foundation

/// Advanced connection settings (Settings → Connection). The defaults are the values the app always used.
public struct NetworkOptions: Codable, Equatable, Sendable {
    /// Smallest part a file is split into, in KB.
    public var minSegmentKB: Int = 512
    /// Tries per part before the download stops with an error (progress that keeps arriving resets the count).
    public var retries: Int = 6
    /// A part that receives nothing for this long is reconnected.
    public var readTimeoutSeconds: Int = 30

    public init() {}

    enum CodingKeys: String, CodingKey { case minSegmentKB, retries, readTimeoutSeconds }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        minSegmentKB = try c.decodeIfPresent(Int.self, forKey: .minSegmentKB) ?? 512
        retries = try c.decodeIfPresent(Int.self, forKey: .retries) ?? 6
        readTimeoutSeconds = try c.decodeIfPresent(Int.self, forKey: .readTimeoutSeconds) ?? 30
    }

    public var clamped: NetworkOptions {
        var o = self
        o.minSegmentKB = min(max(minSegmentKB, 64), 1024 * 1024)
        o.retries = min(max(retries, 1), 50)
        o.readTimeoutSeconds = min(max(readTimeoutSeconds, 5), 3600)
        return o
    }
}

/// Settings used for every download from one website (Settings → Sites). Nil fields keep the general setting.
public struct HostProfile: Codable, Equatable, Sendable, Identifiable {
    public var id = UUID()
    /// example.com (also matches www.example.com and other subdomains).
    public var host: String = ""
    public var enabled: Bool = true
    public var maxConnections: Int?
    public var speedLimitKBps: Int?
    public var retries: Int?
    public var timeoutSeconds: Int?
    public var userAgent: String?
    public var folder: String?
    public var queueName: String?
    /// False: downloads from this site are added stopped.
    public var startAtOnce: Bool?
    /// Login sent only to this website (never to a site it redirects to). Kept with the app's other secrets, not in settings.json.
    public var username: String?
    public var password: String?
    /// Nil = the proxy from Settings.
    public var proxy: ProxySettings?

    public init(host: String = "") { self.host = host }
}

public enum HostProfiles {
    /// "https://www.Example.com/x" → "www.example.com"; "*.example.com" → "example.com".
    public static func normalize(_ host: String) -> String {
        var h = host.trimmingCharacters(in: .whitespaces).lowercased()
        if h.contains("://"), let u = URL(string: h), let uh = u.host { h = uh }
        h = String(h.split(separator: "/", omittingEmptySubsequences: false).first ?? "")
        if h.hasPrefix("*.") { h = String(h.dropFirst(2)) }
        if let colon = h.lastIndex(of: ":"), !h.contains("]"), Int(h[h.index(after: colon)...]) != nil { h = String(h[..<colon]) }
        return h.trimmingCharacters(in: CharacterSet(charactersIn: "."))
    }

    /// The most specific enabled profile whose host is the link's host or a parent domain of it.
    public static func match(_ profiles: [HostProfile], _ url: URL) -> HostProfile? {
        guard let host = url.host?.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: ".")) else { return nil }
        var best: HostProfile?
        var bestLength = -1
        for p in profiles where p.enabled {
            let h = normalize(p.host)
            guard !h.isEmpty else { continue }
            if (host == h || host.hasSuffix("." + h)) && h.count > bestLength { best = p; bestLength = h.count }
        }
        return best
    }
}

/// Per-download engine settings, from Settings and the site's profile.
public struct TransferOptions: Equatable, Sendable {
    public var minSegment: Int64 = SegmentPlanner.minSegment
    public var retries: Int = 6
    public var readTimeoutSeconds: Int = 30

    public init() {}

    public static func from(_ n: NetworkOptions, _ p: HostProfile?) -> TransferOptions {
        var o = TransferOptions()
        o.minSegment = Int64(min(max(n.minSegmentKB, 64), 1024 * 1024)) * 1024
        o.retries = min(max(p?.retries ?? n.retries, 1), 50)
        o.readTimeoutSeconds = min(max(p?.timeoutSeconds ?? n.readTimeoutSeconds, 5), 3600)
        return o
    }
}

/// Backup of settings, queues and the downloads list (versioned JSON). Passwords, proxy passwords and cookies are never
/// written into a backup.
public enum Backup {
    public static let format = "nexonic-backup-mac"
    public static let version = 1

    public struct File: Codable {
        public var format: String
        public var version: Int
        public var app: String
        public var created: Date
        public var settings: AppSettings?
        public var queues: [DownloadQueue]?
        public var downloads: [DownloadRecord]?
    }

    public enum Problem: Error, LocalizedError {
        case notABackup, newer
        public var errorDescription: String? {
            switch self {
            case .notABackup: return "This is not a Nexonic Download Manager backup for Mac."
            case .newer: return "This backup was made by a newer version of the app. Update the app first."
            }
        }
    }

    public static func export(settings: AppSettings, queues: [DownloadQueue], downloads: [DownloadRecord], app: String) throws -> Data {
        var s = Secrets.stripped(settings)
        for i in s.hostProfiles.indices {
            s.hostProfiles[i].password = nil
            s.hostProfiles[i].proxy?.password = ""
        }
        let list = Secrets.stripped(downloads)
        let file = File(format: format, version: version, app: app, created: Date(), settings: s, queues: queues, downloads: list)
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        return try e.encode(file)
    }

    /// Reads a backup; unfinished downloads start again from the beginning (their parts are not in the backup).
    public static func parse(_ data: Data) throws -> File {
        guard var file = try? JSONDecoder().decode(File.self, from: data), file.format == format else { throw Problem.notABackup }
        guard file.version <= version else { throw Problem.newer }
        file.downloads = Secrets.stripped(file.downloads ?? []).map { r in
            var r = r
            if r.status != .finished {
                r.status = .paused
                r.segments = []
                r.validator = nil
                r.error = nil
            }
            return r
        }
        return file
    }

    /// Downloads from the backup that are not in the list yet (same id, or same link and file name).
    public static func newDownloads(_ file: File, existing: [DownloadRecord]) -> [DownloadRecord] {
        let ids = Set(existing.map(\.id))
        let keys = Set(existing.map { "\($0.url.absoluteString)\n\($0.fileName)" })
        return (file.downloads ?? []).filter { !ids.contains($0.id) && !keys.contains("\($0.url.absoluteString)\n\($0.fileName)") }
    }
}

/// Speed limits offered in the menu bar's quick menu.
public enum SpeedLimits {
    public static let presets = [0, 128, 256, 512, 1024, 2048, 5120, 10240]

    public static func title(_ kb: Int) -> String {
        kb <= 0 ? "No Limit" : kb >= 1024 && kb % 1024 == 0 ? "\(kb / 1024) MB/s" : "\(kb) KB/s"
    }
}

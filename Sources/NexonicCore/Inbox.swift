import Foundation

public struct DownloadJob: Codable, Equatable, Sendable {
    public var url: String
    public var fileName: String?
    public var referrer: String?
    public var cookies: String?
    public var userAgent: String?
    // Optional extras from the command line
    public var folder: String?
    public var queue: String?
    public var start: Bool?
    public var username: String?
    public var password: String?
    public var checksum: String?
    public var speedLimitKBps: Int?
    public var stream: Bool?
    public var site: Bool?
    public var audioOnly: Bool?
    public var maxHeight: Int?  // highest picture quality chosen for a video (nil = best)
    // From the browser extension: the size the browser reported and whether the user clicked the link (shows the download window)
    public var size: Int64?
    public var fromBrowser: Bool?
    public var infoJSON: String?

    public init(url: String, fileName: String? = nil, referrer: String? = nil, cookies: String? = nil, userAgent: String? = nil) {
        self.url = url
        self.fileName = fileName
        self.referrer = referrer
        self.cookies = cookies
        self.userAgent = userAgent
    }

    /// Only plain web links are accepted.
    public var validURL: URL? {
        guard let u = URL(string: url.trimmingCharacters(in: .whitespacesAndNewlines)), let scheme = u.scheme?.lowercased(),
              ["http", "https"].contains(scheme), u.host != nil else {
            return nil
        }
        return Links.unwrap(u)  // e.g. a Google Images result: the picture itself, not Google's page
    }
}

/// A folder where the browser helper drops jobs (one JSON file each, readable only by the user) for the app to collect.
public struct Inbox {
    public let folder: URL

    public init(folder: URL = AppPaths.inbox) { self.folder = folder }

    public func drop(_ job: DownloadJob) throws {
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        let file = folder.appendingPathComponent("\(UUID().uuidString).json")
        try JSONEncoder().encode(job).write(to: file, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: file.path)
    }

    public func collect() -> [DownloadJob] {
        let files = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? []
        var jobs: [DownloadJob] = []
        for file in files.filter({ $0.pathExtension == "json" }).sorted(by: { $0.lastPathComponent < $1.lastPathComponent }) {
            if let data = try? Data(contentsOf: file), let job = try? JSONDecoder().decode(DownloadJob.self, from: data) {
                jobs.append(job)
            }
            try? FileManager.default.removeItem(at: file)
        }
        return jobs
    }
}

public enum AppPaths {
    public static var support: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Nexonic Download Manager", isDirectory: true)
    }
    public static var inbox: URL { support.appendingPathComponent("Inbox", isDirectory: true) }
    public static var parts: URL { support.appendingPathComponent("Parts", isDirectory: true) }
    public static var downloads: URL { support.appendingPathComponent("downloads.json") }
    public static var settings: URL { support.appendingPathComponent("settings.json") }
    public static var queues: URL { support.appendingPathComponent("queues.json") }
}

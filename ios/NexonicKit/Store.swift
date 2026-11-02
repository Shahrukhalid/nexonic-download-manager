import Foundation

public enum JobStatus: String, Codable, Sendable {
    case queued, downloading, paused, later, complete, failed
}

/// One download. Logins are not kept here: they are in the Keychain.
public struct DownloadJob: Codable, Identifiable, Equatable, Sendable {
    public var id: UUID
    public var url: URL
    public var fileName: String
    public var status: JobStatus
    public var size: Int64?
    public var written: Int64
    public var error: String?
    public var referer: URL?
    public var userAgent: String?
    public var quality: String?
    public var addedAt: Date
    public var finishedAt: Date?
    /// Which background session runs it (the app's or the Share extension's).
    public var sessionID: String?
    /// A finished file waiting in the shared folder until the app moves it to Files.
    public var incomingPath: String?
    /// Where it is in Files, relative to the app's Documents folder.
    public var savedPath: String?

    public init(id: UUID = UUID(), url: URL, fileName: String, status: JobStatus = .queued, size: Int64? = nil,
                referer: URL? = nil, userAgent: String? = nil, quality: String? = nil) {
        self.id = id; self.url = url; self.fileName = FileNames.sanitize(fileName); self.status = status; self.size = size
        self.written = 0; self.referer = referer; self.userAgent = userAgent; self.quality = quality; self.addedAt = Date()
    }

    public var category: FileCategory { FileCategory(fileName: fileName) }
    public var progress: Double { guard let size, size > 0 else { return 0 } ; return min(1, Double(written) / Double(size)) }
    public var isActive: Bool { status == .downloading || status == .queued }
}

/// The download list in the shared folder. Reads and writes are coordinated, because the app and the
/// Share extension may change it at the same time.
public final class Store {
    public static let shared = Store()
    private let queue = DispatchQueue(label: "com.nexonic.store")
    public var url: URL { AppGroup.folder.appendingPathComponent("downloads.json") }

    public init() {}

    private func read(_ u: URL) -> [DownloadJob] {
        guard let data = try? Data(contentsOf: u) else { return [] }
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        if let jobs = try? d.decode([DownloadJob].self, from: data) { return jobs }
        // A damaged list is kept as a copy, never silently replaced.
        try? FileManager.default.moveItem(at: u, to: u.deletingLastPathComponent()
            .appendingPathComponent("downloads.unreadable-\(Int(Date().timeIntervalSince1970)).json"))
        return []
    }

    public func load() -> [DownloadJob] {
        var jobs: [DownloadJob] = []
        var err: NSError?
        NSFileCoordinator().coordinate(readingItemAt: url, options: [], error: &err) { u in jobs = read(u) }
        return jobs
    }

    /// Read, change and write back in one coordinated step; tells the other process.
    public func update(_ change: (inout [DownloadJob]) -> Void) {
        queue.sync {
            var err: NSError?
            NSFileCoordinator().coordinate(writingItemAt: url, options: .forMerging, error: &err) { u in
                var jobs = read(u)
                change(&jobs)
                let e = JSONEncoder()
                e.dateEncodingStrategy = .iso8601
                if let data = try? e.encode(jobs) { try? data.write(to: u, options: .atomic) }
            }
        }
        Changes.post()
    }

    public func update(_ id: UUID, _ change: (inout DownloadJob) -> Void) {
        update { jobs in if let i = jobs.firstIndex(where: { $0.id == id }) { change(&jobs[i]) } }
    }

    public func job(_ id: UUID) -> DownloadJob? { load().first { $0.id == id } }

    /// Adds a download unless the same link is already in progress (then that one is returned).
    @discardableResult
    public func add(_ job: DownloadJob) -> DownloadJob {
        var result = job
        update { jobs in
            if let existing = jobs.first(where: { $0.url == job.url && $0.status != .complete && $0.status != .failed }) {
                result = existing
            } else {
                jobs.insert(job, at: 0)
            }
        }
        return result
    }

    // Resume data of paused downloads.
    private func resumeURL(_ id: UUID) -> URL {
        let dir = AppGroup.folder.appendingPathComponent("Resume", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent(id.uuidString)
    }
    public func saveResumeData(_ data: Data, for id: UUID) { try? data.write(to: resumeURL(id), options: .atomic) }
    public func takeResumeData(_ id: UUID) -> Data? {
        let u = resumeURL(id)
        defer { try? FileManager.default.removeItem(at: u) }
        return try? Data(contentsOf: u)
    }
    public func dropResumeData(_ id: UUID) { try? FileManager.default.removeItem(at: resumeURL(id)) }
}

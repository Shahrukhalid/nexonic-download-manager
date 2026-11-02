import Foundation

public enum DownloadStatus: String, Codable, Sendable {
    case queued, probing, downloading, paused, finished, failed

    public var isActive: Bool { self == .probing || self == .downloading }
}

/// One piece of a video from a video site (the picture or the sound), shown in the progress window.
public struct VideoPart: Codable, Equatable, Sendable {
    public var name: String
    public var done: Int64
    public var total: Int64?

    public init(name: String, done: Int64, total: Int64?) {
        self.name = name
        self.done = done
        self.total = total
    }
}

/// One byte range of a download. `end` is inclusive; -1 means "until the server stops" (size unknown).
public struct Segment: Codable, Equatable, Sendable {
    public var id: Int
    public var start: Int64
    public var end: Int64
    public var written: Int64

    public init(id: Int, start: Int64, end: Int64, written: Int64 = 0) {
        self.id = id
        self.start = start
        self.end = end
        self.written = written
    }

    public var length: Int64? { end < 0 ? nil : end - start + 1 }
    public var remaining: Int64 { end < 0 ? Int64.max : max(0, end - start + 1 - written) }
    public var isComplete: Bool { end >= 0 && written >= end - start + 1 }
}

public struct DownloadRecord: Codable, Identifiable, Sendable {
    public var id: UUID
    public var url: URL
    public var referrer: String?
    public var cookies: String?
    public var userAgent: String?
    public var fileName: String
    public var folder: URL
    public var size: Int64?
    public var acceptsRanges: Bool
    public var status: DownloadStatus
    public var segments: [Segment]
    public var added: Date
    public var finished: Date?
    public var error: String?
    public var savedPath: String?
    // Added in 0.2.0 (optional so older lists still load)
    public var queueID: String?
    public var username: String?
    public var password: String?
    public var checksum: String?
    public var checksumResult: String?
    public var speedLimitKBps: Int?
    public var awaitingRefresh: Bool?
    // Added in 0.3.0: streamed video
    public var isStream: Bool?
    public var streamDone: Int?
    public var streamTotal: Int?
    public var streamBytes: Int64?
    // Added in 0.4.0: videos from YouTube and other sites
    public var siteVideo: Bool?
    public var audioOnly: Bool?
    // Added in 0.5.5: video details looked up while the download window was open (path of a yt-dlp info file)
    public var infoJSON: String?
    // Added in 0.6.0: the picture and sound pieces of a video, and the step after downloading (joining them)
    public var videoParts: [VideoPart]?
    public var videoStep: String?
    // Added in 0.6.2: the file's ETag or Last-Modified when the download started, so a changed file is never resumed into
    public var validator: String?
    // Added in 0.6.3: the highest picture quality chosen for a video (nil = the best on offer)
    public var maxHeight: Int?

    public init(url: URL, folder: URL, fileName: String? = nil, referrer: String? = nil, cookies: String? = nil,
                userAgent: String? = nil) {
        id = UUID()
        self.url = url
        self.folder = folder
        self.fileName = fileName.map(FileNames.sanitize) ?? FileNames.fromURL(url)
        self.referrer = referrer
        self.cookies = cookies
        self.userAgent = userAgent
        size = nil
        acceptsRanges = false
        status = .queued
        segments = []
        added = Date()
    }

    public var received: Int64 { isStream == true || siteVideo == true ? (streamBytes ?? 0) : segments.reduce(0) { $0 + $1.written } }
    public var progress: Double {
        if isStream == true {
            guard let total = streamTotal, total > 0 else { return 0 }
            return min(1, Double(streamDone ?? 0) / Double(total))
        }
        guard let size, size > 0 else { return 0 }
        return min(1, Double(received) / Double(size))
    }
    public var category: FileCategory { FileCategory(fileName: fileName) }
    public var queue: String { queueID ?? DownloadQueue.mainID }
}

public enum AfterQueue: String, Codable, CaseIterable, Sendable {
    case nothing, quit, sleep, shutDown

    public var title: String {
        switch self {
        case .nothing: return "Do nothing"
        case .quit: return "Quit the app"
        case .sleep: return "Put the computer to sleep"
        case .shutDown: return "Shut down the computer"
        }
    }
}

/// A list of downloads with its own limit and timetable. The main queue starts new downloads straight away.
public struct DownloadQueue: Codable, Identifiable, Equatable, Sendable {
    public static let mainID = "main"

    public var id: String
    public var name: String
    public var running: Bool
    public var maxActive: Int
    public var startEnabled: Bool
    public var startTime: String  // "HH:mm"
    public var stopEnabled: Bool
    public var stopTime: String
    public var days: [Int]  // 1 = Monday … 7 = Sunday
    public var afterDone: AfterQueue
    public var lastStartDay: String?

    public init(id: String = UUID().uuidString, name: String, running: Bool = false, maxActive: Int = 3) {
        self.id = id
        self.name = name
        self.running = running
        self.maxActive = maxActive
        startEnabled = false
        startTime = "02:00"
        stopEnabled = false
        stopTime = "08:00"
        days = [1, 2, 3, 4, 5, 6, 7]
        afterDone = .nothing
    }

    public static var main: DownloadQueue { DownloadQueue(id: mainID, name: "Main queue", running: true) }
    public var isMain: Bool { id == DownloadQueue.mainID }
}

/// Decides when a queue's timetable starts or stops it.
public enum QueueClock {
    /// Monday = 1 … Sunday = 7.
    public static func weekday(_ date: Date, calendar: Calendar = .current) -> Int {
        let w = calendar.component(.weekday, from: date)  // Sunday = 1
        return w == 1 ? 7 : w - 1
    }

    public static func hhmm(_ date: Date, calendar: Calendar = .current) -> String {
        let c = calendar.dateComponents([.hour, .minute], from: date)
        return String(format: "%02d:%02d", c.hour ?? 0, c.minute ?? 0)
    }

    public static func dayKey(_ date: Date, calendar: Calendar = .current) -> String {
        let c = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", c.year ?? 0, c.month ?? 0, c.day ?? 0)
    }

    public static func shouldStart(_ q: DownloadQueue, at date: Date, calendar: Calendar = .current) -> Bool {
        guard q.startEnabled, !q.running, q.days.contains(weekday(date, calendar: calendar)),
              q.lastStartDay != dayKey(date, calendar: calendar) else { return false }
        return hhmm(date, calendar: calendar) == q.startTime
    }

    public static func shouldStop(_ q: DownloadQueue, at date: Date, calendar: Calendar = .current) -> Bool {
        q.stopEnabled && q.running && hhmm(date, calendar: calendar) == q.stopTime
    }
}

public enum ProxyMode: String, Codable, CaseIterable, Sendable {
    case system, none, http, socks

    public var title: String {
        switch self {
        case .system: return "Use the system settings"
        case .none: return "No proxy"
        case .http: return "HTTP proxy"
        case .socks: return "SOCKS5 proxy"
        }
    }
}

public struct ProxySettings: Codable, Equatable, Sendable {
    public var mode: ProxyMode = .system
    public var host: String = ""
    public var port: Int = 8080
    public var username: String = ""
    public var password: String = ""

    public init() {}
}

public struct AppSettings: Codable, Equatable, Sendable {
    public var connections: Int = 8
    public var maxActive: Int = 3
    public var speedLimitKBps: Int = 0
    public var folder: URL = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask)[0]
    /// Each type in its own folder (Video, Music, Documents…), like IDM. On by default since 0.6.6 (saved under a new
    /// name, so earlier installs get it too).
    public var sortIntoCategoryFolders: Bool = true
    public var notifications: Bool = true
    public var monitorClipboard: Bool = true
    public var clipboardTypes: String = FileTypes.defaultList
    public var proxy = ProxySettings()
    public var showCompleteDialog: Bool = true
    public var playSound: Bool = true
    public var markDownloads: Bool = true
    public var scanEnabled: Bool = false
    public var scanCommand: String = ""
    // Added in 0.5.0: download and progress windows
    public var showDownloadDialog: Bool = true
    public var showProgressWindow: Bool = true
    public var bringToFrontOnBrowserDownload: Bool = false
    // Added in 0.6.0: in-app updates
    public var checkForUpdates: Bool = true
    public var skippedVersion: String = ""

    public init() {}

    enum CodingKeys: String, CodingKey {
        case connections, maxActive, speedLimitKBps, folder, sortIntoCategoryFolders = "categoryFolders", notifications, monitorClipboard,
             clipboardTypes, proxy, showCompleteDialog, playSound, markDownloads, scanEnabled, scanCommand,
             showDownloadDialog, showProgressWindow, bringToFrontOnBrowserDownload, checkForUpdates, skippedVersion
    }

    /// Missing keys keep their defaults, so settings from older versions still load.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = AppSettings()
        connections = try c.decodeIfPresent(Int.self, forKey: .connections) ?? d.connections
        maxActive = try c.decodeIfPresent(Int.self, forKey: .maxActive) ?? d.maxActive
        speedLimitKBps = try c.decodeIfPresent(Int.self, forKey: .speedLimitKBps) ?? d.speedLimitKBps
        folder = try c.decodeIfPresent(URL.self, forKey: .folder) ?? d.folder
        sortIntoCategoryFolders = try c.decodeIfPresent(Bool.self, forKey: .sortIntoCategoryFolders) ?? d.sortIntoCategoryFolders
        notifications = try c.decodeIfPresent(Bool.self, forKey: .notifications) ?? d.notifications
        monitorClipboard = try c.decodeIfPresent(Bool.self, forKey: .monitorClipboard) ?? d.monitorClipboard
        clipboardTypes = try c.decodeIfPresent(String.self, forKey: .clipboardTypes) ?? d.clipboardTypes
        proxy = try c.decodeIfPresent(ProxySettings.self, forKey: .proxy) ?? d.proxy
        showCompleteDialog = try c.decodeIfPresent(Bool.self, forKey: .showCompleteDialog) ?? d.showCompleteDialog
        playSound = try c.decodeIfPresent(Bool.self, forKey: .playSound) ?? d.playSound
        markDownloads = try c.decodeIfPresent(Bool.self, forKey: .markDownloads) ?? d.markDownloads
        scanEnabled = try c.decodeIfPresent(Bool.self, forKey: .scanEnabled) ?? d.scanEnabled
        scanCommand = try c.decodeIfPresent(String.self, forKey: .scanCommand) ?? d.scanCommand
        showDownloadDialog = try c.decodeIfPresent(Bool.self, forKey: .showDownloadDialog) ?? d.showDownloadDialog
        showProgressWindow = try c.decodeIfPresent(Bool.self, forKey: .showProgressWindow) ?? d.showProgressWindow
        bringToFrontOnBrowserDownload = try c.decodeIfPresent(Bool.self, forKey: .bringToFrontOnBrowserDownload)
            ?? d.bringToFrontOnBrowserDownload
        checkForUpdates = try c.decodeIfPresent(Bool.self, forKey: .checkForUpdates) ?? d.checkForUpdates
        skippedVersion = try c.decodeIfPresent(String.self, forKey: .skippedVersion) ?? d.skippedVersion
    }
}

/// File types that count as downloads when a link is copied.
public enum FileTypes {
    public static let defaultList =
        "zip rar 7z gz tgz bz2 xz tar iso dmg pkg exe msi apk deb rpm mp3 m4a aac flac wav ogg mp4 mkv mov avi webm m4v wmv flv pdf epub doc docx xls xlsx ppt pptx"

    public static func parse(_ text: String) -> Set<String> {
        Set(text.lowercased().split(whereSeparator: { " ,;\n\t".contains($0) }).map { $0.hasPrefix(".") ? String($0.dropFirst()) : String($0) })
    }

    /// A copied text counts when it is one web link to a file of a listed type.
    public static func downloadLink(in text: String, types: Set<String>) -> URL? {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.contains(where: \.isWhitespace), let job = DownloadJob(url: t).validURL else { return nil }
        let ext = (job.path as NSString).pathExtension.lowercased()
        return types.contains(ext) ? job : nil
    }
}

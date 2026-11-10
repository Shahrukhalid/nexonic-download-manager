import Foundation

/// Download limit (Settings → Downloads): at most N MB every H hours, counted while the app is open. When the amount is
/// reached, downloads stop and continue by themselves when the next period starts.
public struct DataCap: Codable, Equatable, Sendable {
    public var periodStart: Date?
    public var used: Int64 = 0

    public init(periodStart: Date? = nil, used: Int64 = 0) { self.periodStart = periodStart; self.used = used }

    static func length(_ hours: Int) -> TimeInterval { TimeInterval(min(max(hours, 1), 24 * 31)) * 3600 }

    /// Starts a new period when the old one is over. Returns true when it did.
    @discardableResult
    public mutating func roll(_ now: Date, hours: Int) -> Bool {
        if let start = periodStart, now >= start, now.timeIntervalSince(start) < Self.length(hours) { return false }
        periodStart = now
        used = 0
        return true
    }

    public mutating func add(_ bytes: Int64) { if bytes > 0 { used += bytes } }
    public func reached(limitMB: Int) -> Bool { limitMB > 0 && used >= Int64(limitMB) * 1024 * 1024 }
    public func nextPeriod(hours: Int) -> Date { (periodStart ?? Date()).addingTimeInterval(Self.length(hours)) }

    public static var saved: DataCap {
        get { (AppGroup.defaults.data(forKey: "dataCap")).flatMap { try? JSONDecoder().decode(DataCap.self, from: $0) } ?? DataCap() }
        set { AppGroup.defaults.set(try? JSONEncoder().encode(newValue), forKey: "dataCap") }
    }
}

extension SharedSettings {
    /// Download limit: at most this many MB (0 = no limit) every `dataCapHours` hours.
    public static var dataCapMB: Int {
        get { AppGroup.defaults.integer(forKey: "dataCapMB") }
        set { AppGroup.defaults.set(max(0, newValue), forKey: "dataCapMB") }
    }
    public static var dataCapHours: Int {
        get { AppGroup.defaults.object(forKey: "dataCapHours") as? Int ?? 24 }
        set { AppGroup.defaults.set(min(max(newValue, 1), 24 * 31), forKey: "dataCapHours") }
    }
}

/// Link Grabber: the web links in any pasted text (messages, notes, web pages).
public enum LinkFinder {
    public static func find(_ text: String) -> [URL] {
        guard let detector = try? NSDataDetector(types: NSTextCheckingResult.CheckingType.link.rawValue) else { return [] }
        var seen = Set<String>()
        var out: [URL] = []
        for m in detector.matches(in: text, range: NSRange(text.startIndex..., in: text)) {
            guard let u = m.url, let s = u.scheme?.lowercased(), s == "http" || s == "https", seen.insert(u.absoluteString).inserted else { continue }
            out.append(u)
            if out.count >= 5000 { break }
        }
        return out
    }
}

/// Crash reports kept on this device (Settings → Crash Reports). Never sent anywhere; the newest 20 are kept.
public enum CrashReports {
    public static var folder: URL { AppGroup.folder.appendingPathComponent("Crash Reports", isDirectory: true) }

    public static func scrub(_ text: String) -> String {
        text.replacingOccurrences(of: #"(?i)\b(?:https?|magnet|ftp):[^\s"'<>]+"#, with: "[link]", options: .regularExpression)
    }

    public static func text(reason: String, stack: [String], version: String, at date: Date = Date()) -> String {
        "Nexonic Download Manager crash report\nTime: \(ISO8601DateFormatter().string(from: date))\nApp version: \(version)\n"
            + "System: iOS \(ProcessInfo.processInfo.operatingSystemVersionString)\n\n\(scrub(reason))\n\n\(stack.joined(separator: "\n"))"
    }

    public static func write(_ text: String) {
        let fm = FileManager.default
        try? fm.createDirectory(at: folder, withIntermediateDirectories: true)
        let name = "crash-\(Int(Date().timeIntervalSince1970)).txt"
        try? text.write(to: folder.appendingPathComponent(name), atomically: true, encoding: .utf8)
        let old = ((try? fm.contentsOfDirectory(atPath: folder.path)) ?? []).filter { $0.hasPrefix("crash-") }.sorted(by: >).dropFirst(20)
        for n in old { try? fm.removeItem(at: folder.appendingPathComponent(n)) }
    }

    public static var latest: URL? {
        ((try? FileManager.default.contentsOfDirectory(atPath: folder.path)) ?? []).filter { $0.hasPrefix("crash-") }.max()
            .map { folder.appendingPathComponent($0) }
    }

    /// Writes a report for Objective-C exceptions that would stop the app.
    public static func install() {
        NSSetUncaughtExceptionHandler { e in
            let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
            CrashReports.write(CrashReports.text(reason: "\(e.name.rawValue): \(e.reason ?? "")", stack: e.callStackSymbols, version: version))
        }
    }
}

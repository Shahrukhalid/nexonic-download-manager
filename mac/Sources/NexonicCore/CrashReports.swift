import Foundation

/// Help → Crash Reports: when the app stops because of an error, what happened is written to a text file on this Mac
/// (never sent anywhere). The newest 20 are kept. Crashes macOS catches itself are in ~/Library/Logs/DiagnosticReports.
public enum CrashReports {
    public static var folder: URL { AppPaths.support.appendingPathComponent("Crash Reports", isDirectory: true) }
    static let keep = 20
    static var appVersion = "dev"

    /// Removes web addresses from error texts (they can hold access keys).
    public static func scrub(_ text: String) -> String {
        text.replacingOccurrences(of: #"(?i)\b(?:https?|magnet|ftp):[^\s"'<>]+"#, with: "[link]", options: .regularExpression)
    }

    public static func text(reason: String, stack: [String], version: String, at date: Date) -> String {
        let f = ISO8601DateFormatter()
        let os = ProcessInfo.processInfo.operatingSystemVersionString
        return """
        Nexonic Download Manager crash report
        Time: \(f.string(from: date))
        App version: \(version)
        System: macOS \(os)

        \(scrub(reason))

        \(stack.joined(separator: "\n"))
        """
    }

    @discardableResult
    public static func write(reason: String, stack: [String], at date: Date = Date()) -> URL? {
        let fm = FileManager.default
        try? fm.createDirectory(at: folder, withIntermediateDirectories: true)
        let f = DateFormatter()
        f.dateFormat = "yyyyMMdd-HHmmss"
        f.locale = Locale(identifier: "en_US_POSIX")
        let url = folder.appendingPathComponent("crash-\(f.string(from: date))-\(UUID().uuidString.prefix(6)).txt")
        guard (try? text(reason: reason, stack: stack, version: appVersion, at: date).write(to: url, atomically: true, encoding: .utf8)) != nil else { return nil }
        let old = ((try? fm.contentsOfDirectory(atPath: folder.path)) ?? []).filter { $0.hasPrefix("crash-") }.sorted(by: >).dropFirst(keep)
        for name in old { try? fm.removeItem(at: folder.appendingPathComponent(name)) }
        return url
    }

    /// Writes a report for Objective-C exceptions that would stop the app.
    public static func install(version: String) {
        appVersion = version
        NSSetUncaughtExceptionHandler { e in
            CrashReports.write(reason: "\(e.name.rawValue): \(e.reason ?? "")", stack: e.callStackSymbols)
        }
    }
}

import Foundation

/// Sizes, speeds and dates the way the user chose to see them (Settings → General). Kept in the app's preferences.
public enum Units {
    public static let sizeKey = "sizeUnits", speedKey = "speedUnits", dateKey = "dateStyle"
    public static let sizeStyles = ["binary", "iec", "decimal"]
    public static let speedStyles = ["bytes", "bits"]
    public static let dateStyles = ["absolute", "relative"]

    static func value(_ key: String, _ allowed: [String]) -> String {
        let v = UserDefaults.standard.string(forKey: key) ?? ""
        return allowed.contains(v) ? v : allowed[0]
    }
    public static var sizeStyle: String { value(sizeKey, sizeStyles) }
    public static var speedStyle: String { value(speedKey, speedStyles) }
    public static var dateStyle: String { value(dateKey, dateStyles) }

    /// "1.5 MB" (1024), "1.5 MiB" (1024) or "1.6 MB" (1000).
    public static func size(_ bytes: Int64, style: String? = nil) -> String {
        let style = style ?? sizeStyle
        let step = style == "decimal" ? 1000.0 : 1024.0
        let u = style == "iec" ? ["B", "KiB", "MiB", "GiB", "TiB", "PiB"] : ["B", "KB", "MB", "GB", "TB", "PB"]
        var v = Double(max(0, bytes)), i = 0
        while v >= step && i < u.count - 1 { v /= step; i += 1 }
        return "\(number(v)) \(u[i])"
    }

    /// "2 MB/s" or "16.8 Mbit/s"; bits always count in thousands.
    public static func speed(_ bytesPerSecond: Double, style: String? = nil, sizeStyle: String? = nil) -> String {
        guard (style ?? speedStyle) == "bits" else { return size(Int64(bytesPerSecond), style: sizeStyle) + "/s" }
        let u = ["bit/s", "kbit/s", "Mbit/s", "Gbit/s", "Tbit/s"]
        var v = max(0, bytesPerSecond) * 8, i = 0
        while v >= 1000 && i < u.count - 1 { v /= 1000; i += 1 }
        return "\(number(v)) \(u[i])"
    }

    static func number(_ v: Double) -> String {
        let f = NumberFormatter()
        f.maximumFractionDigits = 1
        f.minimumFractionDigits = 0
        f.locale = Locale(identifier: "en_US_POSIX")
        return f.string(from: NSNumber(value: (v * 10).rounded() / 10)) ?? "\(v)"
    }

    /// Relative: "Just now", "5 min ago", "3 h ago", "Yesterday 14:05", "4 days ago"; the date after a week (and for times
    /// in the future). Absolute: the computer's short date and time.
    public static func date(_ d: Date?, style: String? = nil, now: Date = Date(), calendar: Calendar = .current) -> String {
        guard let d else { return "" }
        let absolute = d.formatted(date: .abbreviated, time: .shortened)
        guard (style ?? dateStyle) == "relative" else { return absolute }
        let ago = now.timeIntervalSince(d)
        if ago < 0 { return absolute }
        if ago < 60 { return String(localized: "Just now") }
        if ago < 3600 { return String(format: String(localized: "%d min ago"), Int(ago / 60)) }
        if calendar.isDate(d, inSameDayAs: now) { return String(format: String(localized: "%d h ago"), Int(ago / 3600)) }
        let days = calendar.dateComponents([.day], from: calendar.startOfDay(for: d), to: calendar.startOfDay(for: now)).day ?? 99
        if days == 1 { return String(localized: "Yesterday") + " " + d.formatted(date: .omitted, time: .shortened) }
        return days < 7 ? String(format: String(localized: "%d days ago"), days) : absolute
    }
}

/// Finished downloads whose file was deleted or moved outside the app.
public enum MissingFiles {
    public static let removeOnStartKey = "removeMissingOnStart"
    public static func isMissing(_ r: DownloadRecord) -> Bool {
        guard r.status == .finished, let p = r.savedPath, !p.isEmpty else { return false }
        return !FileManager.default.fileExists(atPath: p)
    }
}

/// Filters links: words find links that contain them, * and ? are wildcards, and a leading ! leaves links out.
public enum LinkFilter {
    public static func apply(_ links: [String], _ filter: String) -> [String] {
        let terms = filter.split(separator: " ").map(String.init)
        let include = terms.filter { !$0.hasPrefix("!") }
        let exclude = terms.filter { $0.hasPrefix("!") && $0.count > 1 }.map { String($0.dropFirst()) }
        return links.filter { l in (include.isEmpty || include.contains { matches(l, $0) }) && !exclude.contains { matches(l, $0) } }
    }

    public static func matches(_ link: String, _ term: String) -> Bool {
        guard term.contains("*") || term.contains("?") else { return link.range(of: term, options: .caseInsensitive) != nil }
        let rx = "^" + NSRegularExpression.escapedPattern(for: term).replacingOccurrences(of: "\\*", with: ".*").replacingOccurrences(of: "\\?", with: ".") + "$"
        guard let re = try? NSRegularExpression(pattern: rx, options: .caseInsensitive) else { return false }
        func ok(_ s: String) -> Bool { re.firstMatch(in: s, range: NSRange(location: 0, length: (s as NSString).length)) != nil }
        let path = URL(string: link)?.path ?? link
        return ok(link) || ok(path) || ok((path as NSString).lastPathComponent)
    }
}

/// Sound-only downloads converted after downloading (needs FFmpeg); "original" keeps the site's format.
public enum AudioConversion {
    public static let key = "audioFormat"
    public static let formats = ["original", "mp3", "opus", "flac"]
    public static var current: String { EnginePaths.defaults.string(forKey: key).flatMap { formats.contains($0) ? $0 : nil } ?? "original" }
    public static func arguments(_ format: String) -> [String] {
        switch format {
        case "mp3": return ["-x", "--audio-format", "mp3", "--audio-quality", "0"]
        case "opus": return ["-x", "--audio-format", "opus"]
        case "flac": return ["-x", "--audio-format", "flac"]
        default: return []
        }
    }
}

/// The user's own yt-dlp and FFmpeg programs (Settings → Video tools); empty = the ones the app ships and keeps current.
public enum EnginePaths {
    public static let ytdlpKey = "customYtDlp", ffmpegKey = "customFFmpeg"
    static let appID = "com.nexonic.downloadmanager"
    /// The app's preferences, also when read by the browser helper (a separate program with preferences of its own).
    static var defaults: UserDefaults {
        Bundle.main.bundleIdentifier == appID ? .standard : (UserDefaults(suiteName: appID) ?? .standard)
    }
    static func usable(_ key: String) -> String? {
        let p = (defaults.string(forKey: key) ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        return !p.isEmpty && FileManager.default.isExecutableFile(atPath: p) ? p : nil
    }
    public static var ytdlp: String? { usable(ytdlpKey) }
    public static var ffmpeg: String? { usable(ffmpegKey) }
    /// Why a path set by the user can't be used, or nil when it is empty or fine.
    public static func problem(_ path: String) -> String? {
        let p = path.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !p.isEmpty else { return nil }
        return FileManager.default.isExecutableFile(atPath: p) ? nil : "Not a program this Mac can run: \(p)"
    }
}

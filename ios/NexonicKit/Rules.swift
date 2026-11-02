import Foundation

/// The desktop apps' network rules (Sources/NexonicCore/Transfer.swift `Network`), for iOS.
public enum Net {
    static func port(_ u: URL) -> Int { u.port ?? (u.scheme?.lowercased() == "https" ? 443 : 80) }

    /// Same server and no step down from https: cookies, logins and the referrer may be sent there.
    public static func sameSite(_ url: URL, _ origin: URL) -> Bool {
        let downgrade = origin.scheme?.lowercased() == "https" && url.scheme?.lowercased() != "https"
        return url.host?.lowercased() == origin.host?.lowercased() && port(url) == port(origin) && !downgrade
    }

    /// A login challenge from the download's own server only.
    public static func sameSpace(host: String, port: Int, proto: String?, origin: URL) -> Bool {
        let scheme = origin.scheme?.lowercased() ?? ""
        return host.lowercased() == origin.host?.lowercased() && port == Net.port(origin)
            && !(scheme == "https" && proto?.lowercased() != "https")
    }

    /// Only plain web links (never file:, data:, javascript: and the like).
    public static func webURL(_ text: String) -> URL? {
        guard let u = URL(string: text.trimmingCharacters(in: .whitespacesAndNewlines)),
              let s = u.scheme?.lowercased(), s == "http" || s == "https", u.host?.isEmpty == false else { return nil }
        return u
    }

    /// Total size from "bytes 0-0/12345".
    public static func total(contentRange: String?) -> Int64? {
        guard let cr = contentRange, let slash = cr.lastIndex(of: "/") else { return nil }
        return Int64(cr[cr.index(after: slash)...].trimmingCharacters(in: .whitespaces))
    }
}

public enum Media {
    public static let video: Set<String> = ["mp4", "m4v", "webm", "mkv", "mov", "flv", "avi", "3gp"]
    public static let audio: Set<String> = ["mp3", "m4a", "aac", "ogg", "oga", "opus", "wav", "flac"]
    public static let streams: Set<String> = ["m3u8", "mpd", "ts", "m4s"]
    public static let files: Set<String> = [
        "zip", "rar", "7z", "gz", "tgz", "bz2", "xz", "tar", "apk", "ipa", "exe", "msi", "dmg", "pkg", "iso", "img", "deb",
        "pdf", "epub", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "csv", "key", "pages", "numbers",
    ]

    public static func ext(_ url: URL) -> String { url.pathExtension.lowercased() }
    public static func isStream(_ url: URL) -> Bool { streams.contains(ext(url)) }
    public static func isMedia(_ url: URL) -> Bool { video.contains(ext(url)) || audio.contains(ext(url)) }
    public static func looksDownloadable(_ url: URL) -> Bool { isMedia(url) || files.contains(ext(url)) }

    public enum Kind: String, Codable, Sendable { case video = "Video", audio = "Audio", file = "File" }

    public static func kind(_ url: URL, mime: String? = nil) -> Kind {
        let m = mime?.lowercased() ?? ""
        if video.contains(ext(url)) || m.hasPrefix("video/") { return .video }
        if audio.contains(ext(url)) || m.hasPrefix("audio/") { return .audio }
        return .file
    }

    /// "lecture-1080p.mp4" → "1080p"
    public static func qualityLabel(_ url: URL) -> String? {
        let name = url.lastPathComponent.lowercased()
        guard let r = name.range(of: #"(2160|1440|1080|720|576|480|360|240)p"#, options: .regularExpression) else { return nil }
        return String(name[r])
    }
}

public enum Format {
    public static func size(_ bytes: Int64?) -> String {
        guard let bytes, bytes >= 0 else { return "Unknown size" }
        return ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }

    public static func speed(_ bytesPerSecond: Int64) -> String {
        bytesPerSecond > 0 ? size(bytesPerSecond) + "/s" : ""
    }

    public static func eta(_ seconds: Int64) -> String {
        if seconds < 0 { return "" }
        if seconds > 99 * 86_400 { return "more than 99 days" }
        let f = DateComponentsFormatter()
        f.allowedUnits = seconds >= 3600 ? [.hour, .minute] : [.minute, .second]
        f.unitsStyle = .abbreviated
        return f.string(from: TimeInterval(seconds)) ?? ""
    }
}

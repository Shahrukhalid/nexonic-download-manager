import Foundation

/// Settings → Extras (added in 0.32.0): what happens around a finished download.
public struct FinishOptions: Codable, Equatable, Sendable {
    /// Rename finished files by this pattern ("" = keep the name). See [RenameMask].
    public var renameMask = ""
    /// A web address told when a download finishes or fails (for a phone notification), "" = none.
    public var notifyURL = ""
    /// The small always-on-top window that takes dropped links.
    public var dropTarget = false
    /// Video downloads: also save the video's picture next to it.
    public var saveThumbnail = false
    /// Video downloads: cut sponsor segments, from the community list of marked segments.
    public var skipSponsors = false

    public init() {}

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        renameMask = try c.decodeIfPresent(String.self, forKey: .renameMask) ?? ""
        notifyURL = try c.decodeIfPresent(String.self, forKey: .notifyURL) ?? ""
        dropTarget = try c.decodeIfPresent(Bool.self, forKey: .dropTarget) ?? false
        saveThumbnail = try c.decodeIfPresent(Bool.self, forKey: .saveThumbnail) ?? false
        skipSponsors = try c.decodeIfPresent(Bool.self, forKey: .skipSponsors) ?? false
    }

    /// Extra video engine options these settings ask for.
    public var videoArguments: [String] {
        (saveThumbnail ? ["--write-thumbnail"] : []) + (skipSponsors ? ["--sponsorblock-remove", "sponsor"] : [])
    }
}

/// File names from a pattern: {name} {ext} {site} {date} {time} {category}. The extension is kept even when the pattern
/// leaves it out; characters a file name can't hold are replaced.
public enum RenameMask {
    public static let tokens = ["{name}", "{ext}", "{site}", "{date}", "{time}", "{category}"]

    public static func apply(_ mask: String, fileName: String, url: URL, date: Date, category: String) -> String? {
        let mask = mask.trimmingCharacters(in: .whitespaces)
        guard !mask.isEmpty, mask.contains("{") else { return nil }
        let ext = (fileName as NSString).pathExtension
        let stem = (fileName as NSString).deletingPathExtension
        let day = DateFormatter(), time = DateFormatter()
        day.locale = Locale(identifier: "en_US_POSIX"); time.locale = day.locale
        day.dateFormat = "yyyy-MM-dd"; time.dateFormat = "HH-mm"
        var site = url.host?.lowercased() ?? ""
        if site.hasPrefix("www.") { site.removeFirst(4) }
        var out = mask
        for (k, v) in [("{name}", stem), ("{ext}", ext), ("{site}", site), ("{date}", day.string(from: date)),
                       ("{time}", time.string(from: date)), ("{category}", category)] {
            out = out.replacingOccurrences(of: k, with: v, options: .caseInsensitive)
        }
        out = clean(out)
        if out.isEmpty { return nil }
        if !ext.isEmpty, !mask.lowercased().contains("{ext}") { out += "." + ext }
        return out == fileName ? nil : out
    }

    static func clean(_ s: String) -> String {
        let bad = CharacterSet(charactersIn: "/\\:*?\"<>|").union(.controlCharacters)
        let parts = s.unicodeScalars.map { bad.contains($0) ? "-" : String($0) }.joined()
        return parts.trimmingCharacters(in: CharacterSet(charactersIn: " .")).prefix(200).description
    }
}

/// The message sent to [FinishOptions.notifyURL]: plain text, so a push service shows it as the notification itself.
public enum FinishNotice {
    public static func request(_ address: String, fileName: String, ok: Bool) -> URLRequest? {
        guard let url = URL(string: address.trimmingCharacters(in: .whitespaces)),
              let scheme = url.scheme?.lowercased(), scheme == "https" || (scheme == "http" && isLocal(url.host ?? "")) else { return nil }
        var r = URLRequest(url: url, timeoutInterval: 15)
        r.httpMethod = "POST"
        r.setValue("text/plain; charset=utf-8", forHTTPHeaderField: "Content-Type")
        r.setValue(ok ? "Download finished" : "Download failed", forHTTPHeaderField: "Title")
        r.httpBody = Data(((ok ? "Finished: " : "Failed: ") + fileName).utf8)
        return r
    }

    /// Plain http only inside the home network.
    static func isLocal(_ host: String) -> Bool {
        let h = host.lowercased()
        return h == "localhost" || h.hasSuffix(".local") || h.hasPrefix("192.168.") || h.hasPrefix("10.") || h.hasPrefix("127.")
            || (h.hasPrefix("172.") && (16...31).contains(Int(h.split(separator: ".").dropFirst().first ?? "") ?? 0))
    }
}

/// Play while downloading: how much of the file is there from its start, without gaps.
public enum PartialPlay {
    public static let minimumBytes: Int64 = 4 << 20
    static let media: Set<String> = ["mp4", "m4v", "mkv", "webm", "mov", "avi", "mp3", "m4a", "aac", "flac", "ogg", "opus", "wav", "ts"]

    public static func isMedia(_ fileName: String) -> Bool { media.contains((fileName as NSString).pathExtension.lowercased()) }

    public static func prefix(_ segments: [Segment]) -> Int64 {
        var end: Int64 = 0
        for s in segments.sorted(by: { $0.start < $1.start }) {
            guard s.start <= end else { break }
            end = max(end, s.start + s.written)
            if s.written < s.end - s.start + 1 { break }
        }
        return end
    }

    /// Ready to start playing: a single file being written, a media type, and its first few MB there.
    public static func ready(_ r: DownloadRecord) -> Bool {
        guard isMedia(r.fileName), r.workFile != nil, r.status != .finished else { return false }
        return prefix(r.segments) >= min(minimumBytes, max(1, (r.size ?? minimumBytes) / 50))
    }
}

/// Video tools: a finished video as .mp4, or its sound alone, without re-encoding (no quality is lost).
public enum MediaTools {
    public static func remuxArguments(input: String, output: String) -> [String] {
        ["-hide_banner", "-loglevel", "error", "-y", "-i", input, "-map", "0:v:0?", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", output]
    }

    public static func audioArguments(input: String, output: String) -> [String] {
        ["-hide_banner", "-loglevel", "error", "-y", "-i", input, "-vn", "-map", "0:a:0", "-c", "copy", output]
    }

    /// The sound's own container, so it can be copied as it is: .m4a for AAC, .opus, .mp3, .ogg for Vorbis.
    public static func audioExtension(codec: String) -> String {
        switch codec.lowercased() {
        case "aac", "alac": return "m4a"
        case "opus": return "opus"
        case "mp3": return "mp3"
        case "vorbis": return "ogg"
        case "flac": return "flac"
        default: return "mka"
        }
    }

    /// A free name beside the file: "Clip.mp4", then "Clip (2).mp4"…
    public static func output(for input: String, ext: String) -> String {
        let base = (input as NSString).deletingPathExtension
        var path = base + "." + ext
        var n = 2
        while FileManager.default.fileExists(atPath: path) || path == input { path = "\(base) (\(n)).\(ext)"; n += 1 }
        return path
    }
}

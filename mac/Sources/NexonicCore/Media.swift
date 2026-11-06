import Foundation

/// Playlists and channels: one link that stands for many videos (YouTube, Vimeo, SoundCloud, Bandcamp…).
public enum Playlists {
    public struct Entry: Equatable, Sendable {
        public var url: String
        public var title: String
        public var duration: Double?
    }

    public struct Result: Equatable, Sendable {
        public var title: String
        public var entries: [Entry]
    }

    /// The most videos read from one playlist or channel.
    public static let maxEntries = 500

    /// True for links that are a list of videos rather than one video.
    public static func isPlaylist(_ link: String) -> Bool {
        guard let u = URL(string: link.trimmingCharacters(in: .whitespacesAndNewlines)), let host = u.host?.lowercased() else { return false }
        let path = u.path.hasSuffix("/") ? String(u.path.dropLast()) : u.path
        let query = u.query ?? ""
        func on(_ h: String) -> Bool { host == h || host.hasSuffix("." + h) }
        if on("youtube.com") {
            if path == "/playlist" { return query.contains("list=") }
            if path == "/watch" { return false }  // one video, even when it is part of a list
            return path.hasPrefix("/@") || path.hasPrefix("/channel/") || path.hasPrefix("/c/") || path.hasPrefix("/user/")
        }
        if on("vimeo.com") { return path.hasPrefix("/showcase/") || path.hasPrefix("/channels/") || path.contains("/albums/") }
        if on("soundcloud.com") { return path.contains("/sets/") }
        if on("bandcamp.com") { return path.hasPrefix("/album/") }
        if on("dailymotion.com") { return path.hasPrefix("/playlist/") }
        return false
    }

    /// A channel's home page lists its tabs; its Videos tab lists the videos.
    public static func normalize(_ link: String) -> String {
        let t = link.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let u = URL(string: t), let host = u.host?.lowercased(), host == "youtube.com" || host.hasSuffix(".youtube.com") else { return t }
        let path = u.path.hasSuffix("/") ? String(u.path.dropLast()) : u.path
        let parts = path.split(separator: "/").map(String.init)
        let home = (parts.count == 1 && parts[0].hasPrefix("@")) || (parts.count == 2 && ["channel", "c", "user"].contains(parts[0]))
        return home ? "\(u.scheme ?? "https")://\(u.host ?? host)\(path)/videos" : t
    }

    /// The command that lists a playlist's videos without downloading them.
    public static func arguments(_ link: String, quickJS: String?, max: Int = maxEntries) -> [String] {
        var a = ["--ignore-config", "--flat-playlist", "-J", "--playlist-end", String(max), "--no-colors", "--js-runtimes", "node"]
        if let quickJS { a += ["--js-runtimes", "quickjs:\(quickJS)"] }
        return a + ["--", normalize(link)]
    }

    /// Reads the videos from the JSON printed by `arguments`.
    public static func parse(_ data: Data) -> Result? {
        guard let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return nil }
        var list: [Entry] = []
        var seen = Set<String>()
        for e in (o["entries"] as? [[String: Any]]) ?? [] {
            var url = (e["url"] as? String) ?? (e["webpage_url"] as? String)
            if !(url?.hasPrefix("http") ?? false), let id = e["id"] as? String, ((e["ie_key"] as? String) ?? "").hasPrefix("Youtube") {
                url = "https://www.youtube.com/watch?v=\(id)"
            }
            guard let u = url, u.hasPrefix("http"), seen.insert(u).inserted else { continue }
            list.append(Entry(url: u, title: (e["title"] as? String) ?? u, duration: (e["duration"] as? NSNumber)?.doubleValue))
        }
        return Result(title: (o["title"] as? String) ?? "", entries: list)
    }
}

/// Subtitles saved inside downloaded videos (Downloads menu; off by default).
public enum VideoSubtitles {
    /// The setting's key in the app's preferences.
    public static let key = "videoSubtitles"

    /// The computer's language and English (subtitles written by people, not machine captions).
    public static func languages(_ locale: Locale = .current) -> String {
        let l = (locale.language.languageCode?.identifier ?? "en").lowercased()
        return l == "en" ? "en.*" : "\(l).*,en.*"
    }

    /// A failed subtitle download must never fail the video, so errors become warnings.
    public static func arguments(_ languages: String? = nil) -> [String] {
        ["--write-subs", "--sub-langs", languages ?? Self.languages(), "--embed-subs", "--ignore-errors"]
    }
}

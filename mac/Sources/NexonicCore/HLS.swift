import Foundation

/// Streamed video sent as a playlist of small pieces (HLS, `.m3u8`).
public enum HLS {
    public struct Variant: Equatable, Sendable {
        public var url: URL
        public var bandwidth: Int
        public var resolution: String?
        public var audioGroup: String?

        /// Pixels in the picture (0 when unknown), used to pick the best quality.
        public var pixels: Int {
            let p = (resolution ?? "").lowercased().split(separator: "x").compactMap { Int($0) }
            return p.count == 2 ? p[0] * p[1] : 0
        }
    }

    public struct Media: Equatable, Sendable {
        public var segments: [URL]
        public var initSegment: URL?
        /// "NONE" or the encryption method the stream uses (protected streams are not saved).
        public var encryption: String
        public var isFragmentedMP4: Bool { initSegment != nil }
    }

    public enum Playlist: Equatable, Sendable {
        case master(variants: [Variant], audio: [String: URL])
        case media(Media)
    }

    public enum Failure: Error, LocalizedError, Equatable {
        case notAPlaylist, protected(String), empty

        public var errorDescription: String? {
            switch self {
            case .notAPlaylist: return "This link is not a video playlist"
            case .protected: return "This video is copy-protected and can't be saved"
            case .empty: return "The video playlist is empty"
            }
        }
    }

    /// Reads the attributes of a tag line such as `BANDWIDTH=1280000,RESOLUTION=1280x720,CODECS="avc1,mp4a"`.
    static func attributes(_ line: String) -> [String: String] {
        guard let colon = line.firstIndex(of: ":") else { return [:] }
        var out: [String: String] = [:]
        var key = "", value = "", inKey = true, quoted = false
        for ch in line[line.index(after: colon)...] {
            if inKey {
                if ch == "=" { inKey = false } else { key.append(ch) }
            } else if ch == "\"" {
                quoted.toggle()
            } else if ch == ",", !quoted {
                out[key.trimmingCharacters(in: .whitespaces).uppercased()] = value
                key = ""; value = ""; inKey = true
            } else {
                value.append(ch)
            }
        }
        if !key.isEmpty { out[key.trimmingCharacters(in: .whitespaces).uppercased()] = value }
        return out
    }

    public static func parse(_ text: String, base: URL) throws -> Playlist {
        let lines = text.components(separatedBy: .newlines).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        guard lines.first?.hasPrefix("#EXTM3U") == true else { throw Failure.notAPlaylist }
        func resolve(_ s: String) -> URL? { URL(string: s, relativeTo: base)?.absoluteURL }
        if lines.contains(where: { $0.hasPrefix("#EXT-X-STREAM-INF") }) {
            var variants: [Variant] = []
            var audio: [String: URL] = [:]
            var pending: [String: String]?
            for line in lines {
                if line.hasPrefix("#EXT-X-STREAM-INF") {
                    pending = attributes(line)
                } else if line.hasPrefix("#EXT-X-MEDIA") {
                    let a = attributes(line)
                    if a["TYPE"] == "AUDIO", let g = a["GROUP-ID"], let u = a["URI"].flatMap(resolve),
                       audio[g] == nil || a["DEFAULT"] == "YES" {
                        audio[g] = u
                    }
                } else if !line.hasPrefix("#"), let a = pending, let u = resolve(line) {
                    variants.append(Variant(url: u, bandwidth: Int(a["BANDWIDTH"] ?? "") ?? 0, resolution: a["RESOLUTION"],
                                            audioGroup: a["AUDIO"]))
                    pending = nil
                }
            }
            guard !variants.isEmpty else { throw Failure.empty }
            return .master(variants: variants, audio: audio)
        }
        var segments: [URL] = []
        var initSegment: URL?
        var encryption = "NONE"
        for line in lines {
            if line.hasPrefix("#EXT-X-KEY") || line.hasPrefix("#EXT-X-SESSION-KEY") {
                let m = attributes(line)["METHOD"] ?? "NONE"
                if m != "NONE" { encryption = m }
            } else if line.hasPrefix("#EXT-X-MAP") {
                initSegment = attributes(line)["URI"].flatMap(resolve)
            } else if !line.hasPrefix("#"), let u = resolve(line) {
                segments.append(u)
            }
        }
        guard !segments.isEmpty else { throw Failure.empty }
        return .media(Media(segments: segments, initSegment: initSegment, encryption: encryption))
    }

    /// The best picture: most pixels, then the highest bit rate.
    public static func best(_ variants: [Variant]) -> Variant? {
        variants.max { ($0.pixels, $0.bandwidth) < ($1.pixels, $1.bandwidth) }
    }

    public static func isPlaylistURL(_ url: URL) -> Bool { url.pathExtension.lowercased() == "m3u8" }
}

/// The ffmpeg that ships inside the app (or one the user installed), used to turn saved streams into .mp4 files.
public enum FFmpeg {
    public static var path: String? {
        let env = ProcessInfo.processInfo.environment["PATH"] ?? ""
        let bundled = Bundle.main.executableURL?.deletingLastPathComponent().path
        let dirs = [bundled].compactMap { $0 } + ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"] + env.split(separator: ":").map(String.init)
        return dirs.map { "\($0)/ffmpeg" }.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    /// Copies the streams into an .mp4 without re-encoding. Returns false when ffmpeg is missing or fails.
    public static func mux(_ inputs: [URL], to output: URL) -> Bool {
        guard let exe = path else { return false }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: exe)
        var args = ["-y", "-loglevel", "error"]
        for i in inputs { args += ["-i", i.path] }
        for n in inputs.indices { args += ["-map", "\(n)"] }
        args += ["-c", "copy", "-movflags", "+faststart", output.path]
        p.arguments = args
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        do {
            try p.run()
            p.waitUntilExit()
            return p.terminationStatus == 0
        } catch {
            return false
        }
    }
}

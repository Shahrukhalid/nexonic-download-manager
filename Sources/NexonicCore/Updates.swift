import Foundation

/// Finds out whether a newer version is published on GitHub Releases.
public enum Updates {
    public static let latestURL = URL(string: "https://api.github.com/repos/Shahrukhalid/nexonic-download-manager/releases/latest")!
    /// The same details as a file attached to every release. Used when GitHub's API refuses (it allows 60 checks an
    /// hour per network, which shared office and mobile networks use up).
    public static let mirrorURL = URL(string: "https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest/download/latest.json")!

    public struct Release: Equatable, Sendable {
        public var version: String
        public var notes: String
        public var page: URL
        public var assets: [String: URL]
        public var digests: [String: String] = [:]  // GitHub's SHA-256 of each attached file
    }

    /// Only files attached to this app's own GitHub releases are ever downloaded as updates.
    public static func trustedAsset(_ url: URL) -> Bool {
        url.scheme == "https" && url.host == "github.com"
            && url.path.hasPrefix("/Shahrukhalid/nexonic-download-manager/releases/download/")
    }

    /// True when the downloaded file is exactly the one GitHub published.
    public static func verify(_ file: URL, sha256: String) -> Bool {
        guard let actual = try? Checksum.compute(.sha256, file: file) else { return false }
        return actual.lowercased() == sha256.lowercased()
    }

    /// Reads the GitHub "latest release" answer.
    public static func parse(_ data: Data) -> Release? {
        guard let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = o["tag_name"] as? String,
              let page = (o["html_url"] as? String).flatMap(URL.init(string:)) else { return nil }
        if o["draft"] as? Bool == true || o["prerelease"] as? Bool == true { return nil }
        var assets: [String: URL] = [:]
        var digests: [String: String] = [:]
        for a in o["assets"] as? [[String: Any]] ?? [] {
            if let name = a["name"] as? String, let url = (a["browser_download_url"] as? String).flatMap(URL.init(string:)) {
                assets[name] = url
                if let d = a["digest"] as? String, d.hasPrefix("sha256:") { digests[name] = String(d.dropFirst(7)).lowercased() }
            }
        }
        let version = tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
        return Release(version: version, notes: o["body"] as? String ?? "", page: page, assets: assets, digests: digests)
    }

    /// True when `a` is a higher version than `b` ("0.6.0" > "0.5.9", "0.10.0" > "0.9.1").
    public static func isNewer(_ a: String, than b: String) -> Bool {
        func parts(_ s: String) -> [Int] { s.split(separator: ".").map { Int($0.prefix { $0.isNumber }) ?? 0 } }
        let x = parts(a), y = parts(b)
        for i in 0..<max(x.count, y.count) {
            let l = i < x.count ? x[i] : 0, r = i < y.count ? y[i] : 0
            if l != r { return l > r }
        }
        return false
    }

    /// The release notes of every version newer than the one installed ("## 0.6.0" sections), without Markdown marks.
    public static func notes(_ body: String, after current: String) -> String {
        var out: [String] = []
        var keep = false
        for line in body.components(separatedBy: "\n") {
            if line.hasPrefix("## ") {
                let v = line.dropFirst(3).trimmingCharacters(in: .whitespaces)
                keep = isNewer(v, than: current)
                if keep { out.append(out.isEmpty ? "Version \(v)" : "\nVersion \(v)") }
                continue
            }
            if keep { out.append(line.replacingOccurrences(of: "**", with: "").replacingOccurrences(of: "`", with: "")) }
        }
        let text = out.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
        return text.count > 2500 ? String(text.prefix(2500)) + "…" : text
    }
}

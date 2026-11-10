import Foundation

/// File → Link Grabber: finds the links in pasted text or a web page and groups them into packages. The parts of a split
/// archive (name.part1.rar, name.7z.001, name.r00…) form one package, saved together and unpacked once every part is done.
public enum LinkGrabber {
    public struct Package: Equatable, Sendable {
        public var name: String
        public var links: [String]
    }

    public static let otherFiles = "Other files"
    public static let maxLinks = 5000
    static let webPages: Set<String> = ["", "html", "htm", "php", "asp", "aspx", "jsp", "cgi", "shtml", "xhtml"]

    private static func regex(_ p: String) -> NSRegularExpression { try! NSRegularExpression(pattern: p, options: [.caseInsensitive]) }
    private static let href = regex(#"\b(?:href|src|data-url|data-href)\s*=\s*(?:"([^"]+)"|'([^']+)')"#)
    private static let plain = regex(#"(?:https?://|magnet:\?)[^\s"'<>\]\[)(]+"#)
    private static let setPart = regex(#"^(.+?)(?:\.part\d+\.rar|\.r\d{2,3}|\.z\d{2}|\.(?:7z|zip|rar|tar|gz)\.\d{3}|\.\d{3})$"#)
    private static let archive = regex(#"^(.+?)\.(?:rar|zip|7z|tar|tar\.gz|tgz|tar\.xz|tar\.bz2)$"#)

    private static func first(_ re: NSRegularExpression, _ s: String) -> String? {
        let ns = s as NSString
        guard let m = re.firstMatch(in: s, range: NSRange(location: 0, length: ns.length)), m.range(at: 1).location != NSNotFound else { return nil }
        return ns.substring(with: m.range(at: 1))
    }

    private static func decode(_ s: String) -> String {
        s.replacingOccurrences(of: "&amp;", with: "&").replacingOccurrences(of: "&quot;", with: "\"").replacingOccurrences(of: "&#39;", with: "'")
    }

    /// The links in `text` (HTML or plain text). From a web page (`page` set), only links to files are kept.
    public static func find(_ text: String, page: URL? = nil) -> [String] {
        var seen = Set<String>()
        var result: [String] = []
        func add(_ raw: String) {
            guard result.count < maxLinks else { return }
            var r = decode(raw.trimmingCharacters(in: .whitespacesAndNewlines))
            while let last = r.last, ".,;".contains(last) { r.removeLast() }
            if r.lowercased().hasPrefix("magnet:?") { if seen.insert(r).inserted { result.append(r) }; return }
            guard var u = URL(string: r, relativeTo: page)?.absoluteURL, let scheme = u.scheme?.lowercased(), scheme == "http" || scheme == "https" else { return }
            if var c = URLComponents(url: u, resolvingAgainstBaseURL: true) { c.fragment = nil; u = c.url ?? u }
            u = Links.unwrap(u)
            if page != nil && webPages.contains(u.pathExtension.lowercased()) { return }
            if seen.insert(u.absoluteString).inserted { result.append(u.absoluteString) }
        }
        let ns = text as NSString
        let all = NSRange(location: 0, length: ns.length)
        for m in href.matches(in: text, range: all) {
            let g = m.range(at: 1).location != NSNotFound ? m.range(at: 1) : m.range(at: 2)
            if g.location != NSNotFound { add(ns.substring(with: g)) }
        }
        for m in plain.matches(in: text, range: all) { add(ns.substring(with: m.range)) }
        return result
    }

    /// The text is one address of a web page (not of a file): the grabber reads that page.
    public static func pageAddress(_ text: String) -> URL? {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty, !t.contains(where: \.isWhitespace), let u = URL(string: t),
              let s = u.scheme?.lowercased(), s == "http" || s == "https", webPages.contains(u.pathExtension.lowercased()) else { return nil }
        return u
    }

    /// The name shared by the parts of a split archive ("Movie" for Movie.part2.rar), or nil for other files.
    public static func setName(_ fileName: String) -> String? { first(setPart, fileName) }

    /// Packages in the order the links came: split archives and archives with the same name together, everything else in
    /// "Other files".
    public static func group(_ links: [String]) -> [Package] {
        var packages: [Package] = []
        var index: [String: Int] = [:]
        var other = Package(name: otherFiles, links: [])
        for link in links {
            let file = link.lowercased().hasPrefix("magnet:") ? "" : URL(string: link).map(FileNames.fromURL) ?? ""
            guard let base = setName(file) ?? first(archive, file) else { other.links.append(link); continue }
            let name = FileNames.sanitize(base)
            if let i = index[name.lowercased()] { packages[i].links.append(link) }
            else { index[name.lowercased()] = packages.count; packages.append(Package(name: name, links: [link])) }
        }
        if !other.links.isEmpty { packages.append(other) }
        return packages
    }

    static let unpackable = ["zip", "7z", "rar", "tar", "tgz", "gz", "bz2", "xz", "tbz2", "txz"]

    /// A finished file that can be unpacked; for split RAR sets only the first part.
    public static func isArchive(_ path: String?) -> Bool {
        guard let path, FileManager.default.fileExists(atPath: path) else { return false }
        let name = (path as NSString).lastPathComponent.lowercased()
        guard unpackable.contains((name as NSString).pathExtension) else { return false }
        if let r = name.range(of: #"\.part(\d+)\.rar$"#, options: .regularExpression) {
            let digits = name[r].filter(\.isNumber)
            return Int(digits) == 1
        }
        return true
    }

    /// Archives to unpack now that `done` finished: a package's archives once every download in it is done, the first part
    /// of a split archive once every part in the folder is done, or a single archive.
    public static func toUnpack(_ done: DownloadRecord, all: [DownloadRecord], unpackAll: Bool) -> [String] {
        guard done.status == .finished else { return [] }
        if let package = done.package, !package.isEmpty {
            guard unpackAll || done.unpackWhenDone == true else { return [] }
            let members = all.filter { $0.package == package && $0.folder == done.folder }
            return members.allSatisfy { $0.status == .finished } ? members.compactMap(\.savedPath).filter { isArchive($0) } : []
        }
        guard unpackAll else { return [] }
        if let set = setName(done.fileName) {
            let parts = all.filter { $0.folder == done.folder && setName($0.fileName)?.lowercased() == set.lowercased() }
            return parts.allSatisfy { $0.status == .finished } ? Array(parts.compactMap(\.savedPath).filter { isArchive($0) }.prefix(1)) : []
        }
        return isArchive(done.savedPath) ? [done.savedPath!] : []
    }
}

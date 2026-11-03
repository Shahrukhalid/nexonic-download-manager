import Foundation

public enum LinkExtractor {
    private static let attr = try! NSRegularExpression(
        pattern: #"(?:href|src)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>"']+))"#, options: [.caseInsensitive])

    /// Absolute web links found in href and src attributes, in page order, without duplicates or #fragments.
    public static func links(in html: String, base: URL) -> [URL] {
        let ns = html as NSString
        var seen = Set<String>()
        var out: [URL] = []
        for m in attr.matches(in: html, range: NSRange(location: 0, length: ns.length)) {
            var raw = ""
            for i in 1...3 where m.range(at: i).location != NSNotFound { raw = ns.substring(with: m.range(at: i)) }
            raw = raw.replacingOccurrences(of: "&amp;", with: "&").trimmingCharacters(in: .whitespaces)
            guard !raw.isEmpty, !raw.hasPrefix("#"), let u = URL(string: raw, relativeTo: base)?.absoluteURL,
                  let scheme = u.scheme?.lowercased(), scheme == "http" || scheme == "https" else { continue }
            var c = URLComponents(url: u, resolvingAgainstBaseURL: false)
            c?.fragment = nil
            guard let clean = c?.url, seen.insert(clean.absoluteString).inserted else { continue }
            out.append(clean)
        }
        return out
    }
}

public struct GrabOptions: Sendable {
    public var start: URL
    public var depth: Int
    public var types: Set<String>  // empty = every file that is not a web page
    public var stayInFolder: Bool
    public var maxPages: Int

    public init(start: URL, depth: Int = 1, types: Set<String> = [], stayInFolder: Bool = true, maxPages: Int = 300) {
        self.start = start
        self.depth = max(0, min(depth, 5))
        self.types = types
        self.stayInFolder = stayInFolder
        self.maxPages = maxPages
    }
}

/// Explores a section of a website and lists the files it links to.
public enum SiteGrabber {
    static let pageTypes: Set<String> = ["", "html", "htm", "php", "asp", "aspx", "jsp", "cgi", "shtml"]

    public static func isPage(_ url: URL) -> Bool { pageTypes.contains(url.pathExtension.lowercased()) }

    public static func inScope(_ url: URL, of o: GrabOptions) -> Bool {
        guard url.host?.lowercased() == o.start.host?.lowercased() else { return false }
        guard o.stayInFolder else { return true }
        var folder = o.start.path
        if !folder.hasSuffix("/") { folder = (folder as NSString).deletingLastPathComponent }
        if !folder.hasSuffix("/") { folder += "/" }
        return url.path.hasPrefix(folder) || url.path + "/" == folder
    }

    public static func wanted(_ url: URL, _ o: GrabOptions) -> Bool {
        let ext = url.pathExtension.lowercased()
        return o.types.isEmpty ? !pageTypes.contains(ext) : o.types.contains(ext)
    }

    public static func explore(_ o: GrabOptions, session: URLSession = .shared,
                               progress: @Sendable (Int, Int) -> Void = { _, _ in }) async -> [URL] {
        var queue: [(URL, Int)] = [(o.start, 0)]
        var visited = Set<String>()
        var files: [URL] = []
        var found = Set<String>()
        while !queue.isEmpty, visited.count < o.maxPages, !Task.isCancelled {
            let (page, level) = queue.removeFirst()
            guard visited.insert(page.absoluteString).inserted else { continue }
            progress(visited.count, files.count)
            guard let (data, response) = try? await session.data(from: page),
                  let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode),
                  (http.value(forHTTPHeaderField: "Content-Type") ?? "text/html").lowercased().contains("html"),
                  data.count < 5 << 20 else { continue }
            let html = String(decoding: data, as: UTF8.self)
            for link in LinkExtractor.links(in: html, base: http.url ?? page) {
                if wanted(link, o) {
                    if found.insert(link.absoluteString).inserted { files.append(link) }
                } else if isPage(link), level < o.depth, inScope(link, of: o), !visited.contains(link.absoluteString) {
                    queue.append((link, level + 1))
                }
            }
        }
        return files
    }
}

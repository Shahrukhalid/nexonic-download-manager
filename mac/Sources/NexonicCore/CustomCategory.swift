import Foundation

/// A category of the user's own (Settings → General): a name, an icon, file types ("cbz cbr"), sites whose files belong
/// here, and a folder (a full path, or a folder inside the download folder; empty = the category's name).
public struct CustomCategory: Codable, Equatable, Sendable {
    public var name = ""
    public var extensions = ""
    public var folder = ""
    public var icon = ""
    public var sites = ""

    public init(name: String = "", extensions: String = "", folder: String = "", icon: String = "", sites: String = "") {
        self.name = name; self.extensions = extensions; self.folder = folder; self.icon = icon; self.sites = sites
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? ""
        extensions = try c.decodeIfPresent(String.self, forKey: .extensions) ?? ""
        folder = try c.decodeIfPresent(String.self, forKey: .folder) ?? ""
        icon = try c.decodeIfPresent(String.self, forKey: .icon) ?? ""
        sites = try c.decodeIfPresent(String.self, forKey: .sites) ?? ""
    }

    /// The file types in the order written, lowercase, without dots.
    public var typeList: [String] {
        var seen = Set<String>()
        return extensions.lowercased().split(whereSeparator: { $0 == " " || $0 == "," || $0 == ";" })
            .map { String($0.drop(while: { $0 == "." || $0 == "*" })) }.filter { !$0.isEmpty && seen.insert($0).inserted }
    }

    public var types: Set<String> { Set(typeList) }

    private var siteList: [String] { sites.split(whereSeparator: \.isWhitespace).map { Self.sitePattern(String($0)) }.filter { !$0.isEmpty } }

    /// True when a file of this type (lowercase, no dot) from this address belongs here. A category with sites and no
    /// types takes every file from those sites.
    public func takes(ext: String, url: String?) -> Bool {
        let t = types
        if !t.isEmpty && (ext.isEmpty || !t.contains(ext)) { return false }
        let list = siteList
        if list.isEmpty { return !t.isEmpty }
        guard let url else { return false }
        return Self.siteMatches(list, url: url)
    }

    /// True when the address matches one of the site patterns ("example.com" also covers its subdomains, * matches anything).
    public static func siteMatches(_ patterns: [String], url: String) -> Bool {
        guard let u = URL(string: url), let scheme = u.scheme?.lowercased(), ["http", "https", "ftp"].contains(scheme),
              let rawHost = u.host?.lowercased() else { return false }
        let host = rawHost.hasSuffix(".") ? String(rawHost.dropLast()) : rawHost
        let path = (u.path.removingPercentEncoding ?? u.path).lowercased()
        for p in patterns where !p.isEmpty {
            let parts = p.split(separator: "/", maxSplits: 1, omittingEmptySubsequences: false)
            let hp = String(parts[0])
            let pp = parts.count > 1 ? "/" + parts[1] : nil
            let hostOK = hp.contains("*") ? glob(hp, host) : host == hp || host.hasSuffix("." + hp)
            if hostOK, pp == nil || glob(pp!, path) || glob((pp!.hasSuffix("/") ? String(pp!.dropLast()) : pp!) + "/*", path) { return true }
        }
        return false
    }

    /// A pattern as kept: lowercase, without "https://" and without a query; "" when it cannot be used.
    public static func sitePattern(_ raw: String) -> String {
        var p = raw.trimmingCharacters(in: .whitespaces).lowercased()
        if let r = p.range(of: "://") { p = String(p[r.upperBound...]) }
        if let q = p.firstIndex(where: { $0 == "?" || $0 == "#" }) { p = String(p[..<q]) }
        return p.count <= 200 && p.unicodeScalars.allSatisfy({ $0.value > 32 && $0 != "\"" && $0 != "\\" }) ? p : ""
    }

    private static func glob(_ pattern: String, _ text: String) -> Bool {
        let rx = "^" + NSRegularExpression.escapedPattern(for: pattern).replacingOccurrences(of: "\\*", with: ".*") + "$"
        return text.range(of: rx, options: .regularExpression) != nil
    }

    /// The folder for this category, or nil when the name and folder are both unusable.
    public func folderURL(in downloadFolder: URL) -> URL? {
        let f = folder.trimmingCharacters(in: .whitespaces)
        if f.hasPrefix("/") { return URL(fileURLWithPath: f, isDirectory: true) }
        if f.split(separator: "/").contains("..") { return nil }
        let sub = FileNames.sanitize(f.isEmpty ? name.trimmingCharacters(in: .whitespaces) : f)
        if sub.isEmpty || sub == "." || sub == ".." { return nil }
        return downloadFolder.appendingPathComponent(sub, isDirectory: true)
    }

    // MARK: - The Settings text: one category per line, "[icon] Name: types @ sites > folder"

    public static func toLines(_ list: [CustomCategory]) -> String {
        list.map { c in
            (c.icon.isEmpty ? "" : c.icon + " ") + "\(c.name): \(c.extensions)"
                + (c.sites.isEmpty ? "" : (c.extensions.isEmpty ? "@ " : " @ ") + c.sites) + (c.folder.isEmpty ? "" : " > " + c.folder)
        }.joined(separator: "\n")
    }

    /// A leading symbol of a name ("📚 Books" → "📚", "Books"): one character that is not a letter or digit.
    public static func splitIcon(_ name: String) -> (icon: String, name: String) {
        let n = name.trimmingCharacters(in: .whitespaces)
        guard let first = n.first, n.count > 2, n.dropFirst().first?.isWhitespace == true,
              !first.isLetter, !first.isNumber else { return ("", n) }
        return (String(first), n.dropFirst().trimmingCharacters(in: .whitespaces))
    }

    /// Reads the Settings text: the categories, or a message naming the first line that cannot be read.
    public static func parse(_ text: String) -> (list: [CustomCategory], problem: String?) {
        var list: [CustomCategory] = []
        for (i, raw) in text.components(separatedBy: "\n").enumerated() {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty || line.hasPrefix("#") { continue }
            // "Name: …"; a colon inside a path (none on macOS) is not expected, so the first ": " or final ":" ends the name.
            guard let colon = line.range(of: ": ")?.lowerBound ?? (line.hasSuffix(":") ? line.index(before: line.endIndex) : nil),
                  colon > line.startIndex else {
                return (list, "Line \(i + 1): write it as Name: types > folder (for example Comics: cbz cbr > Comics).")
            }
            let rest = String(line[line.index(after: colon)...])
            let parts = rest.split(separator: ">", maxSplits: 1, omittingEmptySubsequences: false)
            let match = String(parts[0])
            let at = match.split(separator: "@", maxSplits: 1, omittingEmptySubsequences: false)
            let (icon, name) = splitIcon(String(line[..<colon]))
            let c = CustomCategory(name: name,
                                   extensions: String(at[0]).trimmingCharacters(in: .whitespaces),
                                   folder: parts.count > 1 ? String(parts[1]).trimmingCharacters(in: .whitespaces) : "",
                                   icon: icon,
                                   sites: at.count > 1 ? String(at[1]).trimmingCharacters(in: .whitespaces) : "")
            if c.types.isEmpty && c.sites.isEmpty { return (list, "Line \(i + 1): list at least one file type or site for \(c.name).") }
            if c.sites.split(whereSeparator: \.isWhitespace).contains(where: { sitePattern(String($0)).isEmpty }) {
                return (list, "Line \(i + 1): a site of \(c.name) cannot be read (write it like example.com or github.com/*/releases).")
            }
            list.append(c)
        }
        return (clean(list), nil)
    }

    /// Categories with a name and a type or site, at most 50; each type goes to the first category that lists it, unless
    /// the category is limited to some sites.
    public static func clean(_ list: [CustomCategory]) -> [CustomCategory] {
        var seen = Set<String>(), result: [CustomCategory] = []
        for c in list {
            var seenSites = Set<String>()
            let sites = Array(c.siteList.filter { seenSites.insert($0).inserted }.prefix(20))
            let types = c.typeList.filter { $0.count <= 16 && $0.allSatisfy { $0.isLetter || $0.isNumber } && (!sites.isEmpty || seen.insert($0).inserted) }
            let name = c.name.trimmingCharacters(in: .whitespaces)
            if name.isEmpty || (types.isEmpty && sites.isEmpty) { continue }
            let icon = splitIcon(c.icon + " x").icon
            result.append(CustomCategory(name: name, extensions: types.joined(separator: " "),
                                         folder: c.folder.trimmingCharacters(in: .whitespaces), icon: icon, sites: sites.joined(separator: " ")))
            if result.count == 50 { break }
        }
        return result
    }
}

/// The user's categories in use (set by the app from its settings), checked before the built-in types.
public enum CategoryRules {
    private final class Box: @unchecked Sendable {
        let lock = NSLock()
        var list: [CustomCategory] = []
    }
    private static let box = Box()

    public static var list: [CustomCategory] {
        get { box.lock.lock(); defer { box.lock.unlock() }; return box.list }
        set { let clean = CustomCategory.clean(newValue); box.lock.lock(); box.list = clean; box.lock.unlock() }
    }

    /// The user's first category (in their order) that takes this file.
    public static func category(for fileName: String, url: String?) -> CustomCategory? {
        let ext = (fileName as NSString).pathExtension.lowercased()
        return list.first { $0.takes(ext: ext, url: url) }
    }

    /// Where a finished file goes: a matching category of the user's, else the built-in type folder when sorting is on,
    /// else the download folder.
    public static func targetFolder(_ folder: URL, fileName: String, url: String?, sortBuiltIn: Bool) -> URL {
        if let c = category(for: fileName, url: url), let custom = c.folderURL(in: folder) { return custom }
        if sortBuiltIn, let sub = FileCategory(fileName: fileName).folder { return folder.appendingPathComponent(sub, isDirectory: true) }
        return folder
    }
}

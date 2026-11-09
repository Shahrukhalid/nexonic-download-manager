import Foundation

/// A list of address ranges torrents never connect to. Reads P2P ("name:1.2.3.0-1.2.3.255"), DAT
/// ("001.002.003.000 - 001.002.003.255 , 000 , name"), CIDR ("1.2.3.0/24") and single addresses, plain or gzip.
public enum TorrentBlocklist {
    public static let maxRanges = 2_000_000
    static let maxBytes = 256 * 1024 * 1024

    public struct Failure: LocalizedError {
        public let message: String
        public var errorDescription: String? { message }
    }

    /// The ranges in a list (gzip is unpacked); lines that aren't addresses are skipped.
    public static func parse(_ data: Data) throws -> [[String]] {
        let bytes = data.count >= 2 && data[data.startIndex] == 0x1f && data[data.startIndex + 1] == 0x8b ? try gunzip(data) : data
        guard bytes.count <= maxBytes else { throw Failure(message: "The blocklist is too large.") }
        var ranges: [[String]] = []
        for raw in String(decoding: bytes, as: UTF8.self).split(whereSeparator: \.isNewline) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty || line.hasPrefix("#") || line.hasPrefix(";") || line.hasPrefix("//") { continue }
            if let r = range(line) { ranges.append(r) }
            if ranges.count > maxRanges { throw Failure(message: "The blocklist has more than \(maxRanges) ranges.") }
        }
        return ranges
    }

    static func range(_ line: String) -> [String]? {
        var body = Substring(line)
        if let comma = body.firstIndex(of: ","), body[..<comma].contains("-") { body = body[..<comma] }
        if let dash = body.lastIndex(of: "-") {
            var left = body[..<dash].trimmingCharacters(in: .whitespaces)
            if address(left) == nil, let colon = left.lastIndex(of: ":") { left = left[left.index(after: colon)...].trimmingCharacters(in: .whitespaces) }
            guard let a = address(left), let b = address(body[body.index(after: dash)...].trimmingCharacters(in: .whitespaces)),
                  a.count == b.count, a.lexicographicallyPrecedes(b) || a == b else { return nil }
            return [text(a), text(b)]
        }
        if let slash = body.firstIndex(of: "/"), let net = address(body[..<slash].trimmingCharacters(in: .whitespaces)),
           let bits = Int(body[body.index(after: slash)...].trimmingCharacters(in: .whitespaces)), (0...net.count * 8).contains(bits) {
            var first = net, last = net
            for i in bits..<(net.count * 8) { first[i / 8] &= ~UInt8(0x80 >> (i % 8)); last[i / 8] |= UInt8(0x80 >> (i % 8)) }
            return [text(first), text(last)]
        }
        guard let one = address(String(body)) else { return nil }
        return [text(one), text(one)]
    }

    /// IPv4 (also with leading zeros, as DAT lists write them) or IPv6, as bytes.
    static func address(_ s: String) -> [UInt8]? {
        let parts = s.split(separator: ".", omittingEmptySubsequences: false)
        if parts.count == 4 {
            let n = parts.compactMap { p -> UInt8? in p.count <= 3 && p.allSatisfy(\.isASCII) ? UInt8(p) : nil }
            return n.count == 4 ? n : nil
        }
        var v6 = in6_addr()
        guard s.contains(":"), inet_pton(AF_INET6, s, &v6) == 1 else { return nil }
        return withUnsafeBytes(of: &v6) { Array($0) }
    }

    static func text(_ b: [UInt8]) -> String {
        if b.count == 4 { return b.map(String.init).joined(separator: ".") }
        var v6 = in6_addr(); withUnsafeMutableBytes(of: &v6) { $0.copyBytes(from: b) }
        var out = [CChar](repeating: 0, count: Int(INET6_ADDRSTRLEN))
        return inet_ntop(AF_INET6, &v6, &out, socklen_t(out.count)) != nil ? String(cString: out) : ""
    }

    /// Unpacks a gzip file (RFC 1952): skips the header, then inflates the DEFLATE data.
    static func gunzip(_ data: Data) throws -> Data {
        let d = [UInt8](data)
        guard d.count > 18, d[2] == 8 else { throw Failure(message: "The blocklist is not a valid .gz file.") }
        let flags = d[3]; var i = 10
        if flags & 4 != 0 { guard i + 2 <= d.count else { throw Failure(message: "The blocklist is not a valid .gz file.") }; i += 2 + Int(d[i]) + Int(d[i + 1]) << 8 }
        if flags & 8 != 0 { while i < d.count, d[i] != 0 { i += 1 }; i += 1 }
        if flags & 16 != 0 { while i < d.count, d[i] != 0 { i += 1 }; i += 1 }
        if flags & 2 != 0 { i += 2 }
        guard i < d.count - 8 else { throw Failure(message: "The blocklist is not a valid .gz file.") }
        do { return try (Data(d[i..<(d.count - 8)]) as NSData).decompressed(using: .zlib) as Data }
        catch { throw Failure(message: "The blocklist is not a valid .gz file.") }
    }

    static func cacheFile(_ url: URL) -> URL {
        let name = String(url.absoluteString.utf8.reduce(UInt64(14695981039346656037)) { ($0 ^ UInt64($1)) &* 1099511628211 }, radix: 16)
        return AppPaths.support.appendingPathComponent("Torrent blocklist", isDirectory: true).appendingPathComponent(name + ".list")
    }

    /// A problem with the setting for Settings, or nil.
    public static func problem(_ setting: String) -> String? {
        let s = setting.trimmingCharacters(in: .whitespaces)
        if s.isEmpty { return nil }
        if let u = URL(string: s), let scheme = u.scheme?.lowercased(), ["http", "https"].contains(scheme) {
            return scheme == "https" ? nil : "The blocklist address must start with https://"
        }
        return s.hasPrefix("/") && FileManager.default.fileExists(atPath: s) ? nil : "The blocklist file was not found."
    }

    /// Downloads an https:// list if its copy is missing or older than a day (the old copy stays when that fails).
    public static func refresh(_ setting: String, session: URLSession = .shared) async {
        let s = setting.trimmingCharacters(in: .whitespaces)
        guard let url = URL(string: s), url.scheme?.lowercased() == "https" else { return }
        let file = cacheFile(url)
        if let date = (try? file.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate,
           Date().timeIntervalSince(date) < 86_400 { return }
        guard let (data, response) = try? await session.data(from: url), (response as? HTTPURLResponse)?.statusCode == 200,
              data.count <= maxBytes, let ranges = try? parse(data), !ranges.isEmpty else { return }
        try? FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: file, options: .atomic)
    }

    /// The ranges for the setting, from the file or the downloaded copy. Empty setting = no ranges.
    public static func load(_ setting: String) throws -> [[String]] {
        let s = setting.trimmingCharacters(in: .whitespaces)
        if s.isEmpty { return [] }
        if let message = problem(s) { throw Failure(message: message) }
        let file = URL(string: s).flatMap { $0.scheme?.lowercased() == "https" ? cacheFile($0) : nil } ?? URL(fileURLWithPath: s)
        guard let data = try? Data(contentsOf: file) else {
            throw Failure(message: "The blocklist hasn't been downloaded yet. Check your connection and try again.")
        }
        let ranges = try parse(data)
        if ranges.isEmpty { throw Failure(message: "The blocklist has no address ranges.") }
        return ranges
    }
}

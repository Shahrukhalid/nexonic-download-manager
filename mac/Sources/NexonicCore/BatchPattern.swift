import Foundation

/// Expands download patterns such as `https://example.com/file[001-100].jpg`, `photo[a-z].png` or `part[0-90:10].bin`
/// (from–to, optional step). Several ranges multiply. Brackets that are not ranges stay as they are.
public enum BatchPattern {
    public static let limit = 10_000

    public enum Failure: Error, Equatable { case tooMany(Int) }

    private static let range = try! NSRegularExpression(pattern: #"\[(?:(\d+)-(\d+)(?::(\d+))?|([a-zA-Z])-([a-zA-Z]))\]"#)

    public static func count(_ pattern: String) -> Int {
        parts(pattern).reduce(1) { total, part in
            let n = total.multipliedReportingOverflow(by: part.count)
            return n.overflow ? Int.max : n.partialValue
        }
    }

    public static func expand(_ pattern: String) throws -> [String] {
        let ps = parts(pattern)
        let total = count(pattern)
        guard total <= limit else { throw Failure.tooMany(total) }
        var out = [""]
        for p in ps { out = out.flatMap { prefix in p.map { prefix + $0 } } }
        return out
    }

    private static func parts(_ pattern: String) -> [[String]] {
        let ns = pattern as NSString
        var parts: [[String]] = []
        var at = 0
        for m in range.matches(in: pattern, range: NSRange(location: 0, length: ns.length)) {
            parts.append([ns.substring(with: NSRange(location: at, length: m.range.location - at))])
            at = m.range.location + m.range.length
            func group(_ i: Int) -> String? {
                let r = m.range(at: i)
                return r.location == NSNotFound ? nil : ns.substring(with: r)
            }
            if let a = group(1), let b = group(2), let from = Int(a), let to = Int(b) {
                let step = max(1, Int(group(3) ?? "1") ?? 1)
                let width = a.count > 1 && a.hasPrefix("0") || a.count == b.count ? a.count : 0
                let values = from <= to ? Array(stride(from: from, through: to, by: step)) : Array(stride(from: from, through: to, by: -step))
                parts.append(values.map { width > 0 ? String(format: "%0\(width)ld", $0) : String($0) })
            } else if let a = group(4)?.unicodeScalars.first?.value, let b = group(5)?.unicodeScalars.first?.value {
                let values = a <= b ? Array(a...b) : Array((b...a).reversed())
                parts.append(values.compactMap { Unicode.Scalar($0).map { String(Character($0)) } })
            }
        }
        parts.append([ns.substring(from: at)])
        return parts
    }
}

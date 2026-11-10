import Foundation

/// Speed limits by time of day, one rule per line ("Mon-Fri 09:00-17:00 300" = 300 KB/s on weekdays during office hours).
/// The first rule that applies wins; outside every rule the normal limit applies. Same format as the other apps.
public enum SpeedSchedule {
    public struct Rule: Equatable, Sendable {
        public var days: [Bool]  // Sunday = 0
        public var start: Int    // minutes after midnight
        public var end: Int
        public var kbps: Int
    }

    static let dayNames = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"]

    /// The rules, and the first problem (the rules before it are kept).
    public static func parse(_ text: String?) -> (rules: [Rule], problem: String?) {
        var rules: [Rule] = []
        for (n, raw) in (text ?? "").components(separatedBy: "\n").enumerated() {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty || line.hasPrefix("#") { continue }
            let p = line.split(whereSeparator: { $0 == " " || $0 == "\t" }).map(String.init)
            guard p.count == 3, let d = days(p[0]), let t = times(p[1]), let kb = Int(p[2]), kb >= 0 else {
                return (rules, "Line \(n + 1): write it as days start-end KB/s, for example Mon-Fri 09:00-17:00 300.")
            }
            rules.append(Rule(days: d, start: t.0, end: t.1, kbps: min(kb, 10_000_000)))
            if rules.count == 50 { break }
        }
        return (rules, nil)
    }

    static func days(_ s: String) -> [Bool]? {
        var d = [Bool](repeating: false, count: 7)
        for part in s.lowercased().split(separator: ",").map(String.init) {
            switch part {
            case "daily", "every", "all": d = [Bool](repeating: true, count: 7); continue
            case "weekdays": for i in 1...5 { d[i] = true }; continue
            case "weekends": d[0] = true; d[6] = true; continue
            default: break
            }
            let range = part.split(separator: "-", omittingEmptySubsequences: false).map(String.init)
            guard range.count <= 2 else { return nil }
            func index(_ x: String) -> Int? { x.count >= 3 ? dayNames.firstIndex(of: String(x.prefix(3))) : nil }
            guard let a = index(range[0]), let b = range.count == 2 ? index(range[1]) : a else { return nil }
            var i = a
            while true { d[i] = true; if i == b { break }; i = (i + 1) % 7 }
        }
        return d
    }

    static func times(_ s: String) -> (Int, Int)? {
        let p = s.split(separator: "-", omittingEmptySubsequences: false).map(String.init)
        guard p.count == 2 else { return nil }
        func minutes(_ x: String) -> Int? {
            let hm = x.split(separator: ":", omittingEmptySubsequences: false)
            guard x.count == 5, hm.count == 2, let h = Int(hm[0]), let m = Int(hm[1]), (0..<24).contains(h), (0..<60).contains(m) else { return nil }
            return h * 60 + m
        }
        guard let a = minutes(p[0]), let b = minutes(p[1]), a != b else { return nil }
        return (a, b)
    }

    /// The limit the schedule sets at this time (KB/s, 0 = no limit), or nil when no rule applies.
    public static func limit(_ rules: [Rule], at date: Date, calendar: Calendar = .current) -> Int? {
        let c = calendar.dateComponents([.weekday, .hour, .minute], from: date)
        let day = (c.weekday ?? 1) - 1  // Calendar: Sunday = 1
        let t = (c.hour ?? 0) * 60 + (c.minute ?? 0)
        for r in rules {
            if r.start < r.end { if r.days[day] && t >= r.start && t < r.end { return r.kbps } }
            else if (r.days[day] && t >= r.start) || (r.days[(day + 6) % 7] && t < r.end) { return r.kbps }  // passes midnight
        }
        return nil
    }
}

/// Downloads that would repeat one you have: the same link in the list (still to do, or finished with its file still
/// there). The same address written differently (case of the scheme and host, a #fragment) counts as the same.
public enum Duplicates {
    public static func normalize(_ url: URL) -> String {
        guard var c = URLComponents(url: url, resolvingAgainstBaseURL: false), c.host != nil else { return url.absoluteString }
        c.scheme = c.scheme?.lowercased()
        c.host = c.host?.lowercased()
        c.fragment = nil
        if c.path.isEmpty { c.path = "/" }
        return c.string ?? url.absoluteString
    }

    /// The download in the list that this link would repeat, if any.
    public static func find(_ list: [DownloadRecord], _ url: URL) -> DownloadRecord? {
        let key = normalize(url)
        return list.first { r in
            guard normalize(r.url) == key else { return false }
            if r.status != .finished { return true }
            return r.savedPath.map { FileManager.default.fileExists(atPath: $0) } ?? false
        }
    }
}

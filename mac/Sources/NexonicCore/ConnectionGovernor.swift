import Foundation

/// How many connections a download may use right now, as in the Windows, Linux and Android apps. Auto (the default) starts
/// with a few, adds more while the measured speed keeps rising and stops adding when it no longer does; any mode backs off
/// when the server refuses (HTTP 429/503/509) and waits as long as the server asks (Retry-After) before growing again.
/// Not thread-safe: a transfer uses it on its own queue.
public final class ConnectionGovernor {
    public static let autoStart = 4
    public static let sample: TimeInterval = 2

    private let auto: Bool
    private let ceiling: Int
    public private(set) var allowed: Int
    public private(set) var quietUntil: Date = .distantPast
    private var bytes: Int64 = 0
    private var sampleStart: Date
    private var bestRate: Double = 0

    public init(auto: Bool, maximum: Int, now: Date = Date()) {
        self.auto = auto
        ceiling = min(max(maximum, 1), 256)
        allowed = auto ? min(Self.autoStart, ceiling) : ceiling
        sampleStart = now
    }

    public func received(_ n: Int) { bytes += Int64(n) }

    /// At the end of each sample: Auto adds connections while the speed is still rising (by at least 10 %). A fixed setting
    /// slowly returns to its number after a back-off.
    public func tick(now: Date, running: Int) {
        let span = now.timeIntervalSince(sampleStart)
        guard span >= Self.sample else { return }
        let rate = Double(bytes) / max(0.001, span)
        bytes = 0
        sampleStart = now
        guard now >= quietUntil else { return }
        if !auto { if allowed < ceiling { allowed = min(ceiling, allowed + max(1, allowed / 4)) }; return }
        guard running >= allowed else { return }  // not every allowed connection is busy yet: the sample says nothing
        if rate > bestRate * 1.10 { bestRate = rate; allowed = min(ceiling, allowed + max(2, allowed / 2)) }
    }

    /// The server refused a connection: halve (one always stays) and wait as long as it asked (30 s when it did not say,
    /// at most 10 minutes).
    public func refused(now: Date, retryAfter: TimeInterval?) {
        allowed = max(1, allowed / 2)
        let wait = retryAfter.map { min(max($0, 1), 600) } ?? 30
        quietUntil = max(quietUntil, now.addingTimeInterval(wait))
        bestRate = 0
    }

    public static func isRefusal(_ code: Int) -> Bool { code == 429 || code == 503 || code == 509 }

    /// Retry-After as seconds or an HTTP date; nil when absent or unreadable.
    public static func retryAfter(_ header: String?, now: Date = Date()) -> TimeInterval? {
        guard let h = header?.trimmingCharacters(in: .whitespaces), !h.isEmpty else { return nil }
        if let s = Int(h) { return TimeInterval(max(0, s)) }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "GMT")
        f.dateFormat = "EEE, dd MMM yyyy HH:mm:ss zzz"
        guard let at = f.date(from: h) else { return nil }
        return max(0, at.timeIntervalSince(now))
    }
}

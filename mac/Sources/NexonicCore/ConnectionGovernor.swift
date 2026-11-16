import Foundation

/// How many connections a download may use right now, as in the Windows, Linux and Android apps. Auto (the default) starts
/// with a few, adds more while the measured speed keeps rising and stops adding when it no longer does; any mode backs off
/// when the server refuses (HTTP 429/503/509) and waits as long as the server asks (Retry-After) before growing again.
/// Not thread-safe: a transfer uses it on its own queue.
public final class ConnectionGovernor {
    public static let autoStart = 4
    public static let sample: TimeInterval = 0.5
    /// Refusals this close together are one answer from the server ("too many"), not several.
    public static let burst: TimeInterval = 2

    private let auto: Bool
    private let ceiling: Int
    public private(set) var allowed: Int
    public private(set) var quietUntil: Date = .distantPast
    private var bytes: Int64 = 0
    private var sampleStart: Date
    private var bestRate: Double = 0
    private var refusedAt = Int.max  // the number of connections a server refused at: Auto stays below it
    private var burstStart = Date.distantPast
    private var burstBase = 0        // connections allowed when the current burst of refusals began
    private var burstCount = 0       // refusals in that burst
    private var proven = 0           // the most connections that recently downloaded together without a refusal
    private var burstFloor = 0       // what proven was when the burst began: the burst cannot go below it

    public init(auto: Bool, maximum: Int, now: Date = Date()) {
        self.auto = auto
        ceiling = min(max(maximum, 1), 256)
        allowed = auto ? min(Self.autoStart, ceiling) : ceiling
        sampleStart = now
    }

    public func received(_ n: Int) { bytes += Int64(n) }

    /// At the end of each sample (half a second): Auto doubles the connections while the speed is still rising (by at least 10 %),
    /// staying below a number the server refused. A fixed setting
    /// slowly returns to its number after a back-off.
    public func tick(now: Date, running: Int) {
        let span = now.timeIntervalSince(sampleStart)
        guard span >= Self.sample else { return }
        let rate = Double(bytes) / max(0.001, span)
        bytes = 0
        sampleStart = now
        // A whole sample with data flowing and no refusal: this many connections work with this server.
        if rate > 0 && now.timeIntervalSince(burstStart) >= Self.sample * 2 { proven = max(proven, min(running, allowed)) }
        guard now >= quietUntil else { return }
        if !auto { if allowed < ceiling { allowed = min(ceiling, allowed + max(1, allowed / 4)) }; return }
        guard running >= allowed else { return }  // not every allowed connection is busy yet: the sample says nothing
        if rate > bestRate * 1.10 { bestRate = rate; allowed = max(allowed, min(ceiling, refusedAt - 1, allowed * 2)) }
    }

    /// The server refused a connection. Refusals that arrive together (within `burst`) are one answer: each extra one means
    /// one connection fewer is welcome. Auto then uses exactly the number that was accepted instead of halving, so a server
    /// that allows 6 connections gets 6, not 3; a number that has just worked protects one burst. A fixed setting halves.
    /// Either way no new connection opens from zero until the server's wait is over (30 s when it did not say, at most 10 min).
    public func refused(now: Date, retryAfter: TimeInterval?) {
        let sameBurst = now >= burstStart && now.timeIntervalSince(burstStart) < Self.burst
        if sameBurst {
            burstCount += 1
        } else {
            burstStart = now; burstBase = allowed; burstCount = 1
            burstFloor = auto ? proven : 0
            proven = 0  // must work again before it protects another burst
        }
        var accepted = max(1, burstBase - burstCount)  // the connections that were not refused
        if burstFloor > accepted { accepted = max(1, min(burstFloor, refusedAt - 1)) }
        refusedAt = min(refusedAt, accepted + 1)
        if auto { allowed = min(allowed, accepted) } else if !sameBurst { allowed = max(1, allowed / 2) }
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

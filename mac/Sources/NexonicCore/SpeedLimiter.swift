import Foundation

/// Token bucket shared by every transfer. `bytesPerSecond == 0` means no limit.
public final class SpeedLimiter: @unchecked Sendable {
    private let lock = NSLock()
    private var rate: Double = 0
    private var tokens: Double = 0
    private var last = Date()

    public init(bytesPerSecond: Int = 0) { setLimit(bytesPerSecond: bytesPerSecond) }

    public func setLimit(bytesPerSecond: Int) {
        lock.lock(); defer { lock.unlock() }
        rate = Double(max(0, bytesPerSecond))
        tokens = rate
        last = Date()
    }

    /// Takes `bytes` from the bucket and returns how long the caller should wait before reading more.
    public func consume(_ bytes: Int, now: Date = Date()) -> TimeInterval {
        lock.lock(); defer { lock.unlock() }
        guard rate > 0 else { return 0 }
        tokens = min(rate, tokens + now.timeIntervalSince(last) * rate)
        last = now
        tokens -= Double(bytes)
        return tokens >= 0 ? 0 : min(5, -tokens / rate)
    }
}

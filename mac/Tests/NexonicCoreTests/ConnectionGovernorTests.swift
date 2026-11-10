import XCTest
@testable import NexonicCore

/// Adaptive connections and busy servers: the same rules as the Windows, Linux and Android apps.
final class ConnectionGovernorTests: XCTestCase {
    func testAutoGrowsWhileTheSpeedRises() {
        let start = Date(timeIntervalSince1970: 0)
        let g = ConnectionGovernor(auto: true, maximum: 64, now: start)
        XCTAssertEqual(g.allowed, ConnectionGovernor.autoStart)
        var t = start
        var speed = 1_000_000
        for _ in 0..<3 { g.received(speed * 2); t += 2; g.tick(now: t, running: g.allowed); speed *= 2 }
        let grown = g.allowed
        XCTAssertGreaterThan(grown, ConnectionGovernor.autoStart)
        for _ in 0..<4 { g.received(speed * 2); t += 2; g.tick(now: t, running: g.allowed) }
        XCTAssertEqual(g.allowed, min(64, grown * 2))  // only the first flat sample still counted as a gain
    }

    func testRefusalHalvesAndWaitsAsAsked() {
        let start = Date(timeIntervalSince1970: 0)
        let g = ConnectionGovernor(auto: false, maximum: 16, now: start)
        XCTAssertEqual(g.allowed, 16)
        g.refused(now: start + 1, retryAfter: 5)
        XCTAssertEqual(g.allowed, 8)
        XCTAssertEqual(g.quietUntil, start + 6)
        g.tick(now: start + 4, running: 8)
        XCTAssertEqual(g.allowed, 8)
        g.tick(now: start + 8, running: 8)
        XCTAssertEqual(g.allowed, 10)
        g.refused(now: start + 9, retryAfter: nil)
        XCTAssertEqual(g.quietUntil, start + 39)
        g.refused(now: start + 9, retryAfter: 3600)
        XCTAssertEqual(g.quietUntil, start + 609)
        XCTAssertEqual(ConnectionGovernor(auto: false, maximum: 999).allowed, 256)
    }

    func testAutoStaysBelowTheNumberAServerRefused() {
        let start = Date(timeIntervalSince1970: 0)
        let g = ConnectionGovernor(auto: true, maximum: 64, now: start)
        var t = start
        var speed = 1_000_000
        func rising() { t += 1; g.received(speed); g.tick(now: t, running: g.allowed); speed *= 2 }
        rising()
        XCTAssertEqual(g.allowed, 8)
        g.refused(now: t, retryAfter: 1)
        XCTAssertEqual(g.allowed, 4)
        t += 2
        for _ in 0..<6 { rising() }
        XCTAssertEqual(g.allowed, 7)
    }

    func testReadsRetryAfter() {
        XCTAssertEqual(ConnectionGovernor.retryAfter("120"), 120)
        let now = Date(timeIntervalSince1970: 1_700_000_000)
        XCTAssertEqual(ConnectionGovernor.retryAfter("Tue, 14 Nov 2023 22:13:50 GMT", now: now), 30)
        XCTAssertNil(ConnectionGovernor.retryAfter("soon"))
        XCTAssertTrue(ConnectionGovernor.isRefusal(429) && ConnectionGovernor.isRefusal(503) && !ConnectionGovernor.isRefusal(500))
    }
}

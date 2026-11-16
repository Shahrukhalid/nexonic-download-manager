import XCTest
@testable import NexonicCore

/// Quiet hours for sounds: the same rules as Windows and Linux.
final class QuietHoursTests: XCTestCase {
    private func at(_ h: Int, _ m: Int) -> (Date, Calendar) {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        return (cal.date(from: DateComponents(year: 2026, month: 11, day: 16, hour: h, minute: m))!, cal)
    }

    func testRangesThatPassMidnight() {
        for (h, m, quiet) in [(23, 0, true), (2, 30, true), (6, 59, true), (7, 0, false), (12, 0, false), (22, 0, true)] {
            let (d, cal) = at(h, m)
            XCTAssertEqual(QuietHours.contains("22:00-07:00", at: d, calendar: cal), quiet, "\(h):\(m)")
        }
    }

    func testDaytimeRangesAndInvalidOnes() {
        let (noon, cal) = at(12, 0)
        XCTAssertTrue(QuietHours.contains("09:00-17:00", at: noon, calendar: cal))
        XCTAssertFalse(QuietHours.contains("", at: noon, calendar: cal))
        XCTAssertFalse(QuietHours.contains("12:00-12:00", at: noon, calendar: cal))
        XCTAssertFalse(QuietHours.contains("noon-night", at: noon, calendar: cal))
        XCTAssertTrue(QuietHours.isValid("22:00-07:00"))
        XCTAssertFalse(QuietHours.isValid("25:00-07:00"))
    }
}

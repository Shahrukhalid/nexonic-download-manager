import XCTest
@testable import NexonicCore

final class SchedulesTests: XCTestCase {
    func testSpeedScheduleRules() {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        func at(_ d: Int, _ h: Int, _ m: Int = 0) -> Date { cal.date(from: DateComponents(year: 2026, month: 11, day: d, hour: h, minute: m))! }
        let p = SpeedSchedule.parse("# office\nMon-Fri 09:00-17:00 300\nweekends 23:00-07:00 0\nSat 10:00-12:00 50")
        XCTAssertNil(p.problem)
        XCTAssertEqual(p.rules.count, 3)
        XCTAssertEqual(SpeedSchedule.limit(p.rules, at: at(2, 10), calendar: cal), 300)      // Monday
        XCTAssertNil(SpeedSchedule.limit(p.rules, at: at(2, 18), calendar: cal))
        XCTAssertEqual(SpeedSchedule.limit(p.rules, at: at(7, 23, 30), calendar: cal), 0)    // Saturday night
        XCTAssertEqual(SpeedSchedule.limit(p.rules, at: at(9, 6), calendar: cal), 0)         // Monday morning, from Sunday
        XCTAssertNil(SpeedSchedule.limit(p.rules, at: at(3, 6), calendar: cal))              // Tuesday morning
        XCTAssertEqual(SpeedSchedule.limit(p.rules, at: at(7, 11), calendar: cal), 50)
        XCTAssertTrue(SpeedSchedule.parse("Mon 9-17 300").problem?.hasPrefix("Line 1") == true)
        XCTAssertNotNil(SpeedSchedule.parse("Funday 09:00-10:00 1").problem)
        XCTAssertEqual(SpeedSchedule.parse("Fri-Mon 22:00-23:00 5").rules.count, 1)
    }

    func testDuplicateLinks() {
        XCTAssertEqual(Duplicates.normalize(URL(string: "HTTPS://Example.COM/a%20b.zip#x")!), "https://example.com/a%20b.zip")
        XCTAssertNotEqual(Duplicates.normalize(URL(string: "https://example.com/a.zip?v=1")!), Duplicates.normalize(URL(string: "https://example.com/a.zip?v=2")!))
        let r = DownloadRecord(url: URL(string: "https://Example.com/a.zip")!, folder: URL(fileURLWithPath: "/tmp"))
        XCTAssertNotNil(Duplicates.find([r], URL(string: "https://example.com/a.zip#top")!))
        XCTAssertNil(Duplicates.find([r], URL(string: "https://example.com/b.zip")!))
    }
}

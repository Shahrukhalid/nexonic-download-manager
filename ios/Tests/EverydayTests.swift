import XCTest
@testable import NexonicKit

final class EverydayTests: XCTestCase {
    func testDownloadLimitPeriods() {
        var cap = DataCap()
        let t = Date(timeIntervalSince1970: 1_800_000_000)
        XCTAssertTrue(cap.roll(t, hours: 24))
        cap.add(1100 << 20)
        XCTAssertTrue(cap.reached(limitMB: 1024))
        XCTAssertFalse(cap.roll(t.addingTimeInterval(3600), hours: 24))
        XCTAssertTrue(cap.roll(t.addingTimeInterval(25 * 3600), hours: 24))
        XCTAssertFalse(cap.reached(limitMB: 1024))
    }

    func testFindsLinksInAnyText() {
        let links = LinkFinder.find("Here: https://h.test/a.zip and also https://h.test/b.mp4, thanks! mailto:x@y.z")
        XCTAssertEqual(links.map(\.absoluteString), ["https://h.test/a.zip", "https://h.test/b.mp4"])
    }

    func testCrashReportsLeaveOutLinks() {
        let text = CrashReports.text(reason: "failed https://h.test/f?token=abc", stack: [], version: "1.0")
        XCTAssertTrue(text.contains("[link]"))
        XCTAssertFalse(text.contains("token=abc"))
    }
}

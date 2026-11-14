import XCTest
@testable import NexonicCore

final class FinishingTests: XCTestCase {
    func testRenameMask() {
        let u = URL(string: "https://www.example.com/files/report.pdf")!
        let d = ISO8601DateFormatter().date(from: "2026-11-14T09:05:00Z")!
        let name = RenameMask.apply("{site} {date} {name}", fileName: "report.pdf", url: u, date: d, category: "Documents")
        XCTAssertEqual(name?.hasPrefix("example.com "), true)
        XCTAssertEqual(name?.hasSuffix(" report.pdf"), true)
        XCTAssertEqual(RenameMask.apply("{category}-{name}.{ext}", fileName: "a.zip", url: u, date: d, category: "Compressed"), "Compressed-a.zip")
        XCTAssertEqual(RenameMask.apply("a/b:{name}", fileName: "x.mp4", url: u, date: d, category: ""), "a-b-x.mp4")
        XCTAssertNil(RenameMask.apply("", fileName: "x.mp4", url: u, date: d, category: ""))
        XCTAssertNil(RenameMask.apply("{name}", fileName: "x.mp4", url: u, date: d, category: ""))
    }

    func testNoticeOnlyToSafeAddresses() {
        XCTAssertNotNil(FinishNotice.request("https://push.example/topic", fileName: "a.zip", ok: true))
        XCTAssertNotNil(FinishNotice.request("http://192.168.1.5:8080/hook", fileName: "a.zip", ok: true))
        XCTAssertNil(FinishNotice.request("http://push.example/topic", fileName: "a.zip", ok: true))
        XCTAssertNil(FinishNotice.request("file:///etc/passwd", fileName: "a.zip", ok: true))
        let r = FinishNotice.request("https://push.example/t", fileName: "a.zip", ok: false)!
        XCTAssertEqual(String(decoding: r.httpBody ?? Data(), as: UTF8.self), "Failed: a.zip")
    }

    func testPlayablePrefix() {
        let s = [Segment(id: 1, start: 100, end: 199, written: 50), Segment(id: 0, start: 0, end: 99, written: 100)]
        XCTAssertEqual(PartialPlay.prefix(s), 150)
        XCTAssertEqual(PartialPlay.prefix([Segment(id: 0, start: 0, end: 99, written: 10), Segment(id: 1, start: 100, end: 199, written: 100)]), 10)
        XCTAssertTrue(PartialPlay.isMedia("a.MKV"))
        XCTAssertFalse(PartialPlay.isMedia("a.zip"))
    }

    func testVideoTools() {
        XCTAssertEqual(MediaTools.audioExtension(codec: "aac"), "m4a")
        XCTAssertEqual(MediaTools.audioExtension(codec: "opus"), "opus")
        XCTAssertTrue(MediaTools.remuxArguments(input: "in.mkv", output: "out.mp4").contains("copy"))
        var o = FinishOptions(); o.skipSponsors = true
        XCTAssertEqual(o.videoArguments, ["--sponsorblock-remove", "sponsor"])
    }
}

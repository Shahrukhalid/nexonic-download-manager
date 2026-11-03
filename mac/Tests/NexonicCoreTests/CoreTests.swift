import XCTest
@testable import NexonicCore

final class CoreTests: XCTestCase {
    func testPlanCoversWholeFile() {
        let segs = SegmentPlanner.plan(size: 10_000_000, connections: 8)
        XCTAssertEqual(segs.count, 8)
        XCTAssertEqual(segs.first?.start, 0)
        XCTAssertEqual(segs.last?.end, 9_999_999)
        for (a, b) in zip(segs, segs.dropFirst()) { XCTAssertEqual(a.end + 1, b.start) }
    }

    func testSmallFilesUseFewerSegments() {
        XCTAssertEqual(SegmentPlanner.plan(size: 600_000, connections: 8).count, 1)
        XCTAssertEqual(SegmentPlanner.plan(size: nil, connections: 8), [Segment(id: 0, start: 0, end: -1)])
    }

    func testSplitHalvesTheLargestRemainingRange() {
        var segs = SegmentPlanner.plan(size: 8_000_000, connections: 2)
        segs[0].written = segs[0].length!
        segs[1].written = 1_000_000
        let split = SegmentPlanner.splitLargest(segs, nextID: 2)!
        XCTAssertEqual(split.index, 1)
        XCTAssertEqual(split.added.end, 7_999_999)
        XCTAssertEqual(split.newEnd + 1, split.added.start)
        XCTAssertEqual(split.added.start - (segs[1].start + segs[1].written), (segs[1].remaining) / 2)
        segs[1].written = segs[1].length! - 100
        XCTAssertNil(SegmentPlanner.splitLargest(segs, nextID: 2))
    }

    func testFileNames() {
        XCTAssertEqual(FileNames.fromContentDisposition("attachment; filename=\"report.pdf\""), "report.pdf")
        XCTAssertEqual(FileNames.fromContentDisposition("attachment; filename=a.txt; filename*=UTF-8''r%C3%A9sum%C3%A9.pdf"), "résumé.pdf")
        XCTAssertEqual(FileNames.sanitize("../../etc/passwd"), "_.._etc_passwd")
        XCTAssertEqual(FileNames.fromURL(URL(string: "https://example.com/files/My%20File.zip?x=1")!), "My File.zip")
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: dir.appendingPathComponent("a.zip").path, contents: Data())
        XCTAssertEqual(FileNames.unique("a.zip", in: dir).lastPathComponent, "a (1).zip")
    }

    func testCategories() {
        XCTAssertEqual(FileCategory(fileName: "x.MKV"), .video)
        XCTAssertEqual(FileCategory(fileName: "setup.dmg"), .programs)
        XCTAssertEqual(FileCategory(fileName: "noext"), .other)
    }

    func testSpeedLimiter() {
        let l = SpeedLimiter(bytesPerSecond: 1000)
        let t = Date()
        XCTAssertEqual(l.consume(1000, now: t), 0)
        XCTAssertEqual(l.consume(500, now: t), 0.5, accuracy: 0.01)
        XCTAssertEqual(SpeedLimiter().consume(1 << 30), 0)
    }

    func testNativeMessageRoundTrip() throws {
        let data = try NativeMessaging.encode(["type": "ping"])
        XCTAssertEqual(data.prefix(4), Data([15, 0, 0, 0]))
        let pipe = Pipe()
        pipe.fileHandleForWriting.write(data)
        try pipe.fileHandleForWriting.close()
        let msg = try NativeMessaging.read(from: pipe.fileHandleForReading)
        XCTAssertEqual(msg?["type"] as? String, "ping")
    }

    func testInboxAcceptsOnlyWebLinks() throws {
        XCTAssertNil(DownloadJob(url: "file:///etc/passwd").validURL)
        XCTAssertNil(DownloadJob(url: "javascript:alert(1)").validURL)
        XCTAssertNotNil(DownloadJob(url: "https://example.com/a.zip").validURL)
        let inbox = Inbox(folder: FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString))
        try inbox.drop(DownloadJob(url: "https://example.com/a.zip", cookies: "s=1"))
        let jobs = inbox.collect()
        XCTAssertEqual(jobs.first?.cookies, "s=1")
        XCTAssertTrue(inbox.collect().isEmpty)
    }
}

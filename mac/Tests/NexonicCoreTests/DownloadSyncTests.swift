import XCTest
@testable import NexonicCore

/// Keeping finished files in sync: the same rules as Windows and Linux.
final class DownloadSyncTests: XCTestCase {
    private func synced(_ hours: Int = 24) -> DownloadRecord {
        var r = DownloadRecord(url: URL(string: "https://files.test/report.csv")!, folder: FileManager.default.temporaryDirectory,
                               fileName: "report.csv")
        r.status = .finished
        r.syncHours = hours
        r.size = 100
        r.validator = "\"v1\""
        return r
    }

    func testChecksOnlyFinishedSyncedFilesWhenDue() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        var r = synced(6)
        XCTAssertTrue(DownloadSync.due(r, now: now))
        r.syncChecked = now.addingTimeInterval(-5 * 3600)
        XCTAssertFalse(DownloadSync.due(r, now: now))
        r.syncChecked = now.addingTimeInterval(-6 * 3600)
        XCTAssertTrue(DownloadSync.due(r, now: now))
        r.syncChecked = now.addingTimeInterval(3 * 3600)
        XCTAssertTrue(DownloadSync.due(r, now: now))
        r.syncHours = nil
        XCTAssertFalse(DownloadSync.due(r, now: now))
        var video = synced()
        video.siteVideo = true
        XCTAssertFalse(DownloadSync.due(video, now: now))
    }

    func testDecidesWhatIsAChange() {
        var r = synced()
        XCTAssertEqual(DownloadSync.compare(r, status: 304, validator: nil, size: nil), .unchanged)
        XCTAssertEqual(DownloadSync.compare(r, status: 206, validator: "\"v1\"", size: 999), .unchanged)
        XCTAssertEqual(DownloadSync.compare(r, status: 206, validator: "\"v2\"", size: 100), .changed)
        XCTAssertEqual(DownloadSync.compare(r, status: 404, validator: nil, size: nil), .failed)
        r.validator = nil
        XCTAssertEqual(DownloadSync.compare(r, status: 206, validator: nil, size: 101), .changed)
        XCTAssertEqual(DownloadSync.compare(r, status: 206, validator: nil, size: 100), .unchanged)
        XCTAssertEqual(DownloadSync.sizeOf(status: 206, contentRange: "bytes 0-0/150", contentLength: "1"), 150)
        XCTAssertEqual(DownloadSync.sizeOf(status: 200, contentRange: nil, contentLength: "42"), 42)
        XCTAssertNil(DownloadSync.sizeOf(status: 206, contentRange: nil, contentLength: "1"))
    }

    func testTheCheckAsksOnlyIfChanged() {
        var r = synced()
        r.cookies = "sid=1"
        let req = DownloadSync.request(for: r)
        XCTAssertEqual(req.value(forHTTPHeaderField: "Range"), "bytes=0-0")
        XCTAssertEqual(req.value(forHTTPHeaderField: "If-None-Match"), "\"v1\"")
        XCTAssertEqual(req.value(forHTTPHeaderField: "Cookie"), "sid=1")
        r.validator = "Mon, 16 Nov 2026 10:00:00 GMT"
        XCTAssertEqual(DownloadSync.request(for: r).value(forHTTPHeaderField: "If-Modified-Since"), r.validator)
    }

    func testReplacesTheOldCopyOnlyWhenTheNewOneIsComplete() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ndm-sync-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let old = dir.appendingPathComponent("report.csv")
        try "old".write(to: old, atomically: true, encoding: .utf8)
        var r = synced()
        r.savedPath = old.path
        DownloadSync.prepareRedownload(&r)
        XCTAssertEqual(r.status, .queued)
        XCTAssertEqual(r.syncReplace, old.path)
        XCTAssertNil(r.validator)
        XCTAssertEqual(try String(contentsOf: old, encoding: .utf8), "old")

        let fresh = dir.appendingPathComponent("report (1).csv")
        try "new".write(to: fresh, atomically: true, encoding: .utf8)
        r.savedPath = fresh.path
        r.fileName = "report (1).csv"
        r.status = .finished
        let now = Date()
        XCTAssertTrue(DownloadSync.finished(&r, now: now))
        XCTAssertEqual(r.savedPath, old.path)
        XCTAssertEqual(r.fileName, "report.csv")
        XCTAssertEqual(try String(contentsOf: old, encoding: .utf8), "new")
        XCTAssertFalse(FileManager.default.fileExists(atPath: fresh.path))
        XCTAssertNil(r.syncReplace)
        XCTAssertEqual(r.syncChecked, now)
    }
}

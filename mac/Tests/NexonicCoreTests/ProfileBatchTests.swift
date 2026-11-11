import XCTest
@testable import NexonicCore

final class ProfileBatchTests: XCTestCase {
    func entry(_ id: String, _ title: String = "Clip", seconds: Double? = 120, kind: String = "video", ok: Bool = true, days: Double? = 0) -> BatchEntry {
        BatchEntry(url: "https://v.test/watch?v=\(id)", title: title, duration: seconds,
                   date: days.map { Date().addingTimeInterval(-$0 * 86400) }, kind: kind, available: ok)
    }

    func testReadsTheEnginesLines() {
        let p = ProfileBatch.parseLine(#"{"url": "https://v.test/a.mp4#x", "title": "Episode two", "timestamp": 1793700000, "playlist_title": "Test Channel", "duration": 61}"#)
        XCTAssertEqual(p?.owner, "Test Channel")
        XCTAssertEqual(p?.entry.duration, 61)
        XCTAssertEqual(p?.entry.date, Date(timeIntervalSince1970: 1793700000))
        let yt = ProfileBatch.parseLine(#"{"url": "abc", "id": "abc", "ie_key": "Youtube", "title": "[Private video]"}"#)?.entry
        XCTAssertEqual(yt?.url, "https://www.youtube.com/watch?v=abc")
        XCTAssertEqual(yt?.available, false)
        XCTAssertEqual(ProfileBatch.parseLine(#"{"url": "https://www.youtube.com/shorts/x"}"#)?.entry.kind, "shorts")
        XCTAssertNil(ProfileBatch.parseLine("WARNING: x"))
    }

    func testChoosesAndCounts() {
        let found = [entry("1", "Trip"), entry("2", "Tiny", seconds: 20), entry("3", "Private", ok: false), entry("4", "Have"),
                     entry("1", "Trip"), entry("5", "No date", days: nil), entry("6", "Short", kind: "shorts")]
        var f = BatchFilter(); f.minSeconds = 60; f.kind = "videos"
        let (take, c) = ProfileBatch.choose(found, f, alreadyDownloaded: { $0.hasSuffix("=4") })
        XCTAssertEqual(take.map(\.title), ["Trip", "No date"])
        XCTAssertEqual(c, BatchCounts(discovered: 6, queued: 2, duplicates: 1, filtered: 2, inaccessible: 1))
        let newOnly = ProfileBatch.choose(found, BatchFilter(), alreadyDownloaded: { _ in false }, seen: [ProfileBatch.key(entry("1").url)]).take
        XCTAssertFalse(newOnly.contains { $0.title == "Trip" })
    }

    func testExportFoldersQualitiesAndCommand() {
        let csv = ProfileBatch.export([entry("1", "Say \"hi\", world", days: nil)], format: "csv").components(separatedBy: "\r\n")
        XCTAssertEqual(csv[1], "https://v.test/watch?v=1,\"Say \"\"hi\"\", world\",,120,video,yes")
        XCTAssertEqual(ProfileBatch.export([entry("1")], format: "txt"), "https://v.test/watch?v=1\n")
        XCTAssertEqual(ProfileBatch.folder(base: URL(fileURLWithPath: "/d"), owner: "My Channel", url: "https://v.test").path, "/d/My Channel")
        XCTAssertEqual(ProfileBatch.quality("4k").height, 2160)
        XCTAssertTrue(ProfileBatch.quality("audio").audio)
        XCTAssertEqual(SiteVideo.format(audioOnly: false, maxHeight: SiteVideo.originalQuality)[3], "res,fps,hdr:12")
        let a = ProfileBatch.arguments("https://v.test/c", quickJS: nil, start: 41, max: 60)
        XCTAssertEqual(a[a.firstIndex(of: "--playlist-end")! + 1], "100")
        XCTAssertFalse(a.contains("--username") || a.contains("--cookies") || a.contains("--cookies-from-browser"))
    }
}

import XCTest
@testable import NexonicCore

final class DisplayAndTypesTests: XCTestCase {
    func testSizesAndSpeeds() {
        XCTAssertEqual(Units.size(1_572_864, style: "binary"), "1.5 MB")
        XCTAssertEqual(Units.size(1_572_864, style: "iec"), "1.5 MiB")
        XCTAssertEqual(Units.size(1_572_864, style: "decimal"), "1.6 MB")
        XCTAssertEqual(Units.size(-1, style: "binary"), "0 B")
        XCTAssertEqual(Units.speed(2 * 1024 * 1024, style: "bytes", sizeStyle: "binary"), "2 MB/s")
        XCTAssertEqual(Units.speed(2 * 1024 * 1024, style: "bits"), "16.8 Mbit/s")
    }

    func testRelativeDates() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        XCTAssertEqual(Units.date(now.addingTimeInterval(-20), style: "relative", now: now), "Just now")
        XCTAssertEqual(Units.date(now.addingTimeInterval(-300), style: "relative", now: now), "5 min ago")
        XCTAssertEqual(Units.date(now.addingTimeInterval(-4 * 86400), style: "relative", now: now), "4 days ago")
        XCTAssertEqual(Units.date(now.addingTimeInterval(-40 * 86400), style: "relative", now: now),
                       now.addingTimeInterval(-40 * 86400).formatted(date: .abbreviated, time: .shortened))
        XCTAssertEqual(Units.date(nil), "")
    }

    func testFileTypes() {
        XCTAssertEqual(FileCategory(fileName: "llama.Q4_K_M.gguf"), .models)
        XCTAssertEqual(FileCategory(fileName: "IMG_1.CR3"), .images)
        XCTAssertEqual(FileCategory(fileName: "part.step"), .documents)
        XCTAssertEqual(FileCategory(fileName: "a.lz4"), .compressed)
        XCTAssertEqual(FileCategory.models.folder, "Models")
    }

    func testBatchListsAndFilter() throws {
        XCTAssertEqual(try BatchPattern.expand("https://x.test/p[1-2].{jpg, png}"),
                       ["https://x.test/p1.jpg", "https://x.test/p1.png", "https://x.test/p2.jpg", "https://x.test/p2.png"])
        let all = ["https://x.test/a.jpg?s=1", "https://x.test/a-thumb.jpg", "https://x.test/b.PNG", "https://x.test/c.zip"]
        XCTAssertEqual(LinkFilter.apply(all, "*.jpg !*thumb*"), ["https://x.test/a.jpg?s=1"])
        XCTAssertEqual(LinkFilter.apply(all, "zip"), ["https://x.test/c.zip"])
        XCTAssertEqual(LinkFilter.apply(all, ""), all)
    }

    func testSoundConversion() {
        XCTAssertEqual(AudioConversion.arguments("original"), [])
        XCTAssertEqual(AudioConversion.arguments("flac"), ["-x", "--audio-format", "flac"])
        XCTAssertEqual(AudioConversion.arguments("mp3"), ["-x", "--audio-format", "mp3", "--audio-quality", "0"])
        XCTAssertEqual(AudioConversion.arguments("bad"), [])
    }

    func testOwnEnginePathsMustBePrograms() {
        XCTAssertNil(EnginePaths.problem(""))
        XCTAssertNil(EnginePaths.problem("/bin/sh"))
        XCTAssertNotNil(EnginePaths.problem("/no/such/yt-dlp"))
    }
}

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

    func testCustomColourScheme() {
        let code = ColorSchemeChoice.customCode(dark: true, accent: "#3aa0f2", background: "#0e1a24")
        XCTAssertEqual(code, "custom:dark:#3AA0F2:#0E1A24")
        let s = ColorSchemeChoice.named(code)
        XCTAssertEqual(s?.dark, true)
        XCTAssertEqual(s?.accent, "#3AA0F2")
        XCTAssertNil(ColorSchemeChoice.named("custom:dark:#zzz:#000000"))
        XCTAssertEqual(ColorSchemeChoice.contrast("#000000", "#FFFFFF") ?? 0, 21, accuracy: 0.01)
        XCTAssertLessThan(ColorSchemeChoice.contrast("#777777", "#808080") ?? 9, 3)
    }

    func testDataFolderMovesAndBack() throws {
        let fm = FileManager.default
        let root = fm.temporaryDirectory.appendingPathComponent("ndm-data-\(UUID().uuidString)")
        defer { try? fm.removeItem(at: root) }
        let usual = root.appendingPathComponent("usual"), other = root.appendingPathComponent("disk/NDM")
        try fm.createDirectory(at: usual.appendingPathComponent("Parts/a"), withIntermediateDirectories: true)
        try "{}".write(to: usual.appendingPathComponent("settings.json"), atomically: true, encoding: .utf8)
        try Data(count: 50_000).write(to: usual.appendingPathComponent("Parts/a/0.part"))
        XCTAssertNotNil(DataLocation.request(usual: usual, target: "relative"))
        XCTAssertNotNil(DataLocation.request(usual: usual, target: usual.appendingPathComponent("inside").path))
        XCTAssertNil(DataLocation.request(usual: usual, target: other.path))
        XCTAssertEqual(DataLocation.current(usual).path, usual.path)
        XCTAssertNil(DataLocation.applyPending(usual))
        XCTAssertEqual(DataLocation.current(usual).standardizedFileURL.path, other.standardizedFileURL.path)
        XCTAssertEqual(try Data(contentsOf: other.appendingPathComponent("Parts/a/0.part")).count, 50_000)
        XCTAssertFalse(fm.fileExists(atPath: usual.appendingPathComponent("settings.json").path))
        XCTAssertNil(DataLocation.request(usual: usual, target: usual.path))
        XCTAssertNil(DataLocation.applyPending(usual))
        XCTAssertEqual(DataLocation.current(usual).path, usual.path)
        XCTAssertTrue(fm.fileExists(atPath: usual.appendingPathComponent("settings.json").path))
        XCTAssertFalse(fm.fileExists(atPath: other.path))
    }
}

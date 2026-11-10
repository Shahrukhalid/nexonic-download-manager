import XCTest
@testable import NexonicCore

final class EverydayFeatureTests: XCTestCase {
    func testDownloadLimitPeriods() {
        var cap = DataCap()
        let t = Date(timeIntervalSince1970: 1_800_000_000)
        XCTAssertTrue(cap.roll(t, hours: 24))
        XCTAssertFalse(cap.add(600 << 20, limitMB: 1024))
        XCTAssertTrue(cap.add(500 << 20, limitMB: 1024))
        XCTAssertFalse(cap.roll(t.addingTimeInterval(23 * 3600), hours: 24))
        XCTAssertTrue(cap.roll(t.addingTimeInterval(24 * 3600), hours: 24))
        XCTAssertFalse(cap.reached(limitMB: 1024))
        XCTAssertFalse(cap.add(5 << 30, limitMB: 0))
    }

    func testLinkGrabberFindsAndGroups() {
        let text = #"see https://a.test/one.zip, <a href="/f/Movie.part1.rar">x</a> <a href='page.html'>p</a>"#
        XCTAssertEqual(LinkGrabber.find(text), ["https://a.test/one.zip"])
        XCTAssertEqual(LinkGrabber.find(text, page: URL(string: "https://a.test/list/")),
                       ["https://a.test/f/Movie.part1.rar", "https://a.test/one.zip"])
        let groups = LinkGrabber.group(["https://h.test/Movie.part1.rar", "https://h.test/song.mp3", "https://h.test/Movie.part2.rar", "https://h.test/Tools.zip"])
        XCTAssertEqual(groups.map(\.name), ["Movie", "Tools", LinkGrabber.otherFiles])
        XCTAssertEqual(groups[0].links.count, 2)
        XCTAssertEqual(LinkGrabber.setName("Data.7z.001"), "Data")
        XCTAssertNil(LinkGrabber.setName("video.mp4"))
        XCTAssertNotNil(LinkGrabber.pageAddress("https://a.test/downloads/"))
        XCTAssertNil(LinkGrabber.pageAddress("https://a.test/file.zip"))
    }

    func testPackagesUnpackWhenAllPartsAreDone() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        func rec(_ name: String, _ status: DownloadStatus) throws -> DownloadRecord {
            let path = dir.appendingPathComponent(name)
            try Data("x".utf8).write(to: path)
            var r = DownloadRecord(url: URL(string: "https://h.test/\(name)")!, folder: dir, fileName: name)
            r.status = status
            r.savedPath = path.path
            r.package = "Movie"
            r.unpackWhenDone = true
            return r
        }
        let one = try rec("Movie.part1.rar", .finished)
        var two = try rec("Movie.part2.rar", .downloading)
        XCTAssertEqual(LinkGrabber.toUnpack(one, all: [one, two], unpackAll: false), [])
        two.status = .finished
        XCTAssertEqual(LinkGrabber.toUnpack(two, all: [one, two], unpackAll: false), [one.savedPath!])
    }

    func testColumnsAndLastFolder() {
        var r = DownloadRecord(url: URL(string: "https://h.test/a.zip")!, folder: URL(fileURLWithPath: "/data"))
        XCTAssertEqual(ListColumns.resume(r), "")
        r.size = 10
        XCTAssertEqual(ListColumns.resume(r), "No")
        r.acceptsRanges = true
        XCTAssertEqual(ListColumns.resume(r), "Yes")
        XCTAssertEqual(ListColumns.folder(r), "/data")
        var s = AppSettings()
        let tmp = FileManager.default.temporaryDirectory
        s.usedFolder(tmp)
        XCTAssertEqual(s.startFolder.standardizedFileURL.path, tmp.standardizedFileURL.path)
        s.usedFolder(s.folder)
        XCTAssertEqual(s.startFolder, s.folder)
        XCTAssertEqual(ProxyMode(rawValue: "pac"), .pac)
    }
}

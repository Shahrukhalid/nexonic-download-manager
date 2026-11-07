import XCTest
@testable import NexonicCore

/// Big downloads are written into one ".nxpart" file; space and FAT32 are checked first.
final class SingleFileTests: XCTestCase {
    func testDiskSpaceChecks() {
        let folder = FileManager.default.temporaryDirectory
        XCTAssertTrue(DiskSpace.problem(folder, size: Int64.max / 4)?.contains("Not enough free space") ?? false)
        XCTAssertNil(DiskSpace.problem(folder, size: 1024))
        XCTAssertEqual(DiskSpace.bytes(5 << 30), "5.0 GB")
        XCTAssertEqual(DiskSpace.bytes(12 << 20), "12 MB")
    }

    func testTemporaryFilesAreRemovedForUnfinishedDownloads() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let work = folder.appendingPathComponent("big.zip" + Transfer.workSuffix)
        FileManager.default.createFile(atPath: work.path, contents: Data("x".utf8))
        var r = DownloadRecord(url: URL(string: "https://example.com/big.zip")!, folder: folder)
        r.workFile = work.path
        r.status = .paused
        Transfer.deleteTemporaryFiles(&r)
        XCTAssertNil(r.workFile)
        XCTAssertFalse(FileManager.default.fileExists(atPath: work.path))
    }
}

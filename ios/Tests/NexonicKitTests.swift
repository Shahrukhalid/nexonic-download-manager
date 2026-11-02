import XCTest
@testable import NexonicKit

final class NexonicKitTests: XCTestCase {
    func testCredentialsOnlyGoToTheSameSite() {
        let origin = URL(string: "https://a.com/file.zip")!
        XCTAssertTrue(Net.sameSite(URL(string: "https://A.com:443/x")!, origin))
        XCTAssertFalse(Net.sameSite(URL(string: "https://cdn.a.com/x")!, origin))
        XCTAssertFalse(Net.sameSite(URL(string: "http://a.com/x")!, origin))
        XCTAssertTrue(Net.sameSpace(host: "a.com", port: 443, proto: "https", origin: origin))
        XCTAssertFalse(Net.sameSpace(host: "a.com", port: 443, proto: "http", origin: origin))
        XCTAssertFalse(Net.sameSpace(host: "evil.com", port: 443, proto: "https", origin: origin))
    }

    func testOnlyWebLinks() {
        XCTAssertNotNil(Net.webURL(" https://x.com/a.zip "))
        XCTAssertNil(Net.webURL("javascript:alert(1)"))
        XCTAssertNil(Net.webURL("file:///etc/passwd"))
        XCTAssertEqual(Net.total(contentRange: "bytes 0-0/12345"), 12345)
    }

    func testMediaOnAPage() {
        let html = """
        <video controls><source src="/v/lecture-1080p.mp4" label="1080p"><source src='/v/lecture-720p.mp4' res="720"></video>
        <audio src="talk.mp3"></audio>
        <meta property="og:video" content="https://cdn.x.com/og.mp4">
        <a href="slides.pdf">Slides</a> <a href="/about">About</a> <a href="stream.m3u8">Live</a> <a href="javascript:void(0)">x</a>
        """
        let found = Probe.choices(in: html, base: URL(string: "https://x.com/course/week8")!)
        let urls = found.map(\.url.absoluteString)
        XCTAssertEqual(found.first?.kind, .video)
        XCTAssertTrue(urls.contains("https://x.com/v/lecture-1080p.mp4"))
        XCTAssertEqual(found.first { $0.url.lastPathComponent == "lecture-720p.mp4" }?.label, "720p")
        XCTAssertEqual(found.first { $0.url.lastPathComponent == "talk.mp3" }?.kind, .audio)
        XCTAssertTrue(urls.contains("https://cdn.x.com/og.mp4"))
        XCTAssertTrue(urls.contains("https://x.com/course/slides.pdf"))
        XCTAssertFalse(urls.contains { $0.contains("about") || $0.contains("m3u8") || $0.hasPrefix("javascript") })
    }

    func testStoreAddsOnceAndKeepsChanges() {
        Store.shared.update { $0 = [] }
        let url = URL(string: "https://x.com/a.zip")!
        let first = Library.add(DownloadJob(url: url, fileName: "a.zip"), startNow: false, sessionID: Downloader.appSessionID)
        let again = Library.add(DownloadJob(url: url, fileName: "a.zip"), startNow: false, sessionID: Downloader.appSessionID)
        XCTAssertEqual(first.id, again.id)
        XCTAssertEqual(Store.shared.load().count, 1)
        XCTAssertEqual(Store.shared.load().first?.status, .later)
        Store.shared.update(first.id) { $0.written = 42 }
        XCTAssertEqual(Store.shared.job(first.id)?.written, 42)
        Store.shared.update { $0 = [] }
    }

    func testFinishedFilesGoIntoFilesByType() throws {
        Store.shared.update { $0 = [] }
        var job = DownloadJob(url: URL(string: "https://x.com/clip.mp4")!, fileName: "clip.mp4", status: .complete)
        let dir = AppGroup.folder.appendingPathComponent("Incoming/\(job.id.uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try Data("video".utf8).write(to: dir.appendingPathComponent("clip.mp4"))
        job.incomingPath = "Incoming/\(job.id.uuidString)/clip.mp4"
        Store.shared.update { $0 = [job] }
        SharedSettings.categoryFolders = true
        Library.importFinished()
        let saved = try XCTUnwrap(Store.shared.job(job.id)?.savedPath)
        XCTAssertTrue(saved.hasPrefix("Video/"))
        XCTAssertTrue(FileManager.default.fileExists(atPath: Library.documents.appendingPathComponent(saved).path))
        try? FileManager.default.removeItem(at: Library.documents.appendingPathComponent("Video"))
        Store.shared.update { $0 = [] }
    }

    func testSharedDesktopRules() throws {
        XCTAssertEqual(FileNames.sanitize("../a/b.zip").contains("/"), false)
        XCTAssertEqual(FileCategory(fileName: "x.MP3"), .music)
        XCTAssertEqual(Links.unwrap(URL(string: "https://www.google.com/imgres?imgurl=https%3A%2F%2Fi.com%2Fp.jpg")!).absoluteString, "https://i.com/p.jpg")
        XCTAssertEqual(try BatchPattern.expand("f[1-3].jpg"), ["f1.jpg", "f2.jpg", "f3.jpg"])
        XCTAssertEqual(Media.qualityLabel(URL(string: "https://x.com/a-1080p.mp4")!), "1080p")
    }
}

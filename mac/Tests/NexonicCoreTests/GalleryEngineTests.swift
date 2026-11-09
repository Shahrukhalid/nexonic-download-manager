import XCTest
@testable import NexonicCore

final class GalleryEngineTests: XCTestCase {
    let release = Data("""
        {"assets":[
          {"name":"gallery-dl_macos","digest":"sha256:A75F","browser_download_url":"https://github.com/gdl-org/builds/releases/download/x/gallery-dl_macos"},
          {"name":"gallery-dl_linux","digest":null,"browser_download_url":"https://github.com/gdl-org/builds/releases/download/x/gallery-dl_linux"},
          {"name":"other","digest":"sha256:aa","browser_download_url":"https://evil.test/other"}]}
        """.utf8)

    func testPicksOnlyChecksummedBuildsFromGitHub() {
        let p = GalleryDl.pick(release, asset: "gallery-dl_macos")
        XCTAssertEqual(p?.url.absoluteString, "https://github.com/gdl-org/builds/releases/download/x/gallery-dl_macos")
        XCTAssertEqual(p?.sha256, "a75f")
        XCTAssertNil(GalleryDl.pick(release, asset: "gallery-dl_linux"))
        XCTAssertNil(GalleryDl.pick(release, asset: "other"))
    }

    func testArgumentsAndOutput() {
        XCTAssertEqual(GalleryDl.arguments(url: "https://s.test/a", folder: "/d", speedLimitKBps: 200, proxyURL: "", referrer: nil),
                       ["--directory", "/d", "--limit-rate", "200k", "--", "https://s.test/a"])
        XCTAssertEqual(GalleryDl.savedFile("/d/a.jpg")?.saved, true)
        XCTAssertEqual(GalleryDl.savedFile("# /d/a.jpg")?.path, "/d/a.jpg")
        XCTAssertNil(GalleryDl.savedFile("[gallery-dl][error] Unsupported URL"))
    }

    func testGalleryBytesCountAsReceived() {
        var r = DownloadRecord(url: URL(string: "https://s.test/g")!, folder: URL(fileURLWithPath: "/tmp"))
        r.gallery = true
        r.streamBytes = 42
        XCTAssertEqual(r.received, 42)
    }
}

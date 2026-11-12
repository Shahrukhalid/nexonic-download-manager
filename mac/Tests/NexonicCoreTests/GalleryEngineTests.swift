import XCTest
@testable import NexonicCore

final class GalleryEngineTests: XCTestCase {
    private let releaseJSON = Data("""
        {"assets":[
          {"name":"gallery-dl_macos","digest":"sha256:A75F","browser_download_url":"https://github.com/gdl-org/builds/releases/download/x/gallery-dl_macos"},
          {"name":"gallery-dl_linux","digest":null,"browser_download_url":"https://github.com/gdl-org/builds/releases/download/x/gallery-dl_linux"},
          {"name":"other","digest":"sha256:aa","browser_download_url":"https://evil.test/other"}]}
        """.utf8)

    func testPicksOnlyChecksummedBuildsFromGitHub() {
        let p = GalleryDl.pick(releaseJSON, asset: "gallery-dl_macos")
        XCTAssertEqual(p?.url.absoluteString, "https://github.com/gdl-org/builds/releases/download/x/gallery-dl_macos")
        XCTAssertEqual(p?.sha256, "a75f")
        XCTAssertNil(GalleryDl.pick(releaseJSON, asset: "gallery-dl_linux"))
        XCTAssertNil(GalleryDl.pick(releaseJSON, asset: "other"))
    }

    func testProfilesAreSearchedWithTheDefaultBrowsersSignIn() {
        XCTAssertTrue(GalleryDl.isProfile("https://www.instagram.com/maheeenn10/reels/"))
        XCTAssertTrue(GalleryDl.isProfile("https://www.tiktok.com/@name"))
        XCTAssertFalse(GalleryDl.isProfile("https://www.instagram.com/p/AbC/"))
        XCTAssertFalse(GalleryDl.isProfile("https://www.facebook.com/TheSmuggers/reels/"))
        XCTAssertEqual(GalleryDl.browserName(bundleID: "com.google.Chrome"), "chrome")
        XCTAssertNil(GalleryDl.browserName(bundleID: "com.example.other"))
        XCTAssertEqual(Array(GalleryDl.listArguments(url: "https://s.test/u", max: 5, settingsFile: nil, browser: "chrome").suffix(4)),
                       ["--cookies-from-browser", "chrome", "--", "https://s.test/u"])
    }

    func testAProfileListingBecomesPhotosAndVideosToChooseFrom() {
        XCTAssertEqual(GalleryDl.listArguments(url: "https://s.test/u", max: 300, settingsFile: nil),
                       ["-N", GalleryDl.listFormat, "--range", "1-300", "-o", GalleryDl.pace, "--", "https://s.test/u"])
        let lines = #"""
        ["https://scontent.cdninstagram.com/v/a.jpg?sig=1", "jpg", "AbC", null, null, "1", "a", "Sunny day\nat home", null, null, null, 1, 2, "2025-03-25 10:00:00", null, "amna"]
        ["https://scontent.cdninstagram.com/v/b.mp4?sig=2", "mp4", "AbC", null, null, "2", "b", null, null, null, null, 2, 2, null, 12.5, "amna"]
        ["https://scontent.cdninstagram.com/v/b.mp4?sig=2", "mp4", null, null, null, null, null, null, null, null, null, null, null, null, null, null]
        not json
        """#
        let listed = GalleryDl.entries(lines)
        XCTAssertEqual(listed.owner, "amna")
        XCTAssertEqual(listed.entries.map(\.title), ["Sunny day at home [AbC-1].jpg", "amna [AbC-2].mp4"])
        XCTAssertEqual(listed.entries.map(\.kind), ["photo", "video"])
        XCTAssertEqual(listed.entries.first?.direct, true)
        XCTAssertNotNil(listed.entries.first?.date)
        XCTAssertEqual(ProfileBatch.key("https://scontent.cdninstagram.com/v/a.jpg?sig=1"), ProfileBatch.key("https://scontent.cdninstagram.com/v/a.jpg?sig=9"))
    }

    func testArgumentsAndOutput() {
        XCTAssertEqual(GalleryDl.arguments(url: "https://s.test/a", folder: "/d", speedLimitKBps: 200, referrer: nil),
                       ["--directory", "/d", "-o", GalleryDl.pace, "--limit-rate", "200k", "--", "https://s.test/a"])
        XCTAssertEqual(GalleryDl.arguments(url: "https://s.test/a", folder: "/d", speedLimitKBps: 0, referrer: nil, settingsFile: "/p/g.json"),
                       ["--directory", "/d", "-o", GalleryDl.pace, "--config", "/p/g.json", "--", "https://s.test/a"])
        XCTAssertEqual(GalleryDl.savedFile("/d/a.jpg")?.saved, true)
        XCTAssertEqual(GalleryDl.savedFile("# /d/a.jpg")?.path, "/d/a.jpg")
        XCTAssertNil(GalleryDl.savedFile("[gallery-dl][error] Unsupported URL"))
    }

    func testSiteLoginAndCookiesStayOffTheCommandLine() throws {
        XCTAssertEqual(GalleryDl.settings(cookies: nil, userAgent: "", username: nil, password: nil, proxyURL: ""), "")
        let json = GalleryDl.settings(cookies: "sid=a=b; theme=dark ;bad", userAgent: "Agent/1", username: "me", password: "pw", proxyURL: "http://p:8080")
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any])
        let e = try XCTUnwrap(root["extractor"] as? [String: Any])
        XCTAssertEqual(e["cookies"] as? [String: String], ["sid": "a=b", "theme": "dark"])
        XCTAssertEqual(e["user-agent"] as? String, "Agent/1")
        XCTAssertEqual(e["username"] as? String, "me")
        XCTAssertEqual(e["password"] as? String, "pw")
        XCTAssertEqual(e["proxy"] as? String, "http://p:8080")
        XCTAssertNil(try GalleryDl.writeSettings(""))
    }

    func testGalleryBytesCountAsReceived() {
        var r = DownloadRecord(url: URL(string: "https://s.test/g")!, folder: URL(fileURLWithPath: "/tmp"))
        r.gallery = true
        r.streamBytes = 42
        XCTAssertEqual(r.received, 42)
    }
}

import XCTest
@testable import NexonicCore

/// Security and integrity rules: private headers stay on the download's own server, resumes never mix two files.
final class HardeningTests: XCTestCase {
    func testPrivateHeadersOnlyForTheSameServer() {
        let origin = URL(string: "https://files.example.com/a")!
        XCTAssertTrue(Network.sameSite(URL(string: "https://FILES.example.com/b")!, origin))
        XCTAssertTrue(Network.sameSite(URL(string: "https://files.example.com:443/b")!, origin))
        XCTAssertFalse(Network.sameSite(URL(string: "http://files.example.com/b")!, origin))  // no step down to http
        XCTAssertFalse(Network.sameSite(URL(string: "https://cdn.example.com/b")!, origin))
        XCTAssertFalse(Network.sameSite(URL(string: "https://files.example.com:8443/b")!, origin))
    }

    func testLoginOnlyForTheSameProtectionSpace() {
        let origin = URL(string: "https://files.example.com/a")!
        func space(_ host: String, _ port: Int, _ proto: String) -> URLProtectionSpace {
            URLProtectionSpace(host: host, port: port, protocol: proto, realm: nil, authenticationMethod: NSURLAuthenticationMethodHTTPBasic)
        }
        XCTAssertTrue(Network.sameSpace(space("files.example.com", 443, "https"), origin))
        XCTAssertFalse(Network.sameSpace(space("files.example.com", 80, "http"), origin))  // after a redirect to http
        XCTAssertFalse(Network.sameSpace(space("files.example.com", 8443, "https"), origin))
        XCTAssertFalse(Network.sameSpace(space("cdn.example.com", 443, "https"), origin))
    }

    func testResumeValidators() {
        func response(_ headers: [String: String]) -> HTTPURLResponse {
            HTTPURLResponse(url: URL(string: "https://e.com/f")!, statusCode: 206, httpVersion: nil, headerFields: headers)!
        }
        XCTAssertEqual(Network.validator(response(["ETag": "\"v1\"", "Last-Modified": "Mon, 02 Nov 2026 10:00:00 GMT"])), "\"v1\"")
        XCTAssertEqual(Network.validator(response(["ETag": "W/\"weak\"", "Last-Modified": "Mon, 02 Nov 2026 10:00:00 GMT"])),
                       "Mon, 02 Nov 2026 10:00:00 GMT")
        XCTAssertNil(Network.validator(response([:])))
        XCTAssertTrue(Network.sameVersion(nil, "\"v2\""))  // lists saved before 0.6.2
        XCTAssertFalse(Network.sameVersion("\"v1\"", "\"v2\""))
    }

    func testContentRangeMustMatchTheRequest() {
        XCTAssertTrue(Network.rangeMatches("bytes 5-9/10", from: 5, size: 10))
        XCTAssertTrue(Network.rangeMatches("bytes 5-9/*", from: 5, size: 10))
        XCTAssertFalse(Network.rangeMatches("bytes 0-4/10", from: 5, size: 10))  // wrong part
        XCTAssertFalse(Network.rangeMatches("bytes 5-9/99", from: 5, size: 10))  // another file
        XCTAssertFalse(Network.rangeMatches(nil, from: 5, size: 10))
    }

    func testOlderListsStillLoad() throws {
        let json = #"[{"id":"6F9619FF-8B86-D011-B42D-00C04FC964FF","url":"https://e.com/a.zip","fileName":"a.zip","folder":"file:///tmp/","acceptsRanges":true,"status":"paused","segments":[],"added":0}]"#
        let list = try JSONDecoder().decode([DownloadRecord].self, from: Data(json.utf8))
        XCTAssertNil(list[0].validator)
    }
}

/// Cookies and passwords leave the JSON files for the Keychain; damaged lists are kept.
final class StorageTests: XCTestCase {
    func testSecretsMoveOutOfTheFiles() throws {
        var a = DownloadRecord(url: URL(string: "https://e.com/a")!, folder: URL(fileURLWithPath: "/tmp"), cookies: "sid=1")
        a.username = "me"
        a.password = "pw"
        var settings = AppSettings()
        settings.proxy.password = "proxy-pw"
        let secrets = Secrets.collect([a], settings)
        XCTAssertEqual(secrets.count, 3)
        let fileList = Secrets.stripped([a])
        let fileJSON = String(decoding: try JSONEncoder().encode(fileList), as: UTF8.self)
        XCTAssertFalse(fileJSON.contains("sid=1") || fileJSON.contains("\"pw\""))
        XCTAssertTrue(fileJSON.contains("\"me\""))  // user names are not secret
        XCTAssertEqual(Secrets.stripped(settings).proxy.password, "")

        var back = fileList
        var backSettings = Secrets.stripped(settings)
        Secrets.fill(&back, &backSettings, from: secrets)
        XCTAssertEqual(back[0].cookies, "sid=1")
        XCTAssertEqual(back[0].password, "pw")
        XCTAssertEqual(backSettings.proxy.password, "proxy-pw")

        // a value still in the file (saved by 0.6.1) is kept
        var old = [a]
        old[0].cookies = "newer=2"
        Secrets.fill(&old, &backSettings, from: secrets)
        XCTAssertEqual(old[0].cookies, "newer=2")

        let store = MemoryStore(nil)  // Keychain denied
        XCTAssertNil(store.read())
    }

    func testDamagedListIsKept() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let file = dir.appendingPathComponent("downloads.json")
        try Data("[{\"url\": ".utf8).write(to: file)
        XCTAssertNil(SavedFile.load([DownloadRecord].self, from: file))
        XCTAssertTrue(SavedFile.read([DownloadRecord].self, from: file).damaged)  // its Keychain entries are kept
        let missing = SavedFile.read([DownloadRecord].self, from: dir.appendingPathComponent("none.json"))
        XCTAssertNil(missing.value)
        XCTAssertFalse(missing.damaged)  // a first launch is not damage
        let kept = try FileManager.default.contentsOfDirectory(atPath: dir.path).filter { $0.hasPrefix("downloads.unreadable-") }
        XCTAssertEqual(kept.count, 1)
    }
}

/// yt-dlp: cookies, logins and the proxy never on the command line; the app's proxy is used.
final class VideoEngineTests: XCTestCase {
    func testPrivateOptionsLeaveTheCommandLine() {
        let args = SiteVideo.arguments(url: URL(string: "https://youtu.be/abc")!, folder: "/out", temp: "/tmp", ffmpeg: nil,
                                       quickJS: nil, audioOnly: false, referrer: "https://ref", cookies: "sid=a'b",
                                       userAgent: "UA", username: "me", password: "p w", speedLimitKBps: 0)
        let (rest, config) = SiteVideo.privateOptions(args, proxy: "http://u:p@proxy:8080")
        let line = rest.joined(separator: " ")
        XCTAssertFalse(line.contains("sid=") || line.contains("--password") || line.contains("proxy:8080"))
        XCTAssertTrue(line.contains("--referer"))
        XCTAssertEqual(rest.last, "https://youtu.be/abc")
        XCTAssertTrue(config.contains("--add-header 'Cookie:sid=a'\"'\"'b'"))
        XCTAssertTrue(config.contains("--password 'p w'"))
        XCTAssertTrue(config.contains("--proxy 'http://u:p@proxy:8080'"))
        XCTAssertEqual(SiteVideo.privateOptions(["-J", "--", "u"], proxy: nil).config, "")
    }

    func testProxyForTheVideoEngine() throws {
        XCTAssertNil(SiteVideo.proxyURL(ProxySettings()))  // system settings
        var p = ProxySettings()
        p.mode = .none
        XCTAssertEqual(SiteVideo.proxyURL(p), "")
        p.mode = .socks
        p.host = "10.0.0.1"
        p.port = 1080
        p.username = "a@b"
        p.password = "p:w"
        XCTAssertEqual(SiteVideo.proxyURL(p), "socks5://a%40b:p%3Aw@10.0.0.1:1080")
        let prepared = try SiteVideo.prepare(["-J", "--add-header", "Cookie:x=1", "--", "u"], proxy: p)
        let file = try XCTUnwrap(prepared.configFile)
        defer { try? FileManager.default.removeItem(at: file) }
        XCTAssertEqual(prepared.args.first, "--config-locations")
        let attrs = try FileManager.default.attributesOfItem(atPath: file.path)
        XCTAssertEqual((attrs[.posixPermissions] as? NSNumber)?.intValue, 0o600)
        XCTAssertTrue(try String(contentsOf: file).contains("Cookie:x=1"))
    }
}

/// Updates: only this app's own release file, checked against GitHub's published SHA-256.
final class UpdateTests: XCTestCase {
    func testUpdatesAreVerified() throws {
        let json = #"{"tag_name":"v0.7.0","html_url":"https://x/r","assets":[{"name":"NexonicDownloadManager-Mac.zip","browser_download_url":"https://github.com/Shahrukhalid/nexonic-download-manager/releases/download/v0.7.0/NexonicDownloadManager-Mac.zip","digest":"sha256:BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"}]}"#
        let r = try XCTUnwrap(Updates.parse(Data(json.utf8)))
        let sha = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        XCTAssertEqual(r.digests["NexonicDownloadManager-Mac.zip"], sha)
        XCTAssertTrue(Updates.trustedAsset(try XCTUnwrap(r.assets["NexonicDownloadManager-Mac.zip"])))
        XCTAssertFalse(Updates.trustedAsset(URL(string: "http://github.com/Shahrukhalid/nexonic-download-manager/releases/download/v1/a.zip")!))
        XCTAssertFalse(Updates.trustedAsset(URL(string: "https://github.com/someone/else/releases/download/v1/a.zip")!))
        let file = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try Data("abc".utf8).write(to: file)
        XCTAssertTrue(Updates.verify(file, sha256: sha))
        try Data("abd".utf8).write(to: file)
        XCTAssertFalse(Updates.verify(file, sha256: sha))
    }

    func testSecretsFileIsPrivateAndNeverPrompts() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: dir) }
        let store = FileStore(url: dir.appendingPathComponent("secrets.json"), migrate: false)
        XCTAssertEqual(store.read(), [:])  // nothing saved yet
        XCTAssertTrue(store.write(["proxy.password": "p"]))
        XCTAssertEqual(store.read(), ["proxy.password": "p"])
        XCTAssertTrue(store.write(["proxy.password": "q"]))  // replaced in one step
        XCTAssertEqual(store.read(), ["proxy.password": "q"])
        let mode = try FileManager.default.attributesOfItem(atPath: store.url.path)[.posixPermissions] as? Int
        XCTAssertEqual(mode, 0o600)  // only this user
        try Data("damaged".utf8).write(to: store.url)
        XCTAssertNil(store.read())  // unreadable: nil, so the app never overwrites it with an empty list
    }
}

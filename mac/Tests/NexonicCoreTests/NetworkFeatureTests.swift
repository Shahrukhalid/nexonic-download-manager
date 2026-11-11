import XCTest
@testable import NexonicCore

/// The torrent blocklist and keeping downloads on one network adapter (kill switch).
final class NetworkFeatureTests: XCTestCase {
    func testBlocklistFormats() throws {
        let text = "# comment\nBad:1.2.3.0-1.2.3.255\n001.002.004.000 - 001.002.004.010 , 000 , x\n10.0.0.0/8\n5.6.7.8\n2001:db8::/32\nnot a line\n9.9.9.9-1.1.1.1\n"
        XCTAssertEqual(try TorrentBlocklist.parse(Data(text.utf8)), [
            ["1.2.3.0", "1.2.3.255"], ["1.2.4.0", "1.2.4.10"], ["10.0.0.0", "10.255.255.255"], ["5.6.7.8", "5.6.7.8"],
            ["2001:db8::", "2001:db8:ffff:ffff:ffff:ffff:ffff:ffff"]])
    }

    func testGzipBlocklist() throws {
        // "x:9.9.9.0-9.9.9.9\n" compressed with gzip.
        let gz = Data(base64Encoded: "H4sIAAAAAAAC/6uwstQDQQNdCG3JBQDKKjP4EgAAAA==")!
        XCTAssertEqual(try TorrentBlocklist.parse(gz), [["9.9.9.0", "9.9.9.9"]])
    }

    func testBlocklistSetting() throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("blocklist-\(UUID().uuidString).txt")
        defer { try? FileManager.default.removeItem(at: file) }
        try Data("1.2.3.4\n".utf8).write(to: file)
        XCTAssertNil(TorrentBlocklist.problem(file.path))
        XCTAssertEqual(try TorrentBlocklist.load(file.path), [["1.2.3.4", "1.2.3.4"]])
        XCTAssertNotNil(TorrentBlocklist.problem("http://example.com/list"))
        XCTAssertNotNil(TorrentBlocklist.problem("/no/such/blocklist"))
        XCTAssertEqual(try TorrentBlocklist.load(""), [])
    }

    func testKillSwitch() throws {
        let loopback = try XCTUnwrap(Adapters.list().first { $0.up && $0.addresses.contains("127.0.0.1") })
        defer { Adapters.required = "" }
        Adapters.required = loopback.name
        XCTAssertNil(Adapters.problem())
        XCTAssertEqual(try SiteVideo.prepare(["https://example.com/v"], proxy: nil).args, ["--source-address", "127.0.0.1", "https://example.com/v"])
        Adapters.required = "nexonic-missing-vpn0"
        XCTAssertEqual(Adapters.problem(), Adapters.Down(adapter: "nexonic-missing-vpn0"))
        XCTAssertThrowsError(try SiteVideo.prepare(["https://example.com/v"], proxy: nil))
        var settings = AppSettings()
        settings.torrentsEnabled = true
        XCTAssertNotNil(TorrentPolicy.unavailable(settings))
        settings.network.bindAdapter = loopback.name
        Adapters.required = loopback.name
        XCTAssertEqual(try settings.torrentEngineNetwork().bind, "127.0.0.1")
    }
}

/// RSS feeds, debrid answers and colour schemes (the same rules as the Windows and Linux apps).
final class FeedsDebridTests: XCTestCase {
    let rss = Data("""
    <?xml version="1.0"?><rss xmlns:torrent="http://xmlns.ezrss.it/0.1/"><channel>
    <item><title>Show S01E01 1080p</title><link>https://example.com/1</link><guid>a1</guid><enclosure url="https://example.com/1.torrent" length="1048576000"/></item>
    <item><title>Show S01E01 720p</title><torrent:magnetURI>magnet:?xt=urn:btih:abc</torrent:magnetURI><guid>a2</guid></item>
    <item><title>Other 1080p</title><link>https://example.com/3.zip</link></item></channel></rss>
    """.utf8)

    func testParseAndRules() throws {
        let items = try Feeds.parse(rss)
        XCTAssertEqual(items.map(\.link), ["https://example.com/1.torrent", "magnet:?xt=urn:btih:abc", "https://example.com/3.zip"])
        XCTAssertEqual(items[0].size, 1_048_576_000)
        XCTAssertEqual(try Feeds.parse(Data(#"<feed xmlns="http://www.w3.org/2005/Atom"><entry><title>A</title><id>x</id><link href="https://example.com/a"/></entry></feed>"#.utf8))[0].link, "https://example.com/a")
        var f = FeedSubscription()
        f.include = "show"
        f.exclude = "cam"
        XCTAssertTrue(Feeds.wanted(f, items[0]))
        XCTAssertFalse(Feeds.wanted(f, items[2]))
        f.quality = "1080p 720p"
        f.oneOfEachEpisode = true
        XCTAssertEqual(Feeds.choose(f, items).map(\.id), ["a1"])       // one per episode, best quality
        f.include = "/S01E0[12]/"
        XCTAssertNil(Feeds.ruleProblem(f.include))
        XCTAssertNotNil(Feeds.ruleProblem("/[/"))
        XCTAssertEqual(Feeds.episodeKey("[Grp] Show Name - S01E02 1080p"), "show name|s01e02")
        XCTAssertEqual(Feeds.qualityOf("Movie 4K HDR"), "2160p")
    }

    func testDebrid() throws {
        var s = DebridSettings()
        s.service = .realDebrid; s.key = "k"; s.hosts = "filehost.example, other.example"
        XCTAssertTrue(Debrid.applies(URL(string: "https://www.filehost.example/f/1")!, s))
        XCTAssertFalse(Debrid.applies(URL(string: "https://example.com/x")!, s))
        XCTAssertEqual(try Debrid.parseAnswer(.realDebrid, Data(#"{"download":"https://dl.example/x.bin"}"#.utf8)).absoluteString, "https://dl.example/x.bin")
        XCTAssertEqual(try Debrid.parseAnswer(.allDebrid, Data(#"{"status":"success","data":{"link":"https://ad.example/x"}}"#.utf8)).absoluteString, "https://ad.example/x")
        XCTAssertEqual(try Debrid.parseAnswer(.premiumize, Data(#"{"status":"success","content":[{"link":"https://pm.example/x"}]}"#.utf8)).absoluteString, "https://pm.example/x")
        XCTAssertThrowsError(try Debrid.parseAnswer(.realDebrid, Data(#"{"error":"bad_token"}"#.utf8)))
        let req = try Debrid.request(s, link: "https://filehost.example/f")
        XCTAssertEqual(req.value(forHTTPHeaderField: "Authorization"), "Bearer k")
        XCTAssertEqual(String(decoding: req.httpBody ?? Data(), as: UTF8.self), "link=https%3A%2F%2Ffilehost.example%2Ff")
        // The key is never written into the settings.
        let json = String(decoding: try JSONEncoder().encode(s), as: UTF8.self)
        XCTAssertFalse(json.contains("\"k\""))
    }

    func testColorSchemes() {
        XCTAssertEqual(ColorSchemeChoice.builtIn.map(\.name), ["Ocean", "Forest", "Midnight", "High Contrast"])
        XCTAssertNotNil(ColorSchemeChoice.rgb(ColorSchemeChoice.named("Forest")!.accent))
        XCTAssertNil(ColorSchemeChoice.named("Nope"))
    }
}

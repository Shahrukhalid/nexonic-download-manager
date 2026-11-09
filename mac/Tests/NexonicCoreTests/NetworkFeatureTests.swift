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

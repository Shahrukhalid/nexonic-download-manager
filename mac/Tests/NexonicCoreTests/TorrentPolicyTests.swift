import NexonicCore
import NexonicTorrent
import XCTest

final class TorrentPolicyTests: XCTestCase {
    func testProxyAndSpeedLimitDoNotBlockTorrents() {
        var settings = AppSettings()
        settings.torrentsEnabled = true
        settings.speedLimitKBps = 500
        for mode in ProxyMode.allCases {
            settings.proxy.mode = mode
            XCTAssertNil(TorrentPolicy.unavailable(settings), mode.rawValue)
        }
        settings.torrentsEnabled = false
        XCTAssertNotNil(TorrentPolicy.unavailable(settings))
    }

    func testTorrentsUseTheAppProxy() {
        var settings = AppSettings()
        settings.proxy.mode = .none
        XCTAssertNil(settings.torrentSessionNetwork.proxy)
        settings.proxy.mode = .socks
        settings.proxy.host = "proxy.example.com"
        settings.proxy.port = 1080
        settings.proxy.username = "me"
        settings.proxy.password = "secret"
        XCTAssertEqual(settings.torrentSessionNetwork.proxy,
                       TorrentProxy(kind: .socks5, host: "proxy.example.com", port: 1080, username: "me", password: "secret"))
        settings.proxy.mode = .http
        XCTAssertEqual(settings.torrentSessionNetwork.proxy?.kind, .http)
        settings.proxy.host = ""
        XCTAssertNil(settings.torrentSessionNetwork.proxy)
    }

    func testTheLimitForAllDownloadsCapsTorrents() {
        var settings = AppSettings()
        XCTAssertEqual(TorrentPolicy.downloadLimit(0, settings: settings), 0)
        XCTAssertEqual(TorrentPolicy.downloadLimit(300, settings: settings), 300)
        settings.speedLimitKBps = 200
        XCTAssertEqual(TorrentPolicy.downloadLimit(nil, settings: settings), 200)
        XCTAssertEqual(TorrentPolicy.downloadLimit(300, settings: settings), 200)
        XCTAssertEqual(TorrentPolicy.downloadLimit(100, settings: settings), 100)
    }
}

import XCTest
@testable import NexonicCore

final class ProfilesTests: XCTestCase {
    func testMostSpecificSiteProfileWins() {
        var a = HostProfile(host: "example.com"); a.maxConnections = 4
        var b = HostProfile(host: "https://Files.Example.com/path"); b.maxConnections = 2
        var c = HostProfile(host: "*.other.org"); c.enabled = false
        let list = [a, b, c]
        XCTAssertEqual(HostProfiles.match(list, URL(string: "https://files.example.com/a.zip")!)?.maxConnections, 2)
        XCTAssertEqual(HostProfiles.match(list, URL(string: "https://www.example.com/a.zip")!)?.maxConnections, 4)
        XCTAssertNil(HostProfiles.match(list, URL(string: "https://notexample.com/a.zip")!))
        XCTAssertNil(HostProfiles.match(list, URL(string: "https://x.other.org/a.zip")!))
        XCTAssertEqual(HostProfiles.normalize(" *.Example.com:443/x "), "example.com")
    }

    func testSiteLoginsStayOutOfFilesAndBackups() throws {
        var s = AppSettings()
        var p = HostProfile(host: "example.com")
        p.username = "me"
        p.password = "site-secret"
        var proxy = ProxySettings(); proxy.password = "proxy-secret"
        p.proxy = proxy
        s.hostProfiles = [p]
        let secrets = Secrets.collect([], s)
        let stripped = Secrets.stripped(s)
        let json = String(data: try JSONEncoder().encode(stripped), encoding: .utf8)!
        XCTAssertFalse(json.contains("site-secret"))
        XCTAssertFalse(json.contains("proxy-secret"))
        var back = try JSONDecoder().decode(AppSettings.self, from: try JSONEncoder().encode(stripped))
        var none: [DownloadRecord] = []
        Secrets.fill(&none, &back, from: secrets)
        XCTAssertEqual(back.hostProfiles.first?.password, "site-secret")
        XCTAssertEqual(back.hostProfiles.first?.proxy?.password, "proxy-secret")

        let r = DownloadRecord(url: URL(string: "https://example.com/a.zip")!, folder: URL(fileURLWithPath: "/tmp"), cookies: "sid=1")
        let backup = String(data: try Backup.export(settings: s, queues: [.main], downloads: [r], app: "1.0"), encoding: .utf8)!
        for secret in ["site-secret", "proxy-secret", "sid=1"] { XCTAssertFalse(backup.contains(secret)) }
    }

    func testBackupRoundTripSkipsDuplicatesAndRestartsUnfinished() throws {
        var done = DownloadRecord(url: URL(string: "https://example.com/a.zip")!, folder: URL(fileURLWithPath: "/tmp"))
        done.status = .finished
        var part = DownloadRecord(url: URL(string: "https://example.com/b.zip")!, folder: URL(fileURLWithPath: "/tmp"))
        part.status = .downloading
        part.validator = "\"x\""
        part.segments = [Segment(id: 0, start: 0, end: 99)]
        let data = try Backup.export(settings: AppSettings(), queues: [.main], downloads: [done, part], app: "1.0")
        let file = try Backup.parse(data)
        let b = try XCTUnwrap(file.downloads?.first { $0.fileName == part.fileName })
        XCTAssertEqual(b.status, .paused)
        XCTAssertTrue(b.segments.isEmpty)
        XCTAssertNil(b.validator)
        XCTAssertEqual(Backup.newDownloads(file, existing: [done]).map(\.fileName), [part.fileName])
        XCTAssertThrowsError(try Backup.parse(Data("{\"hello\":1}".utf8)))
        XCTAssertThrowsError(try Backup.parse(Data("{\"format\":\"nexonic-backup-mac\",\"version\":99,\"app\":\"x\",\"created\":0}".utf8)))
    }

    func testOptionsAndPartSizes() {
        var n = NetworkOptions(); n.minSegmentKB = 1; n.retries = 0; n.readTimeoutSeconds = 1
        XCTAssertEqual(n.clamped.minSegmentKB, 64)
        XCTAssertEqual(n.clamped.retries, 1)
        XCTAssertEqual(n.clamped.readTimeoutSeconds, 5)
        var big = NetworkOptions(); big.minSegmentKB = 2048
        var site = HostProfile(host: "x.com"); site.retries = 3; site.timeoutSeconds = 20
        let t = TransferOptions.from(big, site)
        XCTAssertEqual(t.minSegment, 2048 * 1024)
        XCTAssertEqual(t.retries, 3)
        XCTAssertEqual(t.readTimeoutSeconds, 20)
        XCTAssertEqual(SegmentPlanner.plan(size: 100 << 20, connections: 8).count, 8)
        XCTAssertEqual(SegmentPlanner.plan(size: 100 << 20, connections: 8, minSegment: 40 << 20).count, 2)
        let one = SegmentPlanner.plan(size: 10 << 20, connections: 1)
        XCTAssertNotNil(SegmentPlanner.splitLargest(one, nextID: 1))
        XCTAssertNil(SegmentPlanner.splitLargest(one, nextID: 1, minSegment: 8 << 20))
        XCTAssertEqual(SpeedLimits.title(0), "No Limit")
        XCTAssertEqual(SpeedLimits.title(2048), "2 MB/s")
    }

    func testOldSettingsStillLoad() throws {
        let s = try JSONDecoder().decode(AppSettings.self, from: Data("{\"connections\":4}".utf8))
        XCTAssertEqual(s.connections, 4)
        XCTAssertEqual(s.network, NetworkOptions())
        XCTAssertTrue(s.hostProfiles.isEmpty)
    }
}

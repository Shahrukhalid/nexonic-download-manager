import XCTest
@testable import NexonicCore

/// The phone remote: the same rules and API as Windows and Linux.
final class RemoteTests: XCTestCase {
    @MainActor
    final class Fake: RemoteControl {
        var items: [DownloadRecord] = []
        var added: [URL] = []
        var paused: [UUID] = []
        func remoteDownloads() -> [DownloadRecord] { items }
        func remoteQueues() -> [DownloadQueue] { [.main] }
        func remoteAdd(_ urls: [URL], queue: String?, start: Bool) -> [UUID] { added += urls; return urls.map { _ in UUID() } }
        func remotePause(_ id: UUID) -> Bool { guard items.contains(where: { $0.id == id }) else { return false }; paused.append(id); return true }
        func remoteResume(_ id: UUID) -> Bool { items.contains { $0.id == id } }
        func remoteRemove(_ id: UUID) -> Bool { items.contains { $0.id == id } }
        func remoteStartQueue(_ id: String) -> Bool { id == DownloadQueue.mainID }
        func remoteStopQueue(_ id: String) -> Bool { id == DownloadQueue.mainID }
    }

    let key = "0123456789abcdef0123456789abcdef"

    func testReadsRequestsAndRefusesBadOnes() {
        let raw = "POST /v1/downloads HTTP/1.1\r\nHost: 10.0.0.2:47813\r\nContent-Length: 2\r\n\r\n{}"
        guard case .request(let r) = RemoteHTTP.parse(Data(raw.utf8)) else { return XCTFail("not read") }
        XCTAssertEqual(r.method, "POST")
        XCTAssertEqual(r.path, "/v1/downloads")
        XCTAssertEqual(r.headers["host"], "10.0.0.2:47813")
        XCTAssertEqual(r.body, Data("{}".utf8))
        XCTAssertEqual(RemoteHTTP.parse(Data("GET / HTTP/1.1\r\nHost: x".utf8)), .incomplete)
        XCTAssertEqual(RemoteHTTP.parse(Data("POST /v1/downloads HTTP/1.1\r\nContent-Length: 5\r\n\r\n{}".utf8)), .incomplete)
        XCTAssertEqual(RemoteHTTP.parse(Data("NONSENSE\r\n\r\n".utf8)), .bad)
        XCTAssertEqual(RemoteHTTP.parse(Data("POST / HTTP/1.1\r\nContent-Length: 999999\r\n\r\n".utf8)), .bad)
        XCTAssertEqual(RemoteHTTP.parse(Data("POST / HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n".utf8)), .bad)
        XCTAssertEqual(RemoteHTTP.parse(Data(repeating: 65, count: RemoteHTTP.maxHeader)), .bad)
    }

    func testOnlyPairedPhonesAndNoOtherPages() {
        let ok = ["host": "10.0.0.2:47813", "authorization": "Bearer " + key]
        XCTAssertNil(RemoteHTTP.check(ok, key: key))
        XCTAssertNil(RemoteHTTP.check(ok.merging(["origin": "http://10.0.0.2:47813"]) { $1 }, key: key))
        XCTAssertEqual(RemoteHTTP.check(ok.merging(["origin": "https://evil.test"]) { $1 }, key: key)?.status, 403)
        XCTAssertEqual(RemoteHTTP.check(["host": "10.0.0.2:47813", "authorization": "Bearer wrong"], key: key)?.status, 401)
        XCTAssertEqual(RemoteHTTP.check(["host": "10.0.0.2:47813"], key: key)?.status, 401)
        XCTAssertEqual(RemoteHTTP.check(["authorization": "Bearer short"], key: "short")?.status, 401)
        XCTAssertGreaterThanOrEqual(RemoteHTTP.newKey().count, 40)
        XCTAssertNotEqual(RemoteHTTP.newKey(), RemoteHTTP.newKey())
        XCTAssertEqual(RemoteHTTP.pairingURL(host: "192.168.1.5", port: 47813, key: "a_b-c"), "http://192.168.1.5:47813/#k=a_b-c")
        XCTAssertTrue(RemoteHTTP.isPrivate("192.168.1.5"))
        XCTAssertFalse(RemoteHTTP.isPrivate("8.8.8.8"))
    }

    @MainActor
    func testTheApi() throws {
        let c = Fake()
        var r = DownloadRecord(url: URL(string: "https://cdn.test/a.zip")!, folder: FileManager.default.temporaryDirectory, fileName: "a.zip")
        r.cookies = "secret=1"
        r.password = "p"
        c.items = [r]
        let (s1, list) = RemoteHTTP.route("GET", "/v1/downloads", Data(), c)
        XCTAssertEqual(s1, 200)
        let text = String(data: RemoteHTTP.json(list), encoding: .utf8)!
        XCTAssertTrue(text.contains("a.zip"))
        XCTAssertFalse(text.contains("secret"))  // never cookies or passwords
        let id = r.id.uuidString
        XCTAssertEqual(RemoteHTTP.route("POST", "/v1/downloads/\(id)/pause", Data(), c).0, 200)
        XCTAssertEqual(c.paused, [r.id])
        XCTAssertEqual(RemoteHTTP.route("POST", "/v1/downloads/\(UUID().uuidString)/pause", Data(), c).0, 404)
        XCTAssertEqual(RemoteHTTP.route("POST", "/v1/downloads", Data(#"{"url":"https://x.test/f.iso"}"#.utf8), c).0, 201)
        XCTAssertEqual(c.added, [URL(string: "https://x.test/f.iso")!])
        XCTAssertEqual(RemoteHTTP.route("POST", "/v1/downloads", Data(#"{"url":"file:///etc/passwd"}"#.utf8), c).0, 400)
        XCTAssertEqual(RemoteHTTP.route("POST", "/v1/downloads", Data("nope".utf8), c).0, 400)
        XCTAssertEqual(RemoteHTTP.route("POST", "/v1/queues/main/start", Data(), c).0, 200)
        XCTAssertEqual(RemoteHTTP.route("PUT", "/v1/downloads", Data(), c).0, 405)
        XCTAssertEqual(RemoteHTTP.route("GET", "/v2/x", Data(), c).0, 404)

        let page = RemoteHTTP.answer(.init(method: "GET", path: "/", headers: [:], body: Data()), key: key, control: c, blocked: false) {}
        XCTAssertTrue(String(decoding: page, as: UTF8.self).hasPrefix("HTTP/1.1 200"))
        var fails = 0
        let wrong = RemoteHTTP.answer(.init(method: "GET", path: "/v1/status", headers: ["authorization": "Bearer x"], body: Data()),
                                      key: key, control: c, blocked: false) { fails += 1 }
        XCTAssertTrue(String(decoding: wrong, as: UTF8.self).hasPrefix("HTTP/1.1 401"))
        XCTAssertEqual(fails, 1)
        let blocked = RemoteHTTP.answer(.init(method: "GET", path: "/v1/status", headers: [:], body: Data()), key: key, control: c, blocked: true) {}
        XCTAssertTrue(String(decoding: blocked, as: UTF8.self).hasPrefix("HTTP/1.1 429"))
    }

    func testThePageIsTheSameAsOnWindowsAndLinux() throws {
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent()
        let windows = try String(contentsOf: root.appendingPathComponent("windows/Core/Remote/remote.html"), encoding: .utf8)
        XCTAssertEqual(RemotePage.html, windows.trimmingCharacters(in: .newlines))
    }
}

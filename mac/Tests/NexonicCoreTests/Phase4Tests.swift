import XCTest
@testable import NexonicCore

final class Phase4Tests: XCTestCase {
    func testIdmSwitchesAndList() {
        guard case .jobs(let jobs) = CommandLineTool.parse(["/d", "https://a.test/f.zip", "/f", "x.zip", "/a", "/n"]) else { return XCTFail("no jobs") }
        XCTAssertEqual(jobs.first?.url, "https://a.test/f.zip")
        XCTAssertEqual(jobs.first?.fileName, "x.zip")
        XCTAssertEqual(jobs.first?.start, false)
        XCTAssertTrue(CommandLineTool.isCommand(["/d", "https://a.test/f.zip"]))
        let (args, json) = CommandLineTool.json(["list", "--json"])
        XCTAssertTrue(json)
        XCTAssertEqual(CommandLineTool.parse(args), .list)
    }

    func testListJSONLeavesOutSecrets() {
        var r = DownloadRecord(url: URL(string: "https://a.test/f.zip")!, folder: URL(fileURLWithPath: "/d"), cookies: "secret=1")
        r.password = "pw"
        let text = CommandLineTool.listJSON([r])
        XCTAssertTrue(text.contains("f.zip"))
        XCTAssertFalse(text.contains("secret"))
        XCTAssertFalse(text.contains("\"pw\""))
    }

    func testSeveralQrCodes() {
        let urls = (0..<60).map { URL(string: "https://downloads.example.test/folder/file-number-\($0).zip")! }
        let codes = ShareLinks.codes(urls)
        XCTAssertGreaterThan(codes.count, 1)
        XCTAssertEqual(codes.reduce(0) { $0 + $1.count }, 60)
    }

    func testCrashReportsLeaveOutLinks() {
        let text = CrashReports.text(reason: "failed https://h.test/f?token=abc", stack: ["0 app"], version: "1.0", at: Date())
        XCTAssertTrue(text.contains("[link]"))
        XCTAssertFalse(text.contains("token=abc"))
    }
}

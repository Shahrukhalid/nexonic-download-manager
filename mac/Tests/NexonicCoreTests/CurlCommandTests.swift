import XCTest
@testable import NexonicCore

/// cURL import (browser "Copy as cURL" for bash and Windows) and export: the same rules as Windows and Linux.
final class CurlCommandTests: XCTestCase {
    func testReadsABrowserBashCommand() throws {
        let text = "curl 'https://cdn.test/files/a%20b.zip?sig=1' \\\n  -H 'accept: */*' \\\n  -H 'referer: https://site.test/page' \\\n" +
                   "  -H $'cookie: sid=1; theme=it\\'s' \\\n  -H 'user-agent: Agent/1' \\\n  -H 'x-token: abc' \\\n  --compressed"
        let r = try CurlCommand.parse(text).get()
        XCTAssertEqual(r.url.absoluteString, "https://cdn.test/files/a%20b.zip?sig=1")
        XCTAssertEqual(r.referrer, "https://site.test/page")
        XCTAssertEqual(r.cookies, "sid=1; theme=it's")
        XCTAssertEqual(r.userAgent, "Agent/1")
        XCTAssertEqual(r.ignored, ["x-token"])
    }

    func testReadsAWindowsCmdCommandAndBasicLogin() throws {
        let text = "curl ^\"https://h.test/f.iso^\" ^\r\n  -H ^\"Authorization: Basic dXNlcjpwYXNzOndvcmQ=^\" ^\r\n  -b ^\"a=1^\""
        let r = try CurlCommand.parse(text).get()
        XCTAssertEqual(r.url.absoluteString, "https://h.test/f.iso")
        XCTAssertEqual(r.username, "user")
        XCTAssertEqual(r.password, "pass:word")
        XCTAssertEqual(r.cookies, "a=1")
    }

    func testRefusesWhatADownloadCannotRepeat() {
        func problem(_ s: String) -> String {
            if case let .failure(f) = CurlCommand.parse(s) { return f.description }
            return ""
        }
        XCTAssertTrue(problem("curl -X POST https://a.test/x").contains("POST"))
        XCTAssertTrue(problem("curl https://a.test/x --data-raw 'a=1'").contains("sends data"))
        XCTAssertTrue(problem("curl 'https://a.test/x").contains("not closed"))
        XCTAssertTrue(problem("curl file:///etc/passwd").contains("no web address"))
        XCTAssertTrue(problem("wget https://a.test/x").contains("not a cURL"))
        XCTAssertTrue(CurlCommand.looks("  curl https://a.test"))
        XCTAssertFalse(CurlCommand.looks("https://a.test/curl x"))
    }

    func testExportsACommandThatReadsBackTheSame() throws {
        var rec = DownloadRecord(url: URL(string: "https://h.test/it's.zip")!, folder: URL(fileURLWithPath: "/tmp"), fileName: "it's.zip",
                                 referrer: "https://h.test/", cookies: "a=1; b=2")
        rec.userAgent = "Agent/1"
        rec.username = "me"
        rec.password = "p"
        let cmd = CurlCommand.command(for: rec)
        XCTAssertTrue(cmd.hasPrefix("curl -L -C - -o 'it'\\''s.zip'"))
        let r = try CurlCommand.parse(cmd).get()
        XCTAssertEqual(r.url, rec.url)
        XCTAssertEqual(r.referrer, rec.referrer)
        XCTAssertEqual(r.cookies, rec.cookies)
        XCTAssertEqual(r.userAgent, rec.userAgent)
        XCTAssertEqual(r.username, "me")
        XCTAssertEqual(r.password, "p")
    }
}

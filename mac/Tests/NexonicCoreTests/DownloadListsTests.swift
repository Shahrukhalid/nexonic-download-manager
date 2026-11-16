import XCTest
@testable import NexonicCore

/// Importing download lists saved by other download managers: the same rules as Windows and Linux.
final class DownloadListsTests: XCTestCase {
    func testReadsAngleBracketLists() {
        let text = "<\r\nhttps://cdn.test/a.zip\r\nreferer: https://site.test/page\r\ncookie: sid=1\r\nUser-Agent: Agent/1\r\n" +
                   "username: me\r\npassword: p:w\r\n>\r\n<\r\nhttps://cdn.test/b.iso\r\n>\r\n"
        let r = DownloadLists.read(text)
        XCTAssertEqual(r.format, .blocks)
        XCTAssertEqual(r.jobs.count, 2)
        XCTAssertEqual(r.jobs[0].url, "https://cdn.test/a.zip")
        XCTAssertEqual(r.jobs[0].referrer, "https://site.test/page")
        XCTAssertEqual(r.jobs[0].cookies, "sid=1")
        XCTAssertEqual(r.jobs[0].userAgent, "Agent/1")
        XCTAssertEqual(r.jobs[0].username, "me")
        XCTAssertEqual(r.jobs[0].password, "p:w")
        XCTAssertEqual(r.jobs[1].url, "https://cdn.test/b.iso")
    }

    func testReadsInputFilesWithOptions() {
        let sum = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"
        let text = "# my list\nhttps://a.test/f.bin\thttps://b.test/f.bin\n  out=../../evil/name.bin\n  dir=/home/x\n" +
                   "  header=Cookie: k=v\n  referer=https://r.test/\n  http-user=u\n  http-passwd=s\n  checksum=sha-256=\(sum.uppercased())\n" +
                   "https://c.test/g.bin\n"
        let r = DownloadLists.read(text)
        XCTAssertEqual(r.format, .inputFile)
        XCTAssertEqual(r.jobs.count, 2)
        let j = r.jobs[0]
        XCTAssertEqual(j.url, "https://a.test/f.bin")
        XCTAssertEqual(j.fileName, "name.bin")
        XCTAssertNil(j.folder)
        XCTAssertEqual(j.cookies, "k=v")
        XCTAssertEqual(j.referrer, "https://r.test/")
        XCTAssertEqual(j.username, "u")
        XCTAssertEqual(j.password, "s")
        XCTAssertEqual(j.checksum, sum)
        XCTAssertEqual(r.jobs[1].url, "https://c.test/g.bin")
    }

    func testReadsJsonHtmlAndPlainText() {
        let json = DownloadLists.read(#"{"downloads":[{"url":"https://j.test/1.mp4","fileName":"one.mp4"},{"link":"https://j.test/2"},{"x":1}]}"#)
        XCTAssertEqual(json.format, .json)
        XCTAssertEqual(Set(json.jobs.map(\.url)), ["https://j.test/1.mp4", "https://j.test/2"])
        XCTAssertEqual(json.jobs.first { $0.url.hasSuffix("1.mp4") }?.fileName, "one.mp4")

        let html = DownloadLists.read("<!DOCTYPE NETSCAPE-Bookmark-file-1><DL><DT><A HREF=\"https://h.test/a?x=1&amp;y=2\">A</A><a href='javascript:alert(1)'>x</a>")
        XCTAssertEqual(html.format, .html)
        XCTAssertEqual(html.jobs.map(\.url), ["https://h.test/a?x=1&y=2"])

        let text = DownloadLists.read("https://t.test/1.zip\nfile:///etc/passwd\nhttps://t.test/1.zip\nftp://t.test/2.zip")
        XCTAssertEqual(text.format, .text)
        XCTAssertEqual(text.jobs.map(\.url), ["https://t.test/1.zip"])
    }

    func testNeverCrashesOnJunk() {
        for seed in ["<", "<\n>", "<\nhttps://x.test\n", "[", #"{"url":"#, "  out=x", "\u{FEFF}", String(repeating: "<", count: 5000)] {
            _ = DownloadLists.read(seed)
        }
        let alphabet = Array("<>\n\t =:[]{}\"'ahtps/.#")
        var generator = SystemRandomNumberGenerator()
        for _ in 0..<500 {
            let length = Int.random(in: 0..<400, using: &generator)
            let s = String((0..<length).map { _ in alphabet[Int.random(in: 0..<alphabet.count, using: &generator)] })
            _ = DownloadLists.read(s)
        }
    }
}

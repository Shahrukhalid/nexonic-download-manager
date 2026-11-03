import XCTest
@testable import NexonicCore

final class ShareLinksTests: XCTestCase {
    func testOnlyWebLinksAndShortEnoughForAQrCode() {
        let urls = ["https://example.com/a%20b.zip", "file:///etc/passwd", "https://example.com/a%20b.zip", "http://x.org/c?d=1&e=2"].compactMap(URL.init(string:))
        let (link, n) = ShareLinks.link(urls)
        XCTAssertEqual(n, 2)
        XCTAssertEqual(link, "nexonic://add?url=https%3A%2F%2Fexample.com%2Fa%2520b.zip&url=http%3A%2F%2Fx.org%2Fc%3Fd%3D1%26e%3D2")
        let many = (0..<100).compactMap { URL(string: "https://example.com/files/\($0)/archive-\($0).zip") }
        let (big, count) = ShareLinks.link(many)
        XCTAssertLessThanOrEqual(big.count, ShareLinks.maxLength)
        XCTAssertTrue((10..<100).contains(count))
    }

    func testDropsPasswordsAndFlagsPrivateLinks() {
        let (link, _) = ShareLinks.link([URL(string: "https://me:secret@example.com/f.zip")!])
        XCTAssertFalse(link.contains("secret"))
        XCTAssertTrue(ShareLinks.looksPrivate(URL(string: "https://me:secret@example.com/f.zip")!))
        XCTAssertTrue(ShareLinks.looksPrivate(URL(string: "https://b.s3.amazonaws.com/f.zip?X-Amz-Signature=abc")!))
        XCTAssertTrue(ShareLinks.looksPrivate(URL(string: "https://cdn.example.com/f.zip?downloadToken=abc")!))
        XCTAssertFalse(ShareLinks.looksPrivate(URL(string: "https://example.com/f.zip?version=2")!))
    }
}

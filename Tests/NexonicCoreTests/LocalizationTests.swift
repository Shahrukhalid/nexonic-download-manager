import XCTest

/// The Mac and iPhone apps share packaging/Localizations. Every language must have exactly the English keys.
final class LocalizationTests: XCTestCase {
    private let root = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("packaging/Localizations")

    private func table(_ code: String) throws -> [String: String] {
        let url = root.appendingPathComponent("\(code).lproj/Localizable.strings")
        let dict = try XCTUnwrap(NSDictionary(contentsOf: url) as? [String: String], "\(code) does not parse")
        return dict
    }

    func testEveryLanguageHasTheEnglishKeys() throws {
        let english = try table("en")
        XCTAssertGreaterThan(english.count, 100)
        for (key, value) in english { XCTAssertEqual(key, value) }
        for code in ["ar", "de", "es", "fr", "hi", "pt", "ur"] {
            let other = try table(code)
            XCTAssertEqual(Set(other.keys), Set(english.keys), code)
            XCTAssertTrue(other.values.allSatisfy { !$0.isEmpty }, code)
            let text = try String(contentsOf: root.appendingPathComponent("\(code).lproj/Localizable.strings"), encoding: .utf8)
            XCTAssertTrue(text.contains("needs review"), "\(code) must stay marked as a machine translation until reviewed")
            XCTAssertTrue(other["Install ndm Command…"]?.contains("ndm") ?? false, code)
        }
    }
}

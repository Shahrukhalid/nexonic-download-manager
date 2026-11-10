import NexonicCore
import XCTest

final class CustomCategoryTests: XCTestCase {
    func testSitesAndIconsRoundTripThroughTheSettingsText() {
        let text = "📚 Books: epub pdf > Books\nReleases: zip @ github.com/*/releases > Releases\n🎨 Art: @ *.deviantart.com pixiv.net"
        let (list, problem) = CustomCategory.parse(text)
        XCTAssertNil(problem)
        XCTAssertEqual(list.count, 3)
        XCTAssertEqual(list[0].icon, "📚")
        XCTAssertEqual(list[0].name, "Books")
        XCTAssertEqual(list[1].sites, "github.com/*/releases")
        XCTAssertEqual(list[2].extensions, "")
        XCTAssertEqual(CustomCategory.toLines(list), text)
        XCTAssertEqual(CustomCategory.splitIcon("C# Notes").name, "C# Notes")
    }

    func testFirstMatchingCategoryInOrderWins() {
        let keep = CategoryRules.list
        defer { CategoryRules.list = keep }
        CategoryRules.list = CustomCategory.parse(
            "Releases: zip @ https://GitHub.com/*/releases > Releases\nZips: zip > Zips\nArt: @ *.deviantart.com pixiv.net > /Art").list
        XCTAssertEqual(CategoryRules.category(for: "a.zip", url: "https://github.com/o/r/releases/download/v1/a.zip")?.name, "Releases")
        XCTAssertEqual(CategoryRules.category(for: "a.zip", url: "https://github.com/o/r/archive/a.zip")?.name, "Zips")
        XCTAssertEqual(CategoryRules.category(for: "x.png", url: "https://images.deviantart.com/x.png")?.name, "Art")
        XCTAssertNil(CategoryRules.category(for: "x.png", url: "https://notpixiv.net/x.png"))
        let dl = URL(fileURLWithPath: "/dl", isDirectory: true)
        XCTAssertEqual(CategoryRules.targetFolder(dl, fileName: "x.jpg", url: "https://pixiv.net/a", sortBuiltIn: false).path, "/Art")
        XCTAssertEqual(CategoryRules.targetFolder(dl, fileName: "a.zip", url: nil, sortBuiltIn: false).path, "/dl/Zips")
        XCTAssertEqual(CategoryRules.targetFolder(dl, fileName: "x.mp4", url: nil, sortBuiltIn: true).path, "/dl/Video")
        XCTAssertEqual(CategoryRules.targetFolder(dl, fileName: "x.mp4", url: nil, sortBuiltIn: false).path, "/dl")
    }

    func testBadLinesAreReported() {
        XCTAssertTrue(CustomCategory.parse("no colon here").problem?.contains("Line 1") == true)
        XCTAssertTrue(CustomCategory.parse("Odd: @ ").problem?.contains("Line 1") == true)
        XCTAssertFalse(CustomCategory.siteMatches(["example.com"], url: "file:///example.com/x"))
        XCTAssertTrue(CustomCategory.siteMatches(["example.com"], url: "https://cdn.example.com/f"))
        XCTAssertFalse(CustomCategory.siteMatches(["example.com"], url: "https://badexample.com/f"))
    }

    func testOldSettingsWithoutCategoriesStillLoad() throws {
        let s = try JSONDecoder().decode(AppSettings.self, from: Data("{}".utf8))
        XCTAssertEqual(s.customCategories, [])
    }
}

import XCTest
@testable import NexonicCore

final class MediaDetailsTests: XCTestCase {
    func testVideosKeepDetailsAndChaptersWhenFfmpegIsThere() {
        let url = URL(string: "https://youtu.be/abc")!
        let a = SiteVideo.arguments(url: url, folder: "/o", temp: "/t", ffmpeg: "/app/ffmpeg", quickJS: nil, audioOnly: false,
                                    referrer: nil, cookies: nil, userAgent: nil, username: nil, password: nil, speedLimitKBps: 0)
        XCTAssertTrue(a.contains("--embed-metadata"))
        XCTAssertTrue(a.contains("--embed-chapters"))
        XCTAssertEqual(a.last, "https://youtu.be/abc")
        let noTool = SiteVideo.arguments(url: url, folder: "/o", temp: "/t", ffmpeg: nil, quickJS: nil, audioOnly: false,
                                         referrer: nil, cookies: nil, userAgent: nil, username: nil, password: nil, speedLimitKBps: 0)
        XCTAssertFalse(noTool.contains("--embed-metadata"))
    }
}

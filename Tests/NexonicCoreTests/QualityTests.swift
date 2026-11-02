import XCTest
@testable import NexonicCore

/// The qualities a video is offered in, read from the video engine's details, and how a chosen one is fetched.
final class QualityTests: XCTestCase {
    static let sample = #"""
    {"title": "Talk", "duration": 100, "formats": [
      {"format_id": "sb0", "vcodec": "none", "acodec": "none", "height": 90, "width": 160},
      {"format_id": "140", "vcodec": "none", "acodec": "mp4a.40.2", "abr": 129, "filesize": 4000000},
      {"format_id": "251", "vcodec": "none", "acodec": "opus", "abr": 140, "filesize": 4500000},
      {"format_id": "18", "vcodec": "avc1.42001E", "acodec": "mp4a.40.2", "width": 640, "height": 360, "filesize": 10000000},
      {"format_id": "136", "vcodec": "avc1.4d401f", "acodec": "none", "width": 1280, "height": 720, "tbr": 2000},
      {"format_id": "248", "vcodec": "vp9", "acodec": "none", "width": 1920, "height": 1080, "tbr": 5000, "filesize": 90000000},
      {"format_id": "137", "vcodec": "avc1.640028", "acodec": "none", "width": 1920, "height": 1080, "tbr": 4000, "filesize": 100000000},
      {"format_id": "up", "vcodec": "avc1", "acodec": "none", "width": 1080, "height": 1920, "tbr": 1000, "filesize": 1}
    ]}
    """#

    func testQualitiesOnOffer() throws {
        let c = try XCTUnwrap(SiteVideo.choices(Data(Self.sample.utf8)))
        XCTAssertEqual(c.title, "Talk")
        XCTAssertEqual(c.soundSize, 4_000_000)  // the m4a sound the download picks
        XCTAssertEqual(c.video.map(\.height), [1080, 720, 360])  // an upright 1080×1920 picture counts as 1080p
        XCTAssertEqual(c.video.map(\.size), [104_000_000, 29_000_000, 10_000_000])  // picture + sound; 360p already has sound
        XCTAssertNil(SiteVideo.choices(Data("not json".utf8)))
    }

    func testSizeDescribesTheFormatTheDownloadPicks() throws {
        // Frame rate comes before codec when the download chooses, so 60 fps VP9 beats 30 fps H.264 at the same size.
        let json = #"{"duration": 10, "formats": [{"vcodec": "avc1", "acodec": "none", "width": 1920, "height": 1080, "fps": 30, "filesize": 100},"#
            + #"{"vcodec": "vp9", "acodec": "none", "width": 1920, "height": 1080, "fps": 60, "filesize": 150},"#
            + #"{"vcodec": "avc1", "acodec": "none", "width": 1280, "height": 720, "fps": 30, "filesize": 50},"#
            + #"{"vcodec": "vp9", "acodec": "none", "width": 1280, "height": 720, "fps": 30, "filesize": 40}]}"#
        let c = try XCTUnwrap(SiteVideo.choices(Data(json.utf8)))
        XCTAssertEqual(c.video.map(\.size), [150, 50])  // 1080p: the 60 fps one; 720p: same frame rate, so H.264
    }

    func testChosenQualityLimitsThePicture() {
        XCTAssertEqual(SiteVideo.format(audioOnly: false, maxHeight: 720)[3], "res:720,fps,hdr:12,vcodec:avc,acodec:m4a")
        XCTAssertEqual(SiteVideo.format(audioOnly: false, maxHeight: nil)[3], "res,fps,hdr:12,vcodec:avc,acodec:m4a")
        XCTAssertEqual(SiteVideo.format(audioOnly: true, maxHeight: 720), ["-f", "ba[ext=m4a]/ba/b"])
        let url = URL(string: "https://www.youtube.com/watch?v=x")!
        let args = SiteVideo.arguments(url: url, folder: "/tmp", temp: "/tmp", ffmpeg: nil, quickJS: nil, audioOnly: false, referrer: nil,
                                       cookies: nil, userAgent: nil, username: nil, password: nil, speedLimitKBps: 0, maxHeight: 480)
        XCTAssertTrue(args.contains("res:480,fps,hdr:12,vcodec:avc,acodec:m4a"))
        XCTAssertEqual(SiteVideo.choiceText(audioOnly: nil, maxHeight: 2160), "up to 2160p (4K)")
        XCTAssertEqual(SiteVideo.choiceText(audioOnly: true, maxHeight: 720), "sound only")
        XCTAssertEqual(SiteVideo.choiceText(audioOnly: nil, maxHeight: nil), "best quality")
    }
}

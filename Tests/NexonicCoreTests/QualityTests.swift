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

/// Pasted links: several at once, and the real file behind Google Images and other redirect links.
final class LinksTests: XCTestCase {
    func testGoogleImagesGivesThePicture() {
        let imgres = "https://www.google.com/imgres?q=desk%20wallpaper&imgurl=https%3A%2F%2Fstatic0.example.com%2Fuploads%2Fdesk-at-night.jpg%3Fw%3D1600%26h%3D900&imgrefurl=https%3A%2F%2Fwww.example.com%2Fpost%2F&docid=abc"
        XCTAssertEqual(DownloadJob(url: imgres).validURL?.absoluteString, "https://static0.example.com/uploads/desk-at-night.jpg?w=1600&h=900")
        XCTAssertEqual(Links.unwrap(URL(string: "https://www.google.com/url?q=https://example.com/a.zip&sa=D")!).absoluteString, "https://example.com/a.zip")
        XCTAssertEqual(Links.unwrap(URL(string: "https://example.com/plain.zip")!).absoluteString, "https://example.com/plain.zip")
        XCTAssertEqual(Links.unwrap(URL(string: "https://www.google.com/imgres?imgurl=javascript:alert(1)")!).host, "www.google.com")
    }

    func testSeveralLinksPastedTogether() {
        XCTAssertEqual(Links.split("https://a.example/1.jpg, https://b.example/2.jpg\n https://c.example/x,y.zip"),
                       ["https://a.example/1.jpg", "https://b.example/2.jpg", "https://c.example/x,y.zip"])
    }

    func testCategoryFolders() throws {
        XCTAssertEqual(FileCategory.video.folder, "Video")
        XCTAssertNil(FileCategory.other.folder)  // stays in the download folder
        XCTAssertTrue(AppSettings().sortIntoCategoryFolders)
        let old = try JSONDecoder().decode(AppSettings.self, from: Data(#"{"sortIntoCategoryFolders": false}"#.utf8))
        XCTAssertTrue(old.sortIntoCategoryFolders)  // the earlier setting no longer turns it off
    }
}

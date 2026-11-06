import XCTest
@testable import NexonicCore

final class PlaylistTests: XCTestCase {
    func testKnowsPlaylistLinks() {
        XCTAssertTrue(Playlists.isPlaylist("https://www.youtube.com/playlist?list=PL123"))
        XCTAssertTrue(Playlists.isPlaylist("https://www.youtube.com/@channel"))
        XCTAssertFalse(Playlists.isPlaylist("https://www.youtube.com/watch?v=abc&list=PL123"))
        XCTAssertFalse(Playlists.isPlaylist("https://youtu.be/abc"))
        XCTAssertTrue(Playlists.isPlaylist("https://soundcloud.com/artist/sets/album"))
        XCTAssertEqual(Playlists.normalize("https://www.youtube.com/@channel"), "https://www.youtube.com/@channel/videos")
        XCTAssertEqual(Array(Playlists.arguments("https://www.youtube.com/@channel", quickJS: nil).suffix(2)),
                       ["--", "https://www.youtube.com/@channel/videos"])
    }

    func testReadsEntriesWithoutDuplicates() {
        let json = #"{"title":"Mix","entries":[{"ie_key":"Youtube","id":"abc","url":"abc","title":"One","duration":61},{"url":"https://www.youtube.com/watch?v=def","title":"Two"},{"url":"https://www.youtube.com/watch?v=def"}]}"#
        let r = Playlists.parse(Data(json.utf8))
        XCTAssertEqual(r?.title, "Mix")
        XCTAssertEqual(r?.entries.map(\.url), ["https://www.youtube.com/watch?v=abc", "https://www.youtube.com/watch?v=def"])
        XCTAssertEqual(r?.entries.first?.duration, 61)
    }

    func testSubtitlesNeverFailTheVideo() {
        XCTAssertEqual(VideoSubtitles.languages(Locale(identifier: "ur_PK")), "ur.*,en.*")
        XCTAssertEqual(VideoSubtitles.languages(Locale(identifier: "en_US")), "en.*")
        XCTAssertTrue(VideoSubtitles.arguments().contains("--ignore-errors"))
    }
}

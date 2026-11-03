import XCTest
@testable import NexonicCore

final class FeatureTests: XCTestCase {
    func testBatchNumbersKeepTheirWidth() throws {
        let urls = try BatchPattern.expand("https://e.com/file[001-100].jpg")
        XCTAssertEqual(urls.count, 100)
        XCTAssertEqual(urls.first, "https://e.com/file001.jpg")
        XCTAssertEqual(urls.last, "https://e.com/file100.jpg")
        XCTAssertEqual(try BatchPattern.expand("a[1-3]"), ["a1", "a2", "a3"])
        XCTAssertEqual(try BatchPattern.expand("p[0-20:10]"), ["p0", "p10", "p20"])
    }

    func testBatchLettersAndSeveralRanges() throws {
        XCTAssertEqual(try BatchPattern.expand("[a-c].png"), ["a.png", "b.png", "c.png"])
        XCTAssertEqual(try BatchPattern.expand("s[1-2]e[1-2]"), ["s1e1", "s1e2", "s2e1", "s2e2"])
        XCTAssertEqual(try BatchPattern.expand("http://[::1]/x"), ["http://[::1]/x"])
        XCTAssertThrowsError(try BatchPattern.expand("[1-200][1-200]"))
    }

    func testChecksums() throws {
        let abc = Data("abc".utf8)
        XCTAssertEqual(Checksum.compute(.md5, data: abc), "900150983cd24fb0d6963f7d28e17f72")
        XCTAssertEqual(Checksum.compute(.sha1, data: abc), "a9993e364706816aba3e25717850c26c9cd0d89d")
        XCTAssertEqual(Checksum.compute(.sha256, data: abc), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
        XCTAssertEqual(Checksum.parse("SHA-256: BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")?.0, .sha256)
        XCTAssertNil(Checksum.parse("hello"))
        let file = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try abc.write(to: file)
        defer { try? FileManager.default.removeItem(at: file) }
        XCTAssertEqual(try Checksum.compute(.md5, file: file), "900150983cd24fb0d6963f7d28e17f72")
    }

    func testLinksAndScope() {
        let base = URL(string: "https://e.com/files/index.html")!
        let html = ##"<a href="a.zip">a</a> <a href='/files/sub/'>s</a> <img src=pic.jpg> <a href="#top">t</a> <a href="mailto:x@e.com">m</a> <a href="a.zip#x">dup</a>"##
        let links = LinkExtractor.links(in: html, base: base).map(\.absoluteString)
        XCTAssertEqual(links, ["https://e.com/files/a.zip", "https://e.com/files/sub/", "https://e.com/files/pic.jpg"])
        let o = GrabOptions(start: base, depth: 2, types: ["zip"])
        XCTAssertTrue(SiteGrabber.inScope(URL(string: "https://e.com/files/sub/x.html")!, of: o))
        XCTAssertFalse(SiteGrabber.inScope(URL(string: "https://e.com/other/x.html")!, of: o))
        XCTAssertFalse(SiteGrabber.inScope(URL(string: "https://other.com/files/x.html")!, of: o))
        XCTAssertTrue(SiteGrabber.wanted(URL(string: "https://e.com/files/a.ZIP")!, o))
        XCTAssertFalse(SiteGrabber.wanted(URL(string: "https://e.com/files/pic.jpg")!, o))
    }

    func testQueueTimetable() {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        let monday2am = Date(timeIntervalSince1970: 1_793_584_800)  // 2026-11-02 02:00 UTC, a Monday
        XCTAssertEqual(QueueClock.weekday(monday2am, calendar: cal), 1)
        var q = DownloadQueue(name: "Night")
        XCTAssertFalse(QueueClock.shouldStart(q, at: monday2am, calendar: cal))
        q.startEnabled = true
        XCTAssertTrue(QueueClock.shouldStart(q, at: monday2am, calendar: cal))
        q.days = [6, 7]
        XCTAssertFalse(QueueClock.shouldStart(q, at: monday2am, calendar: cal))
        q.days = [1]
        q.lastStartDay = QueueClock.dayKey(monday2am, calendar: cal)
        XCTAssertFalse(QueueClock.shouldStart(q, at: monday2am, calendar: cal))
        q.running = true
        q.stopEnabled = true
        q.stopTime = "02:00"
        XCTAssertTrue(QueueClock.shouldStop(q, at: monday2am, calendar: cal))
    }

    func testClipboardLinks() {
        let types = FileTypes.parse(".zip, mp4 PDF")
        XCTAssertEqual(FileTypes.downloadLink(in: " https://e.com/a.zip \n", types: types)?.absoluteString, "https://e.com/a.zip")
        XCTAssertNil(FileTypes.downloadLink(in: "https://e.com/page.html", types: types))
        XCTAssertNil(FileTypes.downloadLink(in: "see https://e.com/a.zip", types: types))
        XCTAssertNotNil(FileTypes.downloadLink(in: "https://e.com/doc.PDF?x=1", types: types))
    }

    func testOldSettingsStillLoad() throws {
        let old = #"{"connections":4,"maxActive":2,"speedLimitKBps":0,"folder":"file:///tmp/","sortIntoCategoryFolders":true,"notifications":false}"#
        let s = try JSONDecoder().decode(AppSettings.self, from: Data(old.utf8))
        XCTAssertEqual(s.connections, 4)
        XCTAssertTrue(s.sortIntoCategoryFolders)
        XCTAssertTrue(s.monitorClipboard)
        XCTAssertEqual(s.proxy.mode, .system)
    }
}

final class CommandLineTests: XCTestCase {
    func testLinksAndOptions() {
        guard case .jobs(let jobs) = CommandLineTool.parse(["https://e.com/a.zip", "--queue", "Night", "--later", "--limit", "200"]) else {
            return XCTFail("expected jobs")
        }
        XCTAssertEqual(jobs.count, 1)
        XCTAssertEqual(jobs[0].queue, "Night")
        XCTAssertEqual(jobs[0].start, false)
        XCTAssertEqual(jobs[0].speedLimitKBps, 200)
    }

    func testBatchAndErrors() {
        guard case .jobs(let jobs) = CommandLineTool.parse(["batch", "https://e.com/f[1-5].jpg"]) else { return XCTFail("expected jobs") }
        XCTAssertEqual(jobs.count, 5)
        XCTAssertEqual(CommandLineTool.parse(["help"]), .help)
        if case .failure = CommandLineTool.parse(["https://e.com/a", "https://e.com/b", "--name", "x"]) {} else { XCTFail() }
        if case .failure = CommandLineTool.parse(["https://e.com/a", "--checksum", "nope"]) {} else { XCTFail() }
        XCTAssertTrue(CommandLineTool.isCommand(["https://e.com/a"]))
        XCTAssertFalse(CommandLineTool.isCommand(["chrome-extension://abc/"]))
        XCTAssertFalse(CommandLineTool.isCommand([]))
    }

    func testWords() {
        XCTAssertEqual(CommandLineTool.words(#""/opt/scan tool/scan" --quiet  -x"#), ["/opt/scan tool/scan", "--quiet", "-x"])
        XCTAssertEqual(CommandLineTool.words(""), [])
    }
}

final class HLSTests: XCTestCase {
    func testMasterPicksBestQualityAndSound() throws {
        let text = """
        #EXTM3U
        #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aud",NAME="English",DEFAULT=YES,URI="audio/en.m3u8"
        #EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,CODECS="avc1.4d401e,mp4a.40.2",AUDIO="aud"
        low/index.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080,AUDIO="aud"
        hd/index.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720,AUDIO="aud"
        mid/index.m3u8
        """
        guard case .master(let variants, let audio) = try HLS.parse(text, base: URL(string: "https://e.com/v/master.m3u8")!) else {
            return XCTFail("expected a master playlist")
        }
        XCTAssertEqual(variants.count, 3)
        XCTAssertEqual(variants[0].audioGroup, "aud")
        XCTAssertEqual(HLS.best(variants)?.url.absoluteString, "https://e.com/v/hd/index.m3u8")
        XCTAssertEqual(audio["aud"]?.absoluteString, "https://e.com/v/audio/en.m3u8")
    }

    func testMediaPlaylist() throws {
        let text = """
        #EXTM3U
        #EXT-X-TARGETDURATION:6
        #EXT-X-MAP:URI="init.mp4"
        #EXTINF:6.0,
        seg1.m4s
        #EXTINF:6.0,
        https://cdn.e.com/seg2.m4s
        #EXT-X-ENDLIST
        """
        guard case .media(let m) = try HLS.parse(text, base: URL(string: "https://e.com/v/hd/index.m3u8")!) else {
            return XCTFail("expected a media playlist")
        }
        XCTAssertEqual(m.segments.map(\.absoluteString), ["https://e.com/v/hd/seg1.m4s", "https://cdn.e.com/seg2.m4s"])
        XCTAssertEqual(m.initSegment?.absoluteString, "https://e.com/v/hd/init.mp4")
        XCTAssertTrue(m.isFragmentedMP4)
        XCTAssertEqual(m.encryption, "NONE")
    }

    func testProtectedAndInvalid() throws {
        let text = "#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"skd://x\"\n#EXTINF:6,\na.ts\n"
        guard case .media(let m) = try HLS.parse(text, base: URL(string: "https://e.com/i.m3u8")!) else { return XCTFail() }
        XCTAssertEqual(m.encryption, "SAMPLE-AES")
        XCTAssertThrowsError(try HLS.parse("<html>", base: URL(string: "https://e.com/")!))
        XCTAssertTrue(HLS.isPlaylistURL(URL(string: "https://e.com/live/index.M3U8?token=1")!))
    }
}

final class SiteVideoTests: XCTestCase {
    func testSupportedSites() {
        XCTAssertTrue(SiteVideo.isSupported(URL(string: "https://www.youtube.com/watch?v=abc")!))
        XCTAssertTrue(SiteVideo.isSupported(URL(string: "https://youtu.be/abc")!))
        XCTAssertTrue(SiteVideo.isSupported(URL(string: "https://m.facebook.com/watch/?v=1")!))
        XCTAssertTrue(SiteVideo.isSupported(URL(string: "https://www.instagram.com/reel/xyz/")!))
        XCTAssertFalse(SiteVideo.isSupported(URL(string: "https://notyoutube.com/x")!))
        XCTAssertFalse(SiteVideo.isSupported(URL(string: "https://example.com/a.mp4")!))
    }

    func testProgressAndResult() {
        XCTAssertEqual(SiteVideo.progress("NDMP 1024 2048 NA 512.5"), SiteVideo.Progress(downloaded: 1024, total: 2048, speed: 512.5))
        XCTAssertEqual(SiteVideo.progress("NDMP 10 NA 900.0 NA")?.total, 900)
        XCTAssertNil(SiteVideo.progress("[download] 5%"))
        XCTAssertEqual(SiteVideo.savedFile("NDMF /Users/me/Downloads/Talk [abc].mp4"), "/Users/me/Downloads/Talk [abc].mp4")
        XCTAssertEqual(SiteVideo.error("WARNING: x\nERROR: [youtube] abc: Video unavailable"), "Video unavailable")
        XCTAssertTrue(SiteVideo.error("ERROR: This video is DRM protected").contains("copy-protected"))
        XCTAssertTrue(SiteVideo.error("ERROR: [vimeo] 1111417978: The web client only works when logged-in. Use --cookies, --cookies-from-browser, --username and --password, --netrc-cmd, or --netrc (vimeo) to provide account credentials.").hasPrefix("The site only lets signed-in users"))
    }

    func testArguments() {
        let a = SiteVideo.arguments(url: URL(string: "https://youtu.be/abc")!, folder: "/out", temp: "/tmp/x", ffmpeg: "/app/ffmpeg",
                                    quickJS: "/app/qjs", audioOnly: false, referrer: nil, cookies: "a=1", userAgent: nil,
                                    username: nil, password: nil, speedLimitKBps: 500)
        XCTAssertEqual(a.last, "https://youtu.be/abc")
        XCTAssertTrue(a.contains("bv*+ba/b"))
        XCTAssertTrue(a.contains("--progress"))
        XCTAssertTrue(a.contains("quickjs:/app/qjs"))
        XCTAssertTrue(a.contains("/app"))
        XCTAssertTrue(a.contains("500K"))
        XCTAssertTrue(a.contains("Cookie:a=1"))
        let audio = SiteVideo.arguments(url: URL(string: "https://youtu.be/abc")!, folder: "/o", temp: "/t", ffmpeg: nil, quickJS: nil,
                                        audioOnly: true, referrer: nil, cookies: nil, userAgent: nil, username: nil, password: nil,
                                        speedLimitKBps: 0)
        XCTAssertTrue(audio.contains("ba[ext=m4a]/ba/b"))
    }

    func testVideoInfoAndReuse() throws {
        let json = #"{"title":"Koi Song","ext":"mp4","requested_formats":[{"filesize":1000},{"filesize_approx":234}]}"#
        let info = try XCTUnwrap(SiteVideo.info(Data(json.utf8)))
        XCTAssertEqual(info, SiteVideo.Info(title: "Koi Song", ext: "mp4", size: 1234))
        XCTAssertNil(SiteVideo.info(Data("{}".utf8)))
        let probe = SiteVideo.infoArguments(url: URL(string: "https://youtu.be/abc")!, quickJS: nil, audioOnly: false, referrer: nil,
                                            cookies: nil, userAgent: nil, username: nil, password: nil)
        XCTAssertTrue(probe.contains("-J"))
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("ndm-info-test.json")
        try Data(json.utf8).write(to: file)
        defer { try? FileManager.default.removeItem(at: file) }
        let a = SiteVideo.arguments(url: URL(string: "https://youtu.be/abc")!, folder: "/o", temp: "/t", ffmpeg: nil, quickJS: nil,
                                    audioOnly: false, referrer: nil, cookies: nil, userAgent: nil, username: nil, password: nil,
                                    speedLimitKBps: 0, infoJSON: file.path)
        XCTAssertEqual(Array(a.suffix(2)), ["--load-info-json", file.path])
    }

    func testVideoPiecesAddUpWithoutInflating() {
        var parts = SiteVideo.PartProgress()
        _ = parts.add(.init(downloaded: 100, total: 1000, speed: nil, part: "137"))
        _ = parts.add(.init(downloaded: 80, total: 1000, speed: nil, part: "137"))   // fragments report out of order
        _ = parts.add(.init(downloaded: 1000, total: 1000, speed: nil, part: "137"))
        let r = parts.add(.init(downloaded: 50, total: 200, speed: nil, part: "140"))
        XCTAssertEqual(r.done, 1050)
        XCTAssertEqual(r.total, 1200)
        XCTAssertEqual(SiteVideo.progress("NDMP 10 20 NA 1.5 137")?.part, "137")
    }

    func testVideoPiecesStepsAndName() {
        var parts = SiteVideo.PartProgress()
        _ = parts.add(SiteVideo.progress("NDMP 300 1000 NA 5.0 137 1080 avc1.640028")!)
        _ = parts.add(SiteVideo.progress("NDMP 20 40 NA 5.0 140 NA none")!)
        XCTAssertEqual(parts.pieces.map(\.name), ["Picture 1080p", "Sound"])
        XCTAssertEqual(parts.pieces.first?.done, 300)
        XCTAssertEqual(SiteVideo.step("[Merger] Merging formats into \"x.mp4\""), "Joining picture and sound…")
        XCTAssertNil(SiteVideo.step("[download] 50%"))
        XCTAssertEqual(SiteVideo.destinationName("[download] Destination: /t/My Song.f137.mp4"), "My Song.mp4")
    }

    func testUpdateVersionsAndNotes() {
        XCTAssertTrue(Updates.isNewer("0.6.0", than: "0.5.9"))
        XCTAssertTrue(Updates.isNewer("0.10.0", than: "0.9.1"))
        XCTAssertFalse(Updates.isNewer("0.5.9", than: "0.5.9"))
        XCTAssertFalse(Updates.isNewer("0.5.8", than: "0.5.9"))
        let notes = Updates.notes("## 0.6.0\n\n- **New:** updates\n\n## 0.5.9\n\n- old", after: "0.5.9")
        XCTAssertTrue(notes.contains("Version 0.6.0"))
        XCTAssertTrue(notes.contains("New: updates"))
        XCTAssertFalse(notes.contains("old"))
        let json = #"{"tag_name":"v0.6.0","html_url":"https://x/r","body":"b","assets":[{"name":"a.zip","browser_download_url":"https://x/a.zip"}]}"#
        let r = Updates.parse(Data(json.utf8))
        XCTAssertEqual(r?.version, "0.6.0")
        XCTAssertEqual(r?.assets["a.zip"], URL(string: "https://x/a.zip"))
    }
}

import Foundation

/// What the app needs from any running download.
public protocol Downloader: AnyObject {
    var onChange: (@Sendable (DownloadRecord) -> Void)? { get set }
    var ownLimiter: SpeedLimiter { get }
    func start()
    func pause()
    func discard()
}

extension Transfer: Downloader {}

/// Downloads streamed video (HLS): picks the best quality, fetches the pieces in parallel, resumes after a pause or
/// restart, joins them and, with the built-in video tools, turns the result into one .mp4 with picture and sound.
public final class StreamTransfer: Downloader, @unchecked Sendable {
    public var onChange: (@Sendable (DownloadRecord) -> Void)?
    public let ownLimiter = SpeedLimiter()

    private var rec: DownloadRecord
    private let lock = NSLock()
    private let limiter: SpeedLimiter
    private let connections: Int
    private let sortIntoCategories: Bool
    private let session: URLSession
    private var task: Task<Void, Never>?
    private var lastNotify = Date.distantPast

    public var record: DownloadRecord { lock.lock(); defer { lock.unlock() }; return rec }
    private var partsFolder: URL { AppPaths.parts.appendingPathComponent(rec.id.uuidString, isDirectory: true) }

    public init(record: DownloadRecord, connections: Int, limiter: SpeedLimiter, sortIntoCategories: Bool = false,
                proxy: ProxySettings = ProxySettings()) {
        rec = record
        self.limiter = limiter
        self.connections = max(1, min(8, connections))
        self.sortIntoCategories = sortIntoCategories
        let config = Network.configuration(proxy: proxy)
        config.httpShouldSetCookies = false
        config.httpCookieAcceptPolicy = .never
        config.timeoutIntervalForRequest = 30
        config.httpMaximumConnectionsPerHost = 16
        // Redirects to another host lose the cookies and the login (see RedirectGuard).
        session = URLSession(configuration: config, delegate: RedirectGuard(origin: record.url), delegateQueue: nil)
        ownLimiter.setLimit(bytesPerSecond: (record.speedLimitKBps ?? 0) * 1024)
    }

    public func start() { task = Task { await run() } }

    public func pause() {
        change { $0.status = .paused }
        task?.cancel()
        notify(force: true)
    }

    public func discard() {
        task?.cancel()
        try? FileManager.default.removeItem(at: partsFolder)
    }

    // MARK: running

    private func change(_ f: (inout DownloadRecord) -> Void) {
        lock.lock()
        f(&rec)
        lock.unlock()
    }

    private func notify(force: Bool = false) {
        let now = Date()
        lock.lock()
        let due = force || now.timeIntervalSince(lastNotify) > 0.25
        if due { lastNotify = now }
        let copy = rec
        lock.unlock()
        if due { onChange?(copy) }
    }

    private func request(_ url: URL) -> URLRequest {
        let r = record
        var req = URLRequest(url: url)
        if let ref = r.referrer, !ref.isEmpty { req.setValue(ref, forHTTPHeaderField: "Referer") }
        if let ua = r.userAgent, !ua.isEmpty { req.setValue(ua, forHTTPHeaderField: "User-Agent") }
        // Pieces often live on other hosts (CDNs): cookies and the login only go to the playlist's own server.
        guard Network.sameSite(url, r.url) else { return req }
        if let c = r.cookies, !c.isEmpty { req.setValue(c, forHTTPHeaderField: "Cookie") }
        if let u = r.username, !u.isEmpty {
            let token = Data("\(u):\(r.password ?? "")".utf8).base64EncodedString()
            req.setValue("Basic \(token)", forHTTPHeaderField: "Authorization")
        }
        return req
    }

    private func fetch(_ url: URL) async throws -> Data {
        var attempt = 0
        while true {
            try Task.checkCancellation()
            do {
                let (data, response) = try await session.data(for: request(url))
                let code = (response as? HTTPURLResponse)?.statusCode ?? 0
                if [401, 403, 404, 410].contains(code), attempt >= 1 {
                    throw HTTPError(code: code)
                }
                guard (200..<300).contains(code) else { throw HTTPError(code: code) }
                guard !data.isEmpty else { throw URLError(.zeroByteResource) }  // retried like any other failure
                return data
            } catch is CancellationError {
                throw CancellationError()
            } catch {
                attempt += 1
                if attempt >= 6 || Task.isCancelled { throw error }
                try await Task.sleep(nanoseconds: UInt64(min(30, pow(2, Double(attempt - 1)))) * 1_000_000_000)
            }
        }
    }

    struct HTTPError: Error, LocalizedError {
        let code: Int
        var errorDescription: String? {
            [403, 404, 410].contains(code) ? "The video link has expired (HTTP \(code)). Play the video again and use Refresh Link"
                : "The server answered HTTP \(code)"
        }
    }

    private struct Track {
        let name: String
        let media: HLS.Media
    }

    private func tracks() async throws -> [Track] {
        let start = record.url
        let text = String(decoding: try await fetch(start), as: UTF8.self)
        switch try HLS.parse(text, base: start) {
        case .media(let m):
            return [Track(name: "video", media: m)]
        case .master(let variants, let audio):
            guard let best = HLS.best(variants) else { throw HLS.Failure.empty }
            var out: [Track] = []
            guard case .media(let video) = try HLS.parse(String(decoding: try await fetch(best.url), as: UTF8.self), base: best.url) else {
                throw HLS.Failure.notAPlaylist
            }
            out.append(Track(name: "video", media: video))
            if let g = best.audioGroup, let a = audio[g],
               case .media(let sound) = try HLS.parse(String(decoding: try await fetch(a), as: UTF8.self), base: a) {
                out.append(Track(name: "audio", media: sound))
            }
            return out
        }
    }

    private func run() async {
        change { $0.status = .probing; $0.error = nil; $0.isStream = true }
        notify(force: true)
        do {
            let list = try await tracks()
            if let p = list.first(where: { $0.media.encryption != "NONE" }) { throw HLS.Failure.protected(p.media.encryption) }
            try FileManager.default.createDirectory(at: partsFolder, withIntermediateDirectories: true)
            let total = list.reduce(0) { $0 + $1.media.segments.count + ($1.media.initSegment == nil ? 0 : 1) }
            change { $0.status = .downloading; $0.streamTotal = total; $0.streamDone = 0; $0.streamBytes = 0 }
            notify(force: true)
            var joined: [(Track, URL)] = []
            for t in list {
                var pieces = t.media.segments
                if let i = t.media.initSegment { pieces.insert(i, at: 0) }
                try await download(pieces, track: t.name)
                joined.append((t, try join(pieces.count, track: t.name)))
            }
            try finish(joined)
        } catch {
            if Task.isCancelled || error is CancellationError { return }
            change { $0.status = .failed; $0.error = error.localizedDescription }
            notify(force: true)
        }
    }

    private func piece(_ track: String, _ i: Int) -> URL { partsFolder.appendingPathComponent("\(track)-\(i).seg") }

    private func download(_ pieces: [URL], track: String) async throws {
        let width = connections
        try await withThrowingTaskGroup(of: Void.self) { group in
            var next = 0
            while next < min(width, pieces.count) {
                let i = next
                group.addTask { try await self.get(pieces[i], to: self.piece(track, i)) }
                next += 1
            }
            while try await group.next() != nil {
                if next < pieces.count {
                    let i = next
                    group.addTask { try await self.get(pieces[i], to: self.piece(track, i)) }
                    next += 1
                }
            }
        }
    }

    private func get(_ url: URL, to file: URL) async throws {
        if let size = try? FileManager.default.attributesOfItem(atPath: file.path)[.size] as? Int64 {
            count(size)  // already downloaded before a pause
            return
        }
        let data = try await fetch(url)
        let tmp = file.appendingPathExtension("tmp")
        try data.write(to: tmp)
        try FileManager.default.moveItem(at: tmp, to: file)
        count(Int64(data.count))
        let wait = max(limiter.consume(data.count), ownLimiter.consume(data.count))
        if wait > 0 { try await Task.sleep(nanoseconds: UInt64(wait * 1_000_000_000)) }
    }

    private func count(_ bytes: Int64) {
        change {
            $0.streamDone = ($0.streamDone ?? 0) + 1
            $0.streamBytes = ($0.streamBytes ?? 0) + bytes
        }
        notify()
    }

    private func join(_ n: Int, track: String) throws -> URL {
        let out = partsFolder.appendingPathComponent("\(track).joined")
        FileManager.default.createFile(atPath: out.path, contents: nil)
        let h = try FileHandle(forWritingTo: out)
        defer { try? h.close() }
        for i in 0..<n { try h.write(contentsOf: try Data(contentsOf: piece(track, i))) }
        return out
    }

    private func finish(_ joined: [(Track, URL)]) throws {
        let r = record
        var folder = r.folder
        if sortIntoCategories { folder.appendPathComponent(FileCategory.video.rawValue, isDirectory: true) }
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let base = (r.fileName as NSString).deletingPathExtension
        func ext(_ t: Track) -> String {
            if t.media.isFragmentedMP4 { return t.name == "audio" ? "m4a" : "mp4" }
            let e = t.media.segments.first?.pathExtension.lowercased() ?? ""
            return t.name == "audio" && e == "aac" ? "aac" : "ts"
        }
        var saved: URL
        let mp4 = FileNames.unique(base + ".mp4", in: folder)
        if (joined.count > 1 || ext(joined[0].0) != "mp4"), FFmpeg.mux(joined.map(\.1), to: mp4) {
            saved = mp4
        } else {
            saved = FileNames.unique(base + "." + ext(joined[0].0), in: folder)
            try FileManager.default.moveItem(at: joined[0].1, to: saved)
            if joined.count > 1 {
                let audio = FileNames.unique(base + " (sound)." + ext(joined[1].0), in: folder)
                try FileManager.default.moveItem(at: joined[1].1, to: audio)
                change { $0.error = "Picture and sound were saved as two files." }
            }
        }
        try? FileManager.default.removeItem(at: partsFolder)
        let size = (try? FileManager.default.attributesOfItem(atPath: saved.path)[.size] as? Int64) ?? nil
        change {
            $0.savedPath = saved.path
            $0.fileName = saved.lastPathComponent
            $0.size = size
            $0.status = .finished
            $0.finished = Date()
        }
        notify(force: true)
    }
}

/// Removes cookies and the login from a redirect that leaves the original server (or steps down to plain http).
final class RedirectGuard: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    let origin: URL
    init(origin: URL) { self.origin = origin }

    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        var next = request
        if let url = next.url, !Network.sameSite(url, origin) {
            next.setValue(nil, forHTTPHeaderField: "Cookie")
            next.setValue(nil, forHTTPHeaderField: "Authorization")
        }
        completionHandler(next)
    }
}

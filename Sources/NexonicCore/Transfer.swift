import CFNetwork
import Foundation

/// One download: probes the server, splits the file into byte ranges, downloads them in parallel into part files,
/// splits the largest remaining range whenever a connection frees up, retries failed ranges and joins the parts.
/// All state changes happen on `queue`; `onChange` is called with a copy of the record after each change.
public final class Transfer: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    public private(set) var record: DownloadRecord
    public var onChange: (@Sendable (DownloadRecord) -> Void)?

    private let connections: Int
    private let limiter: SpeedLimiter
    /// This download's own limit, on top of the shared one (set any time; 0 = none).
    public let ownLimiter = SpeedLimiter()
    private let proxy: ProxySettings
    private let sortIntoCategories: Bool
    private let queue: DispatchQueue
    private var session: URLSession!
    private var running: [Int: Running] = [:]  // by task identifier
    private var attempts: [Int: Int] = [:]  // by segment id
    private var stopping = false
    private var probeTask: Int?
    private var lastNotify = Date.distantPast

    private struct Running {
        let task: URLSessionDataTask
        let segmentID: Int
        let handle: FileHandle
        let from: Int64  // first byte asked for (checked against the answer's Content-Range)
        let writtenAtStart: Int64
    }

    private var partsFolder: URL { AppPaths.parts.appendingPathComponent(record.id.uuidString, isDirectory: true) }

    private let options: TransferOptions

    public init(record: DownloadRecord, connections: Int, limiter: SpeedLimiter, sortIntoCategories: Bool = false,
                proxy: ProxySettings = ProxySettings(), options: TransferOptions = TransferOptions()) {
        self.options = options
        self.record = record
        self.proxy = proxy
        self.connections = max(1, min(32, connections))
        self.limiter = limiter
        self.sortIntoCategories = sortIntoCategories
        queue = DispatchQueue(label: "nexonic.transfer.\(record.id.uuidString)")
        super.init()
        ownLimiter.setLimit(bytesPerSecond: (record.speedLimitKBps ?? 0) * 1024)
        let config = Network.configuration(proxy: proxy)
        config.httpMaximumConnectionsPerHost = 32
        config.timeoutIntervalForRequest = TimeInterval(options.readTimeoutSeconds)  // a silent connection is retried
        config.httpShouldSetCookies = false
        config.httpCookieAcceptPolicy = .never
        config.requestCachePolicy = .reloadIgnoringLocalCacheData
        let delegateQueue = OperationQueue()
        delegateQueue.maxConcurrentOperationCount = 1
        delegateQueue.underlyingQueue = queue
        session = URLSession(configuration: config, delegate: self, delegateQueue: delegateQueue)
    }

    // MARK: control

    public func start() {
        queue.async { self.begin() }
    }

    public func pause() {
        queue.async {
            guard self.record.status.isActive || self.record.status == .queued else { return }
            self.stopping = true
            self.record.status = .paused
            self.cancelAll()
            self.notify(force: true)
        }
    }

    /// Stops and removes part files. The finished file (if any) is kept.
    public func discard() {
        queue.async {
            self.stopping = true
            self.cancelAll()
            self.session.invalidateAndCancel()
            try? FileManager.default.removeItem(at: self.partsFolder)
        }
    }

    // MARK: start / resume

    private func request(range: String?, ifRange: String? = nil) -> URLRequest {
        var req = URLRequest(url: record.url)
        req.setValue("identity", forHTTPHeaderField: "Accept-Encoding")
        if let range { req.setValue(range, forHTTPHeaderField: "Range") }
        // With If-Range the server sends the whole (new) file instead of a part of it if the file changed.
        if range != nil, let ifRange, !ifRange.isEmpty { req.setValue(ifRange, forHTTPHeaderField: "If-Range") }
        if let ref = record.referrer, !ref.isEmpty { req.setValue(ref, forHTTPHeaderField: "Referer") }
        if let ua = record.userAgent, !ua.isEmpty { req.setValue(ua, forHTTPHeaderField: "User-Agent") }
        if let cookies = record.cookies, !cookies.isEmpty { req.setValue(cookies, forHTTPHeaderField: "Cookie") }
        return req
    }

    private func begin() {
        stopping = false
        record.status = .probing
        record.error = nil
        notify(force: true)
        // The probe is stopped as soon as the headers arrive, so a server that ignores ranges does not send the file.
        let task = session.dataTask(with: request(range: "bytes=0-0"))
        probeTask = task.taskIdentifier
        task.resume()
    }

    private func probed(_ response: HTTPURLResponse?, error: Error?) {
        guard !stopping else { return }
        guard let response, error == nil else {
            return fail(error?.localizedDescription ?? "No response from the server")
        }
        if response.statusCode == 401 { return fail("The server asks for a user name and password (use Properties → Login)") }
        if response.statusCode == 407 { return fail("The proxy asks for a user name and password (Settings → Connection)") }
        if [403, 404, 410].contains(response.statusCode), !record.segments.isEmpty {
            return fail("The link has expired (HTTP \(response.statusCode)). Use Refresh Link to continue from where it stopped")
        }
        guard (200..<300).contains(response.statusCode) else { return fail("The server answered HTTP \(response.statusCode)") }
        var size: Int64?
        var ranges = false
        if response.statusCode == 206, let cr = response.value(forHTTPHeaderField: "Content-Range"),
           let total = cr.split(separator: "/").last, let n = Int64(total) {
            size = n
            ranges = true
        } else if response.expectedContentLength > 0 {
            size = response.expectedContentLength
        }
        if let name = FileNames.fromContentDisposition(response.value(forHTTPHeaderField: "Content-Disposition")),
           record.fileName == FileNames.fromURL(record.url) {
            record.fileName = name
        }
        let validator = Network.validator(response)
        // Resume only into the same file: same size and, when the server names its version, the same ETag/Last-Modified.
        let canResume = !record.segments.isEmpty && ranges && record.acceptsRanges && size != nil && size == record.size
            && Network.sameVersion(record.validator, validator)
        if canResume {
            for i in record.segments.indices {
                let file = partURL(record.segments[i].id)
                let onDisk = (try? FileManager.default.attributesOfItem(atPath: file.path)[.size] as? Int64) ?? 0
                let len = record.segments[i].length ?? onDisk
                record.segments[i].written = min(onDisk, len)
                truncate(file, to: record.segments[i].written)
            }
        } else {
            try? FileManager.default.removeItem(at: partsFolder)
            record.size = size
            record.acceptsRanges = ranges
            record.validator = validator
            record.segments = SegmentPlanner.plan(size: ranges ? size : nil, connections: ranges ? connections : 1, minSegment: options.minSegment)
            if !ranges, let size { record.segments = [Segment(id: 0, start: 0, end: size - 1)] }
        }
        try? FileManager.default.createDirectory(at: partsFolder, withIntermediateDirectories: true)
        record.status = .downloading
        notify(force: true)
        for seg in record.segments where !seg.isComplete { launch(seg.id) }
        finishIfDone()
    }

    private func partURL(_ id: Int) -> URL { partsFolder.appendingPathComponent("\(id).part") }

    private func truncate(_ url: URL, to length: Int64) {
        if !FileManager.default.fileExists(atPath: url.path) { FileManager.default.createFile(atPath: url.path, contents: nil) }
        if let h = try? FileHandle(forWritingTo: url) {
            try? h.truncate(atOffset: UInt64(length))
            try? h.close()
        }
    }

    private func launch(_ segmentID: Int) {
        guard !stopping, let i = record.segments.firstIndex(where: { $0.id == segmentID }) else { return }
        let seg = record.segments[i]
        let file = partURL(seg.id)
        if !record.acceptsRanges { record.segments[i].written = 0 }
        truncate(file, to: record.segments[i].written)
        guard let handle = try? FileHandle(forWritingTo: file) else { return fail("Cannot write to \(file.path)") }
        _ = try? handle.seekToEnd()
        var range: String?
        let from = seg.start + record.segments[i].written
        if record.acceptsRanges {
            range = seg.end >= 0 ? "bytes=\(from)-\(seg.end)" : "bytes=\(from)-"
        }
        let task = session.dataTask(with: request(range: range, ifRange: record.validator))
        running[task.taskIdentifier] = Running(task: task, segmentID: seg.id, handle: handle, from: from,
                                               writtenAtStart: record.segments[i].written)
        task.resume()
    }

    private func cancelAll() {
        for r in running.values {
            r.task.cancel()
            try? r.handle.close()
        }
        running.removeAll()
    }

    // MARK: URLSession delegate (runs on `queue`)

    public func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                           newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        var next = request
        if let url = next.url, !Network.sameSite(url, record.url) {
            next.setValue(nil, forHTTPHeaderField: "Cookie")  // cookies only go to the original host (and never to plain http)
            next.setValue(nil, forHTTPHeaderField: "Authorization")
        }
        completionHandler(next)
    }

    public func urlSession(_ session: URLSession, task: URLSessionTask, didReceive challenge: URLAuthenticationChallenge,
                           completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        let space = challenge.protectionSpace
        let methods = [NSURLAuthenticationMethodHTTPBasic, NSURLAuthenticationMethodHTTPDigest, NSURLAuthenticationMethodNTLM,
                       NSURLAuthenticationMethodDefault]
        guard challenge.previousFailureCount == 0, methods.contains(space.authenticationMethod) else {
            return completionHandler(.performDefaultHandling, nil)
        }
        if space.isProxy() {
            guard !proxy.username.isEmpty else { return completionHandler(.performDefaultHandling, nil) }
            return completionHandler(.useCredential, URLCredential(user: proxy.username, password: proxy.password, persistence: .forSession))
        }
        // The login is only ever given to the download's own server (same host, port and https), never after a redirect.
        if let user = record.username, !user.isEmpty, Network.sameSpace(space, record.url) {
            return completionHandler(.useCredential, URLCredential(user: user, password: record.password ?? "", persistence: .forSession))
        }
        completionHandler(.performDefaultHandling, nil)
    }

    public func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
                           completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        if dataTask.taskIdentifier == probeTask {
            probeTask = nil
            completionHandler(.cancel)
            probed(response as? HTTPURLResponse, error: nil)
            return
        }
        let http = response as? HTTPURLResponse
        let code = http?.statusCode ?? 0
        let wantsRange = record.acceptsRanges
        if code == 200, wantsRange, let v = record.validator, !v.isEmpty {
            // If-Range said the file changed: never write the new file's bytes after the old one's.
            completionHandler(.cancel)
            return fileChanged()
        }
        if code == 206, let run = running[dataTask.taskIdentifier],
           !Network.rangeMatches(http?.value(forHTTPHeaderField: "Content-Range"), from: run.from, size: record.size) {
            completionHandler(.cancel)  // a different part than asked for: retried, never written
            return
        }
        if code == 206 || (code == 200 && !wantsRange) {
            completionHandler(.allow)
        } else {
            completionHandler(.cancel)
        }
    }

    public func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        guard let run = running[dataTask.taskIdentifier], let i = record.segments.firstIndex(where: { $0.id == run.segmentID }) else {
            return
        }
        var chunk = data
        let left = record.segments[i].remaining
        if Int64(chunk.count) > left { chunk = chunk.prefix(Int(left)) }
        if !chunk.isEmpty {
            do { try run.handle.write(contentsOf: chunk) } catch {
                return fail("Writing failed: \(error.localizedDescription)")
            }
            record.segments[i].written += Int64(chunk.count)
        }
        if record.segments[i].isComplete {
            dataTask.cancel()  // the range may have been shortened by a split
        } else {
            let wait = max(limiter.consume(chunk.count), ownLimiter.consume(chunk.count))
            // Holding this (serial) delegate queue makes the connection stop reading, which is what limits the speed.
            if wait > 0 { Thread.sleep(forTimeInterval: wait) }
        }
        notify()
    }

    public func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if task.taskIdentifier == probeTask {
            probeTask = nil
            if !stopping { probed(nil, error: error) }
            return
        }
        guard let run = running.removeValue(forKey: task.taskIdentifier) else { return }
        try? run.handle.close()
        guard !stopping, let i = record.segments.firstIndex(where: { $0.id == run.segmentID }) else { return }
        var seg = record.segments[i]
        if seg.end < 0, error == nil {  // unknown size: the stream ended normally
            seg.end = seg.start + seg.written - 1
            record.segments[i] = seg
            record.size = seg.written
        }
        if record.segments[i].isComplete {
            splitAndLaunch()
            finishIfDone()
            return
        }
        // Progress that is kept (ranges) earns fresh retries; without ranges every retry starts over, so it doesn't.
        if record.acceptsRanges, record.segments[i].written > run.writtenAtStart { attempts[seg.id] = 0 }
        let n = (attempts[seg.id] ?? 0) + 1
        attempts[seg.id] = n
        guard n <= options.retries else {
            return fail(error?.localizedDescription ?? "A part of the file could not be downloaded")
        }
        let delay = min(30, pow(2, Double(n - 1)))
        queue.asyncAfter(deadline: .now() + delay) { [weak self] in self?.launch(seg.id) }
    }

    private func splitAndLaunch() {
        guard record.acceptsRanges, running.count < connections else { return }
        let nextID = (record.segments.map(\.id).max() ?? 0) + 1
        guard let split = SegmentPlanner.splitLargest(record.segments, nextID: nextID, minSegment: options.minSegment) else { return }
        record.segments[split.index].end = split.newEnd
        record.segments.append(split.added)
        launch(split.added.id)
    }

    // MARK: finish

    private func finishIfDone() {
        guard !stopping, running.isEmpty, record.status == .downloading,
              !record.segments.isEmpty, record.segments.allSatisfy(\.isComplete) else { return }
        do {
            var folder = record.folder
            if sortIntoCategories, let sub = record.category.folder { folder.appendPathComponent(sub, isDirectory: true) }
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let target = FileNames.unique(record.fileName, in: folder)
            FileManager.default.createFile(atPath: target.path, contents: nil)
            let out = try FileHandle(forWritingTo: target)
            defer { try? out.close() }
            for seg in record.segments.sorted(by: { $0.start < $1.start }) {
                let input = try FileHandle(forReadingFrom: partURL(seg.id))
                while let block = try input.read(upToCount: 4 << 20), !block.isEmpty { try out.write(contentsOf: block) }
                try input.close()
            }
            let written = try out.offset()
            if let size = record.size, Int64(written) != size {
                return fail("Size check failed: expected \(size) bytes, got \(written)")
            }
            try? FileManager.default.removeItem(at: partsFolder)
            if let expected = record.checksum, let (algorithm, hex) = Checksum.parse(expected) {
                let actual = (try? Checksum.compute(algorithm, file: target)) ?? ""
                record.checksumResult = actual == hex ? "\(algorithm.rawValue) matches" : "\(algorithm.rawValue) does not match (got \(actual))"
                if actual != hex { record.error = "The checksum does not match: the file may be damaged or not the one expected" }
            }
            record.savedPath = target.path
            record.fileName = target.lastPathComponent
            record.status = .finished
            record.finished = Date()
            notify(force: true)
            session.finishTasksAndInvalidate()
        } catch {
            fail("Joining the parts failed: \(error.localizedDescription)")
        }
    }

    /// The file on the server is not the one this download started with: start again from the beginning next time.
    private func fileChanged() {
        guard !stopping else { return }
        fail("The file changed on the server since this download started. Resume to download the new version from the start")
        record.segments = []
        record.validator = nil
        try? FileManager.default.removeItem(at: partsFolder)
        notify(force: true)
    }

    private func fail(_ message: String) {
        stopping = true
        cancelAll()
        record.status = .failed
        record.error = message
        notify(force: true)
    }

    private func notify(force: Bool = false) {
        let now = Date()
        guard force || now.timeIntervalSince(lastNotify) > 0.25 else { return }
        lastNotify = now
        onChange?(record)
    }
}

public enum Network {
    /// Same server and no step down from https: cookies and logins may be sent there.
    public static func sameSite(_ url: URL, _ origin: URL) -> Bool {
        func port(_ u: URL) -> Int { u.port ?? (u.scheme?.lowercased() == "https" ? 443 : 80) }
        let downgrade = origin.scheme?.lowercased() == "https" && url.scheme?.lowercased() != "https"
        return url.host?.lowercased() == origin.host?.lowercased() && port(url) == port(origin) && !downgrade
    }

    /// A login challenge from the download's own server: same host and port, and https if the download is https.
    public static func sameSpace(_ space: URLProtectionSpace, _ origin: URL) -> Bool {
        let scheme = origin.scheme?.lowercased() ?? ""
        let port = origin.port ?? (scheme == "https" ? 443 : 80)
        let proto = space.protocol?.lowercased() ?? ""
        return space.host.lowercased() == origin.host?.lowercased() && space.port == port
            && !(scheme == "https" && proto != "https")
    }

    /// The file's version as the server names it: a strong ETag, else Last-Modified (nil if neither).
    public static func validator(_ response: HTTPURLResponse) -> String? {
        if let tag = response.value(forHTTPHeaderField: "ETag"), !tag.isEmpty, !tag.hasPrefix("W/") { return tag }
        if let lm = response.value(forHTTPHeaderField: "Last-Modified"), !lm.isEmpty { return lm }
        return nil
    }

    /// True unless both versions are known and differ (older lists have none: the size decides, as before).
    public static func sameVersion(_ saved: String?, _ now: String?) -> Bool { saved == nil || now == nil || saved == now }

    /// A 206 answer must start where we asked and belong to a file of the expected size ("bytes 5-9/10").
    public static func rangeMatches(_ header: String?, from: Int64, size: Int64?) -> Bool {
        guard let header, let m = header.range(of: #"bytes\s+(\d+)-(\d+)/(\d+|\*)"#, options: .regularExpression) else { return false }
        let parts = header[m].dropFirst(6).split(whereSeparator: { $0 == "-" || $0 == "/" }).map { String($0).trimmingCharacters(in: .whitespaces) }
        guard parts.count == 3, let start = Int64(parts[0]), start == from else { return false }
        if let size, let total = Int64(parts[2]) { return total == size }
        return true
    }

    /// Session settings with the chosen proxy.
    public static func configuration(proxy: ProxySettings) -> URLSessionConfiguration {
        let config = URLSessionConfiguration.default
        let host = proxy.host.trimmingCharacters(in: .whitespaces)
        switch proxy.mode {
        case .system:
            break
        case .none:
            config.connectionProxyDictionary = [:]
        case .http where !host.isEmpty:
            config.connectionProxyDictionary = [
                kCFNetworkProxiesHTTPEnable as String: 1, kCFNetworkProxiesHTTPProxy as String: host,
                kCFNetworkProxiesHTTPPort as String: proxy.port,
                kCFNetworkProxiesHTTPSEnable as String: 1, kCFNetworkProxiesHTTPSProxy as String: host,
                kCFNetworkProxiesHTTPSPort as String: proxy.port,
            ]
        case .socks where !host.isEmpty:
            var d: [AnyHashable: Any] = [
                kCFNetworkProxiesSOCKSEnable as String: 1, kCFNetworkProxiesSOCKSProxy as String: host,
                kCFNetworkProxiesSOCKSPort as String: proxy.port,
            ]
            if !proxy.username.isEmpty {
                d[kCFStreamPropertySOCKSUser as String] = proxy.username
                d[kCFStreamPropertySOCKSPassword as String] = proxy.password
            }
            config.connectionProxyDictionary = d
        default:
            break
        }
        return config
    }
}

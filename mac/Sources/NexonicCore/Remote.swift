import Foundation
import Network
import Security

/// Options → Remote: manage downloads from a phone or another device on this network. Off by default. The pairing key
/// is kept in the Keychain with the other secrets (see Secrets); a new key unpairs every device.
public struct RemoteSettings: Codable, Equatable, Sendable {
    public var enabled = false
    public var port = 47813
    public var key = ""
    public init() {}

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = RemoteSettings()
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? d.enabled
        port = try c.decodeIfPresent(Int.self, forKey: .port) ?? d.port
        key = try c.decodeIfPresent(String.self, forKey: .key) ?? d.key
    }
}

/// What the phone may do with the app's downloads (the same /v1 API as Windows and Linux). Every call runs on the main thread.
@MainActor
public protocol RemoteControl: AnyObject {
    func remoteDownloads() -> [DownloadRecord]
    func remoteQueues() -> [DownloadQueue]
    /// Adds web links; returns the new downloads' ids.
    func remoteAdd(_ urls: [URL], queue: String?, start: Bool) -> [UUID]
    func remotePause(_ id: UUID) -> Bool
    func remoteResume(_ id: UUID) -> Bool
    /// Removes it from the list; the file stays on the computer.
    func remoteRemove(_ id: UUID) -> Bool
    func remoteStartQueue(_ id: String) -> Bool
    func remoteStopQueue(_ id: String) -> Bool
}

/// The remote's HTTP handling, kept apart from the network so it can be tested: reading a request, checking the pairing
/// key, the API's endpoints and the answer.
public enum RemoteHTTP {
    public static let maxHeader = 16 * 1024
    public static let maxBody = 64 * 1024
    public static let maxLinks = 200

    public struct Request: Equatable {
        public var method: String
        public var path: String
        /// Header names in lower case.
        public var headers: [String: String]
        public var body: Data
    }

    public enum Parsed: Equatable { case incomplete, bad, request(Request) }

    /// One HTTP/1.1 request from what has arrived so far (headers up to 16 KB, a body up to 64 KB).
    public static func parse(_ data: Data) -> Parsed {
        guard let end = data.range(of: Data("\r\n\r\n".utf8)) else { return data.count >= maxHeader ? .bad : .incomplete }
        guard end.lowerBound <= maxHeader, let head = String(data: data[data.startIndex..<end.lowerBound], encoding: .ascii) else { return .bad }
        let lines = head.components(separatedBy: "\r\n")
        let first = lines[0].split(separator: " ", omittingEmptySubsequences: false)
        guard first.count == 3, first[2].hasPrefix("HTTP/1."), first[1].hasPrefix("/") else { return .bad }
        var headers: [String: String] = [:]
        for line in lines.dropFirst() {
            guard let colon = line.firstIndex(of: ":"), colon > line.startIndex else { return .bad }
            headers[line[..<colon].trimmingCharacters(in: .whitespaces).lowercased()] = line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
        }
        if headers["transfer-encoding"] != nil { return .bad }
        let length = headers["content-length"].map { Int($0) ?? -1 } ?? 0
        guard length >= 0, length <= maxBody else { return .bad }
        let start = end.upperBound
        guard data.count - (start - data.startIndex) >= length else { return .incomplete }
        return .request(Request(method: first[0].uppercased(), path: String(first[1]), headers: headers,
                                body: data.subdata(in: start..<(start + length))))
    }

    /// Nil when the API call may go on. Pages of other sites are refused (their Origin differs from the Host).
    public static func check(_ headers: [String: String], key: String) -> (status: Int, message: String)? {
        let host = headers["host"] ?? ""
        if let origin = headers["origin"], origin.lowercased() != "http://" + host.lowercased() {
            return (403, "Requests from other web pages are not allowed")
        }
        let auth = headers["authorization"] ?? ""
        let given = auth.hasPrefix("Bearer ") ? String(auth.dropFirst(7)).trimmingCharacters(in: .whitespaces) : ""
        guard key.count >= 16, same(given, key) else { return (401, "This device is not paired (scan the QR code in the app again)") }
        return nil
    }

    /// Compares without stopping at the first difference, so the time taken says nothing about the key.
    static func same(_ a: String, _ b: String) -> Bool {
        let x = Array(a.utf8), y = Array(b.utf8)
        var diff = x.count ^ y.count
        for i in 0..<max(x.count, y.count) { diff |= Int((i < x.count ? x[i] : 0) ^ (i < y.count ? y[i] : 0)) }
        return diff == 0
    }

    /// A new pairing key: 32 random bytes, URL-safe.
    public static func newKey() -> String {
        var bytes = [UInt8](repeating: 0, count: 32)
        if SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) != errSecSuccess {
            for i in bytes.indices { bytes[i] = UInt8.random(in: 0...255) }
        }
        return Data(bytes).base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    /// The address a phone opens (scanned from the QR code): the key is in the #fragment, which browsers never send.
    public static func pairingURL(host: String, port: Int, key: String) -> String {
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-_.~"))
        return "http://\(host):\(port)/#k=\(key.addingPercentEncoding(withAllowedCharacters: allowed) ?? key)"
    }

    /// A download as the API shows it (never cookies, passwords or the browser's name).
    public static func describe(_ r: DownloadRecord, queueName: (String) -> String) -> [String: Any] {
        let iso = ISO8601DateFormatter()
        var d: [String: Any] = [
            "id": r.id.uuidString.lowercased(), "url": r.url.absoluteString, "fileName": r.fileName, "folder": r.folder.path,
            "status": r.status.rawValue, "received": r.received, "progress": (r.progress * 1000).rounded() / 10,
            "queue": queueName(r.queue), "queueId": r.queue, "added": iso.string(from: r.added),
        ]
        d["size"] = r.size ?? NSNull()
        d["finished"] = r.finished.map { iso.string(from: $0) } ?? NSNull()
        d["savedPath"] = r.savedPath ?? NSNull()
        d["error"] = r.error ?? NSNull()
        return d
    }

    static func error(_ message: String) -> [String: Any] { ["error": message] }

    /// The API's endpoints, as on Windows and Linux.
    @MainActor
    public static func route(_ method: String, _ rawPath: String, _ body: Data, _ c: RemoteControl) -> (Int, Any) {
        let path = rawPath.split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false).first.map(String.init) ?? ""
        let parts = path.split(separator: "/").map(String.init)
        guard parts.count >= 2, parts[0] == "v1" else { return (404, error("Unknown address")) }
        let id = parts.count >= 3 ? UUID(uuidString: parts[2]) : nil
        let queueName: (String) -> String = { q in c.remoteQueues().first { $0.id == q }?.name ?? "Main queue" }
        switch (parts[1], parts.count, method) {
        case ("status", 2, "GET"):
            let all = c.remoteDownloads()
            let body: [String: Any] = ["app": "Nexonic Download Manager", "active": all.filter { $0.status.isActive }.count,
                                       "queued": all.filter { $0.status == .queued }.count, "total": all.count]
            return (200, body)
        case ("downloads", 2, "GET"):
            return (200, c.remoteDownloads().map { describe($0, queueName: queueName) })
        case ("downloads", 2, "POST"):
            return add(body, c)
        case ("downloads", 3, "GET"):
            guard let id, let one = c.remoteDownloads().first(where: { $0.id == id }) else { return (404, error("No such download")) }
            return (200, describe(one, queueName: queueName))
        case ("downloads", 3, "DELETE"):
            guard let id, c.remoteRemove(id) else { return (404, error("No such download")) }
            return (200, ["removed": id.uuidString.lowercased()])
        case ("downloads", 4, "POST") where parts[3] == "pause" || parts[3] == "resume":
            guard let id, parts[3] == "pause" ? c.remotePause(id) : c.remoteResume(id),
                  let one = c.remoteDownloads().first(where: { $0.id == id }) else { return (404, error("No such download")) }
            return (200, describe(one, queueName: queueName))
        case ("queues", 2, "GET"):
            return (200, c.remoteQueues().map { ["id": $0.id, "name": $0.name, "running": $0.running, "maxActive": $0.maxActive] as [String: Any] })
        case ("queues", 4, "POST") where parts[3] == "start" || parts[3] == "stop":
            let done = parts[3] == "start" ? c.remoteStartQueue(parts[2]) : c.remoteStopQueue(parts[2])
            return done ? (200, ["id": parts[2], "running": parts[3] == "start"] as [String: Any]) : (404, error("No such queue"))
        case ("status", _, _), ("downloads", _, _), ("queues", _, _):
            return (405, error("This method is not allowed here"))
        default:
            return (404, error("Unknown address"))
        }
    }

    @MainActor
    static func add(_ body: Data, _ c: RemoteControl) -> (Int, Any) {
        guard let o = (try? JSONSerialization.jsonObject(with: body)) as? [String: Any] else { return (400, error("The body must be a JSON object")) }
        var links: [String] = []
        if let one = o["url"] as? String { links.append(one) }
        if let many = o["urls"] as? [Any] { links += many.map { $0 as? String ?? "" } }
        if links.isEmpty { return (400, error("Give url or urls")) }
        if links.count > maxLinks { return (413, error("At most \(maxLinks) links per request")) }
        var urls: [URL] = []
        for link in links {
            let t = link.trimmingCharacters(in: .whitespacesAndNewlines)
            guard t.count <= 4096, let u = URL(string: t), ["http", "https"].contains(u.scheme?.lowercased() ?? ""), u.host?.isEmpty == false
            else { return (400, error("Only http and https links can be added")) }
            urls.append(u)
        }
        let start = o["start"] as? Bool ?? true
        let ids = c.remoteAdd(urls, queue: o["queue"] as? String, start: start)
        return (201, ["ids": ids.map { $0.uuidString.lowercased() }])
    }

    /// The whole answer, with the headers that keep the page safe in a browser.
    public static func response(_ status: Int, body: Data, type: String) -> Data {
        let reason = status < 300 ? "OK" : "Error"
        let head = "HTTP/1.1 \(status) \(reason)\r\nContent-Type: \(type)\r\nContent-Length: \(body.count)\r\nCache-Control: no-store\r\n"
            + "X-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nX-Frame-Options: DENY\r\n"
            + "Content-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\r\n"
            + "Connection: close\r\n\r\n"
        return Data(head.utf8) + body
    }

    public static func json(_ value: Any) -> Data {
        (try? JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])) ?? Data("{}".utf8)
    }

    /// The answer to a request, given the pairing key. Runs on the main thread.
    @MainActor
    public static func answer(_ req: Request, key: String, control: RemoteControl, blocked: Bool, failed: () -> Void) -> Data {
        let path = req.path.split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false).first.map(String.init) ?? ""
        let type = "application/json; charset=utf-8"
        if req.method == "GET", path == "/" || path == "/index.html" {
            return response(200, body: Data(RemotePage.html.utf8), type: "text/html; charset=utf-8")
        }
        guard path.hasPrefix("/v1/") else { return response(404, body: json(error("Not found")), type: type) }
        if blocked { return response(429, body: json(error("Too many wrong keys; try again in a minute")), type: type) }
        if let refused = check(req.headers, key: key) {
            if refused.status == 401 { failed() }
            return response(refused.status, body: json(error(refused.message)), type: type)
        }
        let (status, body) = route(req.method, req.path, req.body, control)
        return response(status, body: json(body), type: type)
    }

    /// This computer's IPv4 addresses on the local network (private ones first), for the pairing address.
    public static func lanAddresses() -> [String] {
        var list: [String] = []
        var first: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&first) == 0 else { return [] }
        defer { freeifaddrs(first) }
        var p = first
        while let a = p {
            defer { p = a.pointee.ifa_next }
            let flags = Int32(a.pointee.ifa_flags)
            guard let sa = a.pointee.ifa_addr, sa.pointee.sa_family == UInt8(AF_INET),
                  flags & IFF_UP != 0, flags & IFF_LOOPBACK == 0 else { continue }
            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            if getnameinfo(sa, socklen_t(sa.pointee.sa_len), &host, socklen_t(host.count), nil, 0, NI_NUMERICHOST) == 0 {
                let s = String(cString: host)
                if !list.contains(s) { list.append(s) }
            }
        }
        return list.sorted { (isPrivate($0) ? 0 : 1) < (isPrivate($1) ? 0 : 1) }
    }

    public static func isPrivate(_ address: String) -> Bool {
        let b = address.split(separator: ".").compactMap { Int($0) }
        guard b.count == 4 else { return false }
        return b[0] == 10 || (b[0] == 172 && (16...31).contains(b[1])) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && (64...127).contains(b[1]))
    }
}

/// The remote server on the local network. Traffic is plain HTTP inside the network: Options says to use it only on
/// trusted networks (home or office), never on public Wi-Fi.
@MainActor
public final class RemoteServer {
    public static let maxConnections = 16
    public let port: Int
    public let key: String
    private weak var control: RemoteControl?
    private var listener: NWListener?
    private var open = 0
    // Wrong keys slow down: after 10 failures from one address, that address waits a minute.
    private var failures: [String: (count: Int, until: Date)] = [:]
    private let queue = DispatchQueue(label: "nexonic.remote")

    public init(port: Int, key: String, control: RemoteControl) {
        self.port = port
        self.key = key
        self.control = control
    }

    public func start() throws {
        guard let p = NWEndpoint.Port(rawValue: UInt16(clamping: port)), port > 0 else { throw NWError.posix(.EINVAL) }
        let l = try NWListener(using: .tcp, on: p)
        l.newConnectionHandler = { [weak self] c in Task { @MainActor in self?.accept(c) } }
        l.start(queue: queue)
        listener = l
    }

    public func stop() {
        listener?.cancel()
        listener = nil
    }

    private func address(_ c: NWConnection) -> String {
        if case .hostPort(let host, _) = c.endpoint { return "\(host)" }
        return "?"
    }

    private func accept(_ c: NWConnection) {
        guard open < Self.maxConnections else { c.cancel(); return }
        open += 1
        c.start(queue: queue)
        queue.asyncAfter(deadline: .now() + 15) { c.cancel() }  // slow or idle connections are closed
        receive(c, Data())
    }

    private func finish(_ c: NWConnection, _ answer: Data?) {
        open -= 1
        guard let answer else { c.cancel(); return }
        c.send(content: answer, completion: .contentProcessed { _ in c.cancel() })
    }

    private nonisolated func receive(_ c: NWConnection, _ sofar: Data) {
        c.receive(minimumIncompleteLength: 1, maximumLength: RemoteHTTP.maxHeader) { [weak self] data, _, done, failure in
            var all = sofar
            if let data { all.append(data) }
            Task { @MainActor in
                guard let self else { c.cancel(); return }
                switch RemoteHTTP.parse(all) {
                case .request(let req): self.serve(c, req)
                case .bad: self.finish(c, RemoteHTTP.response(400, body: RemoteHTTP.json(["error": "Bad request"]), type: "application/json; charset=utf-8"))
                case .incomplete:
                    if done || failure != nil || all.count > RemoteHTTP.maxHeader + RemoteHTTP.maxBody { self.finish(c, nil) } else { self.receive(c, all) }
                }
            }
        }
    }

    private func serve(_ c: NWConnection, _ req: RemoteHTTP.Request) {
        guard let control else { finish(c, nil); return }
        let from = address(c)
        let now = Date()
        let blocked = failures[from].map { $0.count >= 10 && $0.until > now } ?? false
        let answer = RemoteHTTP.answer(req, key: key, control: control, blocked: blocked) {
            let old = failures[from].flatMap { $0.until > now ? $0 : nil } ?? (count: 0, until: now)
            failures[from] = (old.count + 1, now.addingTimeInterval(60))
            if failures.count > 1000 { failures.removeAll() }
        }
        finish(c, answer)
    }
}

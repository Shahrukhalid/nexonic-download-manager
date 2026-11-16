import Foundation

/// Keeps finished files in sync with the server (the same rules as DownloadSync.cs on Windows and Linux). A file with a
/// sync interval is checked when it is due with one small request (the first byte, asking "only if changed"). It is
/// downloaded again only when the server reports a new version (a different ETag or Last-Modified, or a different size
/// when the server names no version), and the new copy replaces the old one only once it is complete.
public enum DownloadSync {
    /// The intervals offered in the menus, in hours.
    public static let choices = [1, 6, 24, 168]

    public enum Result: Equatable, Sendable { case unchanged, changed, failed }

    public static func describe(_ hours: Int) -> String {
        switch hours {
        case 1: return "Every Hour"
        case 24: return "Every Day"
        case 168: return "Every Week"
        default: return "Every \(hours) Hours"
        }
    }

    /// Torrents, site videos and streams are not kept in sync.
    public static func canSync(_ r: DownloadRecord) -> Bool {
        r.status == .finished && r.torrent == nil && r.siteVideo != true && r.isStream != true
    }

    /// A finished, synced file whose interval has passed (a clock set back also counts as due).
    public static func due(_ r: DownloadRecord, now: Date) -> Bool {
        guard let hours = r.syncHours, hours > 0, canSync(r) else { return false }
        guard let at = r.syncChecked else { return true }
        return now.timeIntervalSince(at) >= Double(hours) * 3600 || at > now
    }

    /// What the server's answer says about the file we have.
    public static func compare(_ r: DownloadRecord, status: Int, validator: String?, size: Int64?) -> Result {
        if status == 304 { return .unchanged }
        if status < 200 || status >= 300 { return .failed }
        if let saved = r.validator, let now = validator { return saved == now ? .unchanged : .changed }
        if let saved = r.size, saved > 0, let now = size, now > 0 { return saved == now ? .unchanged : .changed }
        return .unchanged
    }

    /// The whole size from a ranged answer ("bytes 0-0/12345"), else the body's length on a full answer.
    public static func sizeOf(status: Int, contentRange: String?, contentLength: String?) -> Int64? {
        if let range = contentRange, let slash = range.lastIndex(of: "/") {
            return Int64(range[range.index(after: slash)...].trimmingCharacters(in: .whitespaces))
        }
        return status == 200 ? contentLength.flatMap { Int64($0) } : nil
    }

    /// The check request: the first byte only, "only if changed", with the download's own details.
    public static func request(for r: DownloadRecord) -> URLRequest {
        var req = URLRequest(url: r.url, timeoutInterval: 60)
        req.setValue("bytes=0-0", forHTTPHeaderField: "Range")
        if let v = r.validator {
            req.setValue(v, forHTTPHeaderField: v.hasPrefix("\"") ? "If-None-Match" : "If-Modified-Since")
        }
        if let ua = r.userAgent, !ua.isEmpty { req.setValue(ua, forHTTPHeaderField: "User-Agent") }
        if let ref = r.referrer, !ref.isEmpty { req.setValue(ref, forHTTPHeaderField: "Referer") }
        if let c = r.cookies, !c.isEmpty { req.setValue(c, forHTTPHeaderField: "Cookie") }
        if let user = r.username, !user.isEmpty {
            let pair = Data("\(user):\(r.password ?? "")".utf8).base64EncodedString()
            req.setValue("Basic \(pair)", forHTTPHeaderField: "Authorization")
        }
        return req
    }

    /// Follows redirects only within the download's own host, so private details never reach another one.
    final class SameHostRedirects: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
        let host: String
        init(host: String) { self.host = host.lowercased() }
        func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                        newRequest request: URLRequest) async -> URLRequest? {
            (request.url?.host?.lowercased() ?? "") == host ? request : nil
        }
    }

    /// Asks the server whether the file changed. Problems count as failed (checked again at the next interval).
    public static func check(_ r: DownloadRecord, session: URLSession) async -> (result: Result, validator: String?, size: Int64?) {
        let scheme = r.url.scheme?.lowercased() ?? ""
        guard scheme == "http" || scheme == "https", let host = r.url.host else { return (.failed, nil, nil) }
        do {
            let guardDelegate = SameHostRedirects(host: host)
            let (bytes, response) = try await session.bytes(for: request(for: r), delegate: guardDelegate)
            bytes.task.cancel()  // only the answer's headers are needed, even when a server ignores the range
            guard let http = response as? HTTPURLResponse else { return (.failed, nil, nil) }
            let validator = Network.validator(http)
            let size = sizeOf(status: http.statusCode, contentRange: http.value(forHTTPHeaderField: "Content-Range"),
                              contentLength: http.value(forHTTPHeaderField: "Content-Length"))
            return (compare(r, status: http.statusCode, validator: validator, size: size), validator, size)
        } catch {
            return (.failed, nil, nil)
        }
    }

    /// Gets a changed file ready to download again: the old copy stays where it is until the new one is complete.
    public static func prepareRedownload(_ r: inout DownloadRecord) {
        r.syncReplace = r.savedPath
        r.status = .queued
        r.segments = []
        r.size = nil
        r.validator = nil
        r.workFile = nil
        r.finished = nil
        r.error = nil
        r.checksumResult = nil
    }

    /// After a download finishes: an updated synced file takes the old copy's place (true when it did).
    public static func finished(_ r: inout DownloadRecord, now: Date) -> Bool {
        if let hours = r.syncHours, hours > 0 { r.syncChecked = now }
        guard let old = r.syncReplace else { return false }
        r.syncReplace = nil
        guard let fresh = r.savedPath, fresh != old, FileManager.default.fileExists(atPath: fresh) else { return false }
        let freshURL = URL(fileURLWithPath: fresh)
        let oldURL = URL(fileURLWithPath: old)
        guard freshURL.deletingLastPathComponent().path == oldURL.deletingLastPathComponent().path else { return false }
        do {
            if FileManager.default.fileExists(atPath: old) {
                _ = try FileManager.default.replaceItemAt(oldURL, withItemAt: freshURL)
            } else {
                try FileManager.default.moveItem(at: freshURL, to: oldURL)
            }
            r.savedPath = old
            r.fileName = oldURL.lastPathComponent
            return true
        } catch {
            return false
        }
    }
}

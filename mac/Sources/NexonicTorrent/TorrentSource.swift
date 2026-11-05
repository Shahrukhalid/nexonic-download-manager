import Foundation

private final class MetadataRedirects: NSObject, URLSessionTaskDelegate {
    private var hops = 0
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        hops += 1
        guard hops <= 10, let url = request.url, ["http", "https"].contains(url.scheme?.lowercased() ?? ""), url.user == nil else { completionHandler(nil); return }
        var clean = request; clean.setValue(nil, forHTTPHeaderField: "Cookie"); clean.setValue(nil, forHTTPHeaderField: "Authorization")
        completionHandler(clean)
    }
}
public enum TorrentSource {
    public static let maximumBytes = 8 * 1024 * 1024
    public static func metadata(_ source: String) async throws -> Data {
        if let url = URL(string: source), ["http", "https"].contains(url.scheme?.lowercased() ?? "") {
            guard url.user == nil, url.password == nil else { throw TorrentFailure("Torrent links cannot contain website credentials.") }
            let config = URLSessionConfiguration.ephemeral
            config.httpShouldSetCookies = false; config.httpCookieStorage = nil; config.urlCredentialStorage = nil
            config.timeoutIntervalForRequest = 60; config.timeoutIntervalForResource = 90
            let session = URLSession(configuration: config, delegate: MetadataRedirects(), delegateQueue: nil)
            defer { session.invalidateAndCancel() }
            let (stream, response) = try await session.bytes(from: url)
            guard let response = response as? HTTPURLResponse, (200..<300).contains(response.statusCode), response.expectedContentLength <= maximumBytes else { throw TorrentFailure("Torrent metadata could not be fetched or exceeds 8 MiB.") }
            var data = Data()
            for try await byte in stream {
                try Task.checkCancellation()
                guard data.count < maximumBytes else { throw TorrentFailure("Torrent metadata exceeds 8 MiB.") }
                data.append(byte)
            }
            return data
        }
        guard source.hasPrefix("/"), source.lowercased().hasSuffix(".torrent") else { throw TorrentFailure("Choose a local .torrent file, HTTP(S) torrent link or magnet.") }
        let input = try FileHandle(forReadingFrom: URL(fileURLWithPath: source)); defer { try? input.close() }
        var data = Data()
        while let chunk = try input.read(upToCount: 81920), !chunk.isEmpty {
            try Task.checkCancellation()
            guard data.count + chunk.count <= maximumBytes else { throw TorrentFailure("Torrent metadata exceeds 8 MiB.") }
            data.append(chunk)
        }
        return data
    }
    public static func isTorrent(_ source: String) -> Bool {
        source.lowercased().hasPrefix("magnet:?") || (URL(string: source)?.path.lowercased().hasSuffix(".torrent") ?? source.lowercased().hasSuffix(".torrent"))
    }
    public static func preview(_ source: String, stateDirectory: URL) async throws -> TorrentSnapshot {
        let source = source.trimmingCharacters(in: .whitespacesAndNewlines)
        let magnet = source.lowercased().hasPrefix("magnet:?")
        let metainfo = magnet ? "" : try await metadata(source).base64EncodedString()
        let directory = stateDirectory.appendingPathComponent("preview-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: directory) }
        let engine = try NativeTorrentSession(stateDirectory: directory)
        defer { engine.close() }
        var snapshot = try TorrentSnapshot.decode(engine.call(["action": "add", "id": "preview", "preview": true, "source": magnet ? "magnet:?" + source.dropFirst(8) : source, "metainfo": metainfo]))
        let deadline = Date().addingTimeInterval(100)
        while snapshot.state != "preview" {
            try Task.checkCancellation()
            if snapshot.state == "failed" { throw TorrentFailure(snapshot.error) }
            guard Date() < deadline else { throw TorrentFailure("Torrent metadata lookup timed out.") }
            try await Task.sleep(nanoseconds: 250_000_000)
            snapshot = try TorrentSnapshot.decode(engine.call(["action": "status", "id": "preview"]))
        }
        return snapshot
    }
}

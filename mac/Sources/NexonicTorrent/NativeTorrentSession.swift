import CNexonicTorrent
import Foundation

public struct TorrentFailure: LocalizedError {
    public let message: String
    public var errorDescription: String? { message }
    public init(_ message: String) { self.message = message }
}

/// One native session with serialized calls and deterministic checkpoint flushing on close.
public final class NativeTorrentSession: @unchecked Sendable {
    private let lock = NSLock()
    private var context: UnsafeMutableRawPointer?
    public init(stateDirectory: URL, allowLocalPeers: Bool = false, dht: Bool = true) throws {
        guard stateDirectory.isFileURL else { throw TorrentFailure("Choose a local state directory.") }
        let path = stateDirectory.path.hasPrefix("/var/") ? "/private" + stateDirectory.path : stateDirectory.path
        let data = try JSONSerialization.data(withJSONObject: ["state_dir": path, "allow_local_peers": allowLocalPeers, "dht": dht])
        context = String(decoding: data, as: UTF8.self).withCString { ndm_torrent_create($0) }
        if context == nil {
            let result = ndm_torrent_call(nil, "{}")
            defer { ndm_torrent_free(result) }
            let message = result.flatMap { String(cString: $0).data(using: .utf8) }
                .flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] }?["error"] as? String
            throw TorrentFailure(message ?? "The native torrent engine could not start.")
        }
    }
    public func call(_ request: [String: Any]) throws -> [String: Any] {
        let encoded = try JSONSerialization.data(withJSONObject: request)
        lock.lock(); defer { lock.unlock() }
        guard let context else { throw TorrentFailure("The torrent session is closed.") }
        let pointer = String(decoding: encoded, as: UTF8.self).withCString { ndm_torrent_call(context, $0) }
        guard let pointer else { throw TorrentFailure("Native torrent response allocation failed.") }
        defer { ndm_torrent_free(pointer) }
        guard let data = String(cString: pointer).data(using: .utf8),
              let result = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw TorrentFailure("Invalid native response.") }
        guard result["ok"] as? Bool == true else { throw TorrentFailure(result["error"] as? String ?? "Native torrent operation failed.") }
        return result
    }
    public func close() {
        lock.lock(); defer { lock.unlock() }
        if let context { ndm_torrent_destroy(context); self.context = nil }
    }
    deinit { close() }
}

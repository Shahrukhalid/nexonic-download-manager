import Foundation

public struct TorrentFile: Codable, Equatable, Sendable, Identifiable {
    public var index: Int
    public var path: String
    public var size: Int64
    public var received: Int64
    public var priority: Int
    public var pad: Bool
    public var id: Int { index }
}
public struct TorrentSnapshot: Codable, Equatable, Sendable {
    public var state: String
    public var name: String?
    public var size: Int64?
    public var metainfo: String?
    public var files: [TorrentFile]?
    public var infoHashV1: String?
    public var infoHashV2: String?
    public var received: Int64
    public var wantedSize: Int64
    public var downloadSpeed: Int64
    public var uploadSpeed: Int64
    public var uploaded: Int64
    public var peers: Int
    public var seeds: Int
    public var eta: Int64
    public var ratio: Double
    public var payloadComplete: Bool
    public var trackers: [String]
    public var sha256: [String: String]
    public var error: String
    public static func decode(_ result: [String: Any]) throws -> Self {
        let decoder = JSONDecoder(); decoder.keyDecodingStrategy = .convertFromSnakeCase
        return try decoder.decode(Self.self, from: JSONSerialization.data(withJSONObject: result))
    }
    public var progress: Double { wantedSize > 0 ? min(1, Double(received) / Double(wantedSize)) : 0 }
    public var summary: String { "\(peers) peers · \(seeds) seeds · ↓ \(downloadSpeed / 1024) KiB/s · ↑ \(uploadSpeed / 1024) KiB/s · Ratio \(String(format: "%.2f", ratio))" }
}
public struct TorrentOptions: Codable, Equatable, Sendable {
    public var seed = false
    public var ratioLimit = 1.0
    public var seedSeconds = 0
    public var uploadLimitKBps = 32
    public var dht = true
    public init() {}
    public func validate() throws {
        guard ratioLimit.isFinite, ratioLimit >= 0, seedSeconds >= 0, uploadLimitKBps >= 0, uploadLimitKBps <= 2_000_000 else { throw TorrentFailure("Enter valid nonnegative torrent limits.") }
    }
}
public struct TorrentTask: Codable, Equatable, Sendable {
    public var source: String
    public var metadata: TorrentSnapshot?
    public var priorities: [Int]?
    public var options = TorrentOptions()
    public var notified = false
    public init(source: String, metadata: TorrentSnapshot? = nil) {
        self.source = source; self.metadata = metadata
        self.priorities = metadata?.files?.map { $0.pad ? 0 : 4 }
    }
    public func selectedPriorities() throws -> [Int] {
        guard let files = metadata?.files else { throw TorrentFailure("Load torrent metadata first.") }
        let values = priorities ?? files.map { $0.pad ? 0 : 4 }
        guard values.count == files.count, values.allSatisfy({ [0, 1, 4, 7].contains($0) }), values.contains(where: { $0 > 0 }),
              !files.enumerated().contains(where: { $0.element.pad && values[$0.offset] != 0 }) else { throw TorrentFailure("Select at least one real file with valid priorities.") }
        return values
    }
    public var selectedSize: Int64 { zip(metadata?.files ?? [], priorities ?? []).filter { $0.1 > 0 }.reduce(0) { $0 + $1.0.size } }
}

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
/// One tracker and what it last answered.
public struct TrackerStatus: Codable, Equatable, Sendable, Identifiable {
    public var url: String
    public var tier: Int
    public var working: Bool
    public var updating: Bool
    public var message: String
    public var seeds: Int
    public var peers: Int
    public var id: String { url }
    public var statusText: String {
        if updating { return "Updating…" }
        if !message.isEmpty { return message }
        return working ? "Working" : "Not contacted yet"
    }
}
/// One connected peer.
public struct TorrentPeer: Codable, Equatable, Sendable, Identifiable {
    public var address: String
    public var port: Int
    public var client: String
    public var progress: Double
    public var downloadSpeed: Int64
    public var uploadSpeed: Int64
    public var downloaded: Int64
    public var uploaded: Int64
    public var seed: Bool
    public var encrypted: Bool
    public var incoming: Bool
    public var utp: Bool
    public var id: String { "\(address):\(port)" }
    /// Short flags as BitTorrent clients show them: E encrypted, I incoming, P µTP, S seed.
    public var flags: String { (encrypted ? "E" : "") + (incoming ? "I" : "") + (utp ? "P" : "") + (seed ? "S" : "") }
    public static func list(_ result: [String: Any]) throws -> [TorrentPeer] {
        let decoder = JSONDecoder(); decoder.keyDecodingStrategy = .convertFromSnakeCase
        return try decoder.decode([TorrentPeer].self, from: JSONSerialization.data(withJSONObject: result["peers"] ?? [Any]()))
    }
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
    // Swarm and connection details (0.11.2). Optional so tasks saved by older versions still load.
    public var connections: Int?
    public var seedsTotal: Int?
    public var peersTotal: Int?
    public var seedsConnected: Int?
    public var peersConnected: Int?
    public var availability: Double?
    public var downloaded: Int64?
    public var piecesDone: Int?
    public var pieceCount: Int?
    public var pieceSize: Int64?
    public var activeSeconds: Int64?
    public var seedingSeconds: Int64?
    public var maxConnections: Int?
    public var maxUploads: Int?
    public var sequential: Bool?
    public var trackerStatus: [TrackerStatus]?
    public var pieceMap: [Int]?
    public static func decode(_ result: [String: Any]) throws -> Self {
        let decoder = JSONDecoder(); decoder.keyDecodingStrategy = .convertFromSnakeCase
        return try decoder.decode(Self.self, from: JSONSerialization.data(withJSONObject: result))
    }
    public var progress: Double { wantedSize > 0 ? min(1, Double(received) / Double(wantedSize)) : 0 }
    public var summary: String { "\(peers) peers · \(seeds) seeds · ↓ \(downloadSpeed / 1024) KiB/s · ↑ \(uploadSpeed / 1024) KiB/s · Ratio \(String(format: "%.2f", ratio))" }
    /// "3 of 120" style counts: connected, and in the whole swarm when a tracker reported it.
    public var seedsText: String { Self.count(seedsConnected ?? seeds, seedsTotal) }
    public var peersText: String { Self.count(peersConnected ?? max(0, peers - seeds), peersTotal) }
    static func count(_ connected: Int, _ total: Int?) -> String { if let total, total >= 0 { return "\(connected) (\(total))" }; return "\(connected)" }
}
public struct TorrentOptions: Codable, Equatable, Sendable {
    public var seed = false
    public var ratioLimit = 1.0
    public var seedSeconds = 0
    public var uploadLimitKBps = 32
    public var dht = true
    /// Connections and upload slots for this torrent; 0 means no limit.
    public var maxConnections = 50
    public var maxUploads = 4
    /// Download pieces in order (handy for watching a video while it downloads).
    public var sequential = false
    public init() {}
    enum CodingKeys: String, CodingKey { case seed, ratioLimit, seedSeconds, uploadLimitKBps, dht, maxConnections, maxUploads, sequential }
    /// Missing keys keep their defaults, so tasks saved by older versions still load.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = TorrentOptions()
        seed = try c.decodeIfPresent(Bool.self, forKey: .seed) ?? d.seed
        ratioLimit = try c.decodeIfPresent(Double.self, forKey: .ratioLimit) ?? d.ratioLimit
        seedSeconds = try c.decodeIfPresent(Int.self, forKey: .seedSeconds) ?? d.seedSeconds
        uploadLimitKBps = try c.decodeIfPresent(Int.self, forKey: .uploadLimitKBps) ?? d.uploadLimitKBps
        dht = try c.decodeIfPresent(Bool.self, forKey: .dht) ?? d.dht
        // Tasks from before 0.11.2 had no connection limits.
        maxConnections = try c.decodeIfPresent(Int.self, forKey: .maxConnections) ?? 0
        maxUploads = try c.decodeIfPresent(Int.self, forKey: .maxUploads) ?? 0
        sequential = try c.decodeIfPresent(Bool.self, forKey: .sequential) ?? d.sequential
    }
    public func validate() throws {
        guard ratioLimit.isFinite, ratioLimit >= 0, seedSeconds >= 0, uploadLimitKBps >= 0, uploadLimitKBps <= 2_000_000 else { throw TorrentFailure("Enter valid nonnegative torrent limits.") }
        guard maxConnections == 0 || (2...10_000).contains(maxConnections) else { throw TorrentFailure("Connections per torrent must be 0 (no limit) or between 2 and 10,000.") }
        guard (0...10_000).contains(maxUploads) else { throw TorrentFailure("Upload slots must be 0 (no limit) or between 1 and 10,000.") }
    }
    /// The engine's settings for these options (-1 is the engine's "no limit").
    public var engineSettings: [String: Any] {
        ["seed": seed, "ratio_limit": ratioLimit, "seed_seconds": seedSeconds, "upload_limit": uploadLimitKBps * 1024,
         "max_connections": maxConnections == 0 ? -1 : maxConnections, "max_uploads": maxUploads == 0 ? -1 : maxUploads, "sequential": sequential]
    }
}

/// An HTTP or SOCKS5 proxy for peers, trackers and DHT.
public struct TorrentProxy: Equatable, Sendable {
    public enum Kind: String, Sendable { case http, socks5 }
    public var kind: Kind
    public var host: String
    public var port: Int
    public var username = ""
    public var password = ""
    public init(kind: Kind, host: String, port: Int, username: String = "", password: String = "") {
        self.kind = kind; self.host = host; self.port = port; self.username = username; self.password = password
    }
    var engineSettings: [String: Any] { ["type": kind.rawValue, "host": host, "port": port, "username": username, "password": password] }
}

/// App-wide torrent network settings, used by every torrent session.
public struct TorrentNetwork: Codable, Equatable, Sendable {
    public enum Encryption: String, Codable, CaseIterable, Sendable { case prefer, require, off }
    /// Ask the router to forward the listening port (UPnP and NAT-PMP), so more peers can connect.
    public var portForwarding = false
    public var encryption = Encryption.prefer
    /// The proxy torrents connect through, from the app's proxy setting (not saved with these settings).
    public var proxy: TorrentProxy?
    public init() {}
    enum CodingKeys: String, CodingKey { case portForwarding, encryption }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        portForwarding = try c.decodeIfPresent(Bool.self, forKey: .portForwarding) ?? false
        encryption = (try? c.decodeIfPresent(Encryption.self, forKey: .encryption)) ?? .prefer
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

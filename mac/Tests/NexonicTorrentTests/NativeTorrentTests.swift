import CryptoKit
import Foundation
import NexonicTorrent
import XCTest

final class NativeTorrentTests: XCTestCase {
    func testRealTransferSelectionMagnetRestartAndIntegrity() async throws {
        let temporary = FileManager.default.temporaryDirectory.path
        let base = URL(fileURLWithPath: temporary.hasPrefix("/var/") ? "/private" + temporary : temporary)
        let root = base.appendingPathComponent("nexonic-swift-torrent-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let original = root.appendingPathComponent("seed/Legal-試験")
        try FileManager.default.createDirectory(at: original, withIntermediateDirectories: true)
        let bytes = Data((0..<(4 * 1024 * 1024)).map { UInt8($0 % 251) })
        try bytes.write(to: original.appendingPathComponent("selected.bin"))
        try Data("Legal Nexonic fixture\n".utf8).write(to: original.appendingPathComponent("skipped.txt"))
        let seed = try NativeTorrentSession(stateDirectory: root.appendingPathComponent("seed-state"), allowLocalPeers: true, dht: false)
        defer { seed.close() }
        let metadata = try XCTUnwrap(seed.call(["action": "create_torrent", "path": original.path])["metainfo"] as? String)
        _ = try seed.call(["action": "add", "id": "seed", "preview": false, "metainfo": metadata, "destination": original.deletingLastPathComponent().path, "seed": true, "ratio_limit": 0, "upload_limit": 0])
        _ = try seed.call(["action": "resume", "id": "seed"])
        let seeded = try await wait(seed, "seed") { $0["state"] as? String == "seeding" }
        let port = try XCTUnwrap(seed.call(["action": "list"])["listen_port"] as? Int)
        let files = try XCTUnwrap(seeded["files"] as? [[String: Any]])
        let priorities = files.map { ($0["path"] as? String)?.hasSuffix("selected.bin") == true ? 7 : 0 }
        let state = root.appendingPathComponent("download-state")
        var client = try NativeTorrentSession(stateDirectory: state, allowLocalPeers: true, dht: false)
        defer { client.close() }
        _ = try client.call(["action": "add", "id": "preview", "source": try XCTUnwrap(seeded["magnet"] as? String)])
        _ = try client.call(["action": "connect_peer", "id": "preview", "port": port])
        let preview = try await wait(client, "preview") { $0["state"] as? String == "preview" }
        XCTAssertEqual(preview["payload_complete"] as? Bool, false)
        XCTAssertThrowsError(try client.call(["action": "resume", "id": "preview"]))
        _ = try client.call(["action": "remove", "id": "preview"])
        let destination = root.appendingPathComponent("downloaded")
        _ = try client.call(["action": "add", "id": "download", "preview": false, "metainfo": metadata, "destination": destination.path, "priorities": priorities, "download_limit": 262144])
        _ = try client.call(["action": "resume", "id": "download"])
        _ = try client.call(["action": "connect_peer", "id": "download", "port": port])
        _ = try await wait(client, "download") { (($0["received"] as? NSNumber)?.int64Value ?? 0) > 65536 }
        _ = try client.call(["action": "pause", "id": "download"])
        client.close()
        client = try NativeTorrentSession(stateDirectory: state, allowLocalPeers: true, dht: false)
        _ = try client.call(["action": "restore", "id": "download"])
        _ = try client.call(["action": "configure", "id": "download", "download_limit": 0])
        _ = try client.call(["action": "resume", "id": "download"])
        _ = try client.call(["action": "connect_peer", "id": "download", "port": port])
        var options = TorrentOptions(); options.maxConnections = 30; options.maxUploads = 3; options.sequential = true
        var tune = options.engineSettings; tune["action"] = "configure"; tune["id"] = "download"
        let tuned = try TorrentSnapshot.decode(client.call(tune))
        XCTAssertEqual(tuned.maxConnections, 30); XCTAssertEqual(tuned.maxUploads, 3); XCTAssertEqual(tuned.sequential, true)
        XCTAssertGreaterThan(tuned.pieceCount ?? 0, 0); XCTAssertNotNil(tuned.connections); XCTAssertNotNil(tuned.trackerStatus)
        XCTAssertNoThrow(try TorrentPeer.list(client.call(["action": "peers", "id": "download"])))
        let complete = try await wait(client, "download") { $0["state"] as? String == "complete" }
        let selected = try XCTUnwrap(files.first { ($0["path"] as? String)?.hasSuffix("selected.bin") == true }?["path"] as? String)
        XCTAssertEqual(try Data(contentsOf: destination.appendingPathComponent(selected)), bytes)
        let hash = SHA256.hash(data: bytes).map { String(format: "%02x", $0) }.joined()
        XCTAssertEqual((complete["sha256"] as? [String: String])?[selected], hash)
        XCTAssertEqual(complete["payload_complete"] as? Bool, true)
        _ = try client.call(["action": "remove", "id": "download"])
        XCTAssertTrue(FileManager.default.fileExists(atPath: destination.appendingPathComponent(selected).path))
        client.close()
        XCTAssertThrowsError(try client.call(["action": "list"]))
    }
    func testOptionsFromOlderVersionsStillLoad() throws {
        let old = try JSONDecoder().decode(TorrentOptions.self, from: Data(#"{"seed":true,"ratioLimit":2,"seedSeconds":60,"uploadLimitKBps":10,"dht":false}"#.utf8))
        XCTAssertTrue(old.seed); XCTAssertEqual(old.uploadLimitKBps, 10); XCTAssertFalse(old.dht)
        XCTAssertEqual(old.maxConnections, 0); XCTAssertEqual(old.maxUploads, 0); XCTAssertFalse(old.sequential)
        XCTAssertEqual(old.engineSettings["max_connections"] as? Int, -1)
        XCTAssertEqual(TorrentOptions().engineSettings["max_connections"] as? Int, 50)
        var bad = TorrentOptions(); bad.maxConnections = 1
        XCTAssertThrowsError(try bad.validate())
        let peers = try TorrentPeer.list(["peers": [["address": "203.0.113.5", "port": 6881, "client": "Test 1.0", "progress": 0.5, "download_speed": 1024,
            "upload_speed": 0, "downloaded": 4096, "uploaded": 0, "seed": false, "encrypted": true, "incoming": true, "utp": false]] as [[String: Any]]])
        XCTAssertEqual(peers.first?.flags, "EI")
    }
    private func wait(_ engine: NativeTorrentSession, _ id: String, condition: ([String: Any]) -> Bool) async throws -> [String: Any] {
        let deadline = Date().addingTimeInterval(40)
        while Date() < deadline {
            let status = try engine.call(["action": "status", "id": id])
            if status["state"] as? String == "failed" { throw TorrentFailure(status["error"] as? String ?? "Torrent failed") }
            if condition(status) { return status }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        throw TorrentFailure("Timed out waiting for a native transfer.")
    }
}

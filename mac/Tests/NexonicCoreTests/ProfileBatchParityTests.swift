import XCTest
@testable import NexonicCore

/// The Profile Batch behaviour every app shares (tests/profile-batch-vectors.json).
final class ProfileBatchParityTests: XCTestCase {
    private func vectors() throws -> [String: Any] {
        var root = URL(fileURLWithPath: #filePath)
        for _ in 0..<4 { root.deleteLastPathComponent() }
        let data = try Data(contentsOf: root.appendingPathComponent("tests/profile-batch-vectors.json"))
        return try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    private func trim(_ s: String) -> String { s.hasSuffix("/") ? String(s.dropLast()) : s }

    func testKeysMatchEveryApp() throws {
        for pair in try XCTUnwrap(try vectors()["keys"] as? [[String]]) {
            XCTAssertEqual(trim(ProfileBatch.key(pair[0])), trim(pair[1]), pair[0])
        }
    }

    func testDuplicatesMatchEveryApp() throws {
        let c = try XCTUnwrap(try vectors()["choose"] as? [String: Any])
        let found = (c["found"] as? [String] ?? []).map { BatchEntry(url: $0, title: "Clip") }
        let done = Set((c["downloaded"] as? [String] ?? []).map(ProfileBatch.key))
        var filter = BatchFilter(); filter.max = 100
        let (take, counts) = ProfileBatch.choose(found, filter, alreadyDownloaded: { done.contains(ProfileBatch.key($0)) })
        XCTAssertEqual(counts.discovered, c["discovered"] as? Int)
        XCTAssertEqual(counts.queued, c["queued"] as? Int)
        XCTAssertEqual(counts.duplicates, c["duplicates"] as? Int)
        XCTAssertEqual(take.count, counts.queued)
    }
}

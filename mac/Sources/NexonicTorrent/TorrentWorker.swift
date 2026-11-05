import Foundation

/// Shared foreground native worker. Hosts own queue/history policy and must pause it when execution is unavailable.
public final class TorrentWorker: @unchecked Sendable {
    private let gate = NSLock()
    private var work: Task<Void, Never>?
    private var discarded = false
    private var session: NativeTorrentSession?
    private var taskID = ""
    public init() {}
    public func start(id: String, task: TorrentTask, destination: URL, stateDirectory: URL, downloadLimitKBps: Int = 0,
                      network: TorrentNetwork = TorrentNetwork(), onChange: @escaping @Sendable (Result<TorrentSnapshot, Error>) -> Void) {
        gate.lock(); defer { gate.unlock() }
        guard work == nil else { return }
        work = Task.detached(priority: .utility) { [self] in
            var engine: NativeTorrentSession?
            var last: TorrentSnapshot?
            var failure: Error?
            do {
                var task = task
                try task.options.validate()
                guard (0...2_000_000).contains(downloadLimitKBps) else { throw TorrentFailure("Invalid torrent download limit.") }
                if task.metadata == nil { task.metadata = try await TorrentSource.preview(task.source, stateDirectory: stateDirectory) }
                guard let metadata = task.metadata, let metainfo = metadata.metainfo else { throw TorrentFailure("Torrent metadata is missing.") }
                if task.priorities == nil { task.priorities = metadata.files?.map { $0.pad ? 0 : 4 } }
                let priorities = try task.selectedPriorities()
                try Task.checkCancellation()
                let active = try NativeTorrentSession(stateDirectory: stateDirectory, dht: task.options.dht, network: network)
                engine = active
                var request: [String: Any] = ["action": "add", "id": id, "source": task.source, "metainfo": metainfo, "preview": false,
                    "destination": destination.path, "priorities": priorities, "download_limit": downloadLimitKBps * 1024, "trackers": metadata.trackers]
                request.merge(task.options.engineSettings) { _, new in new }
                _ = try active.call(request)
                attach(active, id: id)
                var snapshot = try TorrentSnapshot.decode(active.call(["action": "resume", "id": id]))
                while true {
                    try Task.checkCancellation()
                    if snapshot.state == "failed" { throw TorrentFailure(snapshot.error) }
                    last = snapshot
                    if snapshot.state == "complete" { break }
                    if !isDiscarded { onChange(.success(snapshot)) }
                    try await Task.sleep(nanoseconds: 500_000_000)
                    snapshot = try TorrentSnapshot.decode(active.call(["action": "status", "id": id]))
                }
            } catch is CancellationError {
                // The final paused event is delivered after checkpoint flushing below.
            } catch { failure = error }
            attach(nil, id: id)
            if let engine {
                _ = try? engine.call(["action": "pause", "id": id])
                _ = try? engine.call(["action": "flush"])
                if isDiscarded { _ = try? engine.call(["action": "remove", "id": id]) }
                engine.close()
            }
            if let failure, !isDiscarded { onChange(.failure(failure)) }
            else if Task.isCancelled, !isDiscarded, var snapshot = last {
                snapshot.state = "paused"; onChange(.success(snapshot))
            } else if Task.isCancelled, !isDiscarded, last == nil {
                onChange(.failure(CancellationError()))
            } else if !isDiscarded, let snapshot = last, snapshot.state == "complete" { onChange(.success(snapshot)) }
        }
    }
    private func attach(_ engine: NativeTorrentSession?, id: String) { gate.lock(); defer { gate.unlock() }; session = engine; taskID = id }
    private var running: (NativeTorrentSession, String)? { gate.lock(); defer { gate.unlock() }; return session.map { ($0, taskID) } }

    /// Changes limits, connections, seeding, priorities or trackers while the torrent runs. False when it is not running.
    @discardableResult
    public func configure(_ settings: [String: Any]) throws -> Bool {
        guard let active = running else { return false }
        var request = settings; request["action"] = "configure"; request["id"] = active.1
        _ = try active.0.call(request)
        return true
    }
    /// The connected peers, or none when the torrent is not running.
    public func peers() -> [TorrentPeer] {
        guard let active = running, let result = try? active.0.call(["action": "peers", "id": active.1]) else { return [] }
        return (try? TorrentPeer.list(result)) ?? []
    }
    /// Asks the trackers and DHT for more peers now.
    public func reannounce() { if let active = running { _ = try? active.0.call(["action": "reannounce", "id": active.1]) } }
    private var isDiscarded: Bool { gate.lock(); defer { gate.unlock() }; return discarded }
    public func pause() { gate.lock(); defer { gate.unlock() }; work?.cancel() }
    public func discard() { gate.lock(); defer { gate.unlock() }; discarded = true; work?.cancel() }
    deinit { work?.cancel() }
}

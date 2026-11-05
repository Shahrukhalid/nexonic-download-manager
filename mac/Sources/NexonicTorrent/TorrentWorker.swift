import Foundation

/// Shared foreground native worker. Hosts own queue/history policy and must pause it when execution is unavailable.
public final class TorrentWorker: @unchecked Sendable {
    private let gate = NSLock()
    private var work: Task<Void, Never>?
    private var discarded = false
    public init() {}
    public func start(id: String, task: TorrentTask, destination: URL, stateDirectory: URL, downloadLimitKBps: Int = 0,
                      onChange: @escaping @Sendable (Result<TorrentSnapshot, Error>) -> Void) {
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
                let active = try NativeTorrentSession(stateDirectory: stateDirectory, dht: task.options.dht)
                engine = active
                _ = try active.call(["action": "add", "id": id, "source": task.source, "metainfo": metainfo, "preview": false,
                    "destination": destination.path, "priorities": priorities, "seed": task.options.seed,
                    "ratio_limit": task.options.ratioLimit, "seed_seconds": task.options.seedSeconds,
                    "download_limit": downloadLimitKBps * 1024, "upload_limit": task.options.uploadLimitKBps * 1024, "trackers": metadata.trackers])
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
    private var isDiscarded: Bool { gate.lock(); defer { gate.unlock() }; return discarded }
    public func pause() { gate.lock(); defer { gate.unlock() }; work?.cancel() }
    public func discard() { gate.lock(); defer { gate.unlock() }; discarded = true; work?.cancel() }
    deinit { work?.cancel() }
}

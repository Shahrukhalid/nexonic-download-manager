import Foundation
import NexonicTorrent

public enum TorrentPolicy {
    public static func unavailable(_ settings: AppSettings) -> String? {
        if !settings.torrentsEnabled { return "Enable experimental torrents first." }
        if settings.proxy.mode != .none { return "Torrents require No proxy; system/custom proxies are not supported." }
        if settings.speedLimitKBps > 0 { return "Torrents do not support the shared global limit. Use a per-torrent limit." }
        return nil
    }
}
public final class TorrentTransfer: Downloader, @unchecked Sendable {
    public var onChange: (@Sendable (DownloadRecord) -> Void)?
    public let ownLimiter = SpeedLimiter()
    private var record: DownloadRecord
    private let settings: AppSettings
    private let worker = TorrentWorker()
    public init(record: DownloadRecord, settings: AppSettings) { self.record = record; self.settings = settings }
    public func start() {
        guard let task = record.torrent else { fail(TorrentFailure("Torrent metadata is missing.")); return }
        if let reason = TorrentPolicy.unavailable(settings) { fail(TorrentFailure(reason)); return }
        let folder = record.folder.appendingPathComponent("Nexonic torrents/\(record.id.uuidString)")
        let state = AppPaths.parts.appendingPathComponent(record.id.uuidString).appendingPathComponent("native")
        worker.start(id: "payload", task: task, destination: folder, stateDirectory: state, downloadLimitKBps: record.speedLimitKBps ?? 0) { [self] result in
            switch result {
            case .failure(let error): fail(error)
            case .success(let snapshot):
                record.torrent?.metadata = snapshot
                if record.torrent?.priorities == nil { record.torrent?.priorities = snapshot.files?.map { $0.pad ? 0 : 4 } }
                record.fileName = snapshot.name ?? record.fileName
                record.size = record.torrent?.selectedSize
                switch snapshot.state {
                case "complete": record.status = .finished
                case "seeding": record.status = .seeding
                case "paused": record.status = .paused
                case "checking", "verifying": record.status = .probing
                default: record.status = .downloading
                }
                if snapshot.payloadComplete, let task = record.torrent {
                    let files = zip(snapshot.files ?? [], task.priorities ?? []).filter { $0.1 > 0 && !$0.0.pad }.map { $0.0 }
                    record.savedPath = files.count == 1 ? folder.appendingPathComponent(files[0].path).path
                        : folder.appendingPathComponent(snapshot.name ?? "").path
                    record.finished = record.finished ?? Date()
                }
                onChange?(record)
            }
        }
    }
    private func fail(_ error: Error) {
        record.status = error is CancellationError ? .paused : .failed
        record.error = error is CancellationError ? nil : error.localizedDescription
        onChange?(record)
    }
    public func pause() { worker.pause() }
    public func discard() { worker.discard() }
}

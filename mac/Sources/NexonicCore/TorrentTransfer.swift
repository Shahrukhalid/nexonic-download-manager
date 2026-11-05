import CFNetwork
import Foundation
import NexonicTorrent

public enum TorrentPolicy {
    /// Why torrents can't run now, or nil. Proxies and the speed limit for all downloads apply to torrents too.
    public static func unavailable(_ settings: AppSettings) -> String? {
        if !settings.torrentsEnabled { return "Turn on torrents first." }
        return nil
    }
    /// A torrent's download limit: its own limit, capped by the limit for all downloads.
    public static func downloadLimit(_ own: Int?, settings: AppSettings) -> Int {
        let own = max(0, own ?? 0), all = max(0, settings.speedLimitKBps)
        if own == 0 { return all }
        return all == 0 ? own : min(own, all)
    }
}

extension AppSettings {
    /// Torrent network settings with the app's proxy: No proxy connects directly, HTTP and SOCKS5 use the proxy
    /// entered in Settings, and "system settings" uses the Mac's SOCKS or HTTPS/HTTP proxy when one is turned on.
    public var torrentSessionNetwork: TorrentNetwork {
        var network = torrentNetwork
        switch proxy.mode {
        case .none: network.proxy = nil
        case .http, .socks:
            let host = proxy.host.trimmingCharacters(in: .whitespaces)
            network.proxy = host.isEmpty || !(1...65535).contains(proxy.port) ? nil
                : TorrentProxy(kind: proxy.mode == .socks ? .socks5 : .http, host: host, port: proxy.port, username: proxy.username, password: proxy.password)
        case .system: network.proxy = Self.systemProxy()
        }
        return network
    }

    static func systemProxy() -> TorrentProxy? {
        guard let settings = CFNetworkCopySystemProxySettings()?.takeRetainedValue() as? [String: Any] else { return nil }
        for (kind, prefix) in [(TorrentProxy.Kind.socks5, "SOCKS"), (.http, "HTTPS"), (.http, "HTTP")] {
            guard (settings[prefix + "Enable"] as? NSNumber)?.boolValue == true,
                  let host = settings[prefix + "Proxy"] as? String, !host.isEmpty,
                  let port = (settings[prefix + "Port"] as? NSNumber)?.intValue, (1...65535).contains(port) else { continue }
            return TorrentProxy(kind: kind, host: host, port: port)
        }
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
        worker.start(id: "payload", task: task, destination: folder, stateDirectory: state, downloadLimitKBps: TorrentPolicy.downloadLimit(record.speedLimitKBps, settings: settings),
                     network: settings.torrentSessionNetwork) { [self] result in
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
    /// Applies new options, download limit and file priorities to the running torrent. False when it is not running.
    @discardableResult
    public func apply(_ task: TorrentTask, limitKBps: Int, trackers: [String]? = nil) throws -> Bool {
        var request = task.options.engineSettings
        if let trackers { request["trackers"] = trackers }
        request["download_limit"] = TorrentPolicy.downloadLimit(limitKBps, settings: settings) * 1024
        if let priorities = task.priorities { request["priorities"] = priorities }
        guard try worker.configure(request) else { return false }
        record.torrent?.options = task.options; record.torrent?.priorities = task.priorities
        return true
    }
    public func peers() -> [TorrentPeer] { worker.peers() }
    public func reannounce() { worker.reannounce() }
    public func pause() { worker.pause() }
    public func discard() { worker.discard() }
}

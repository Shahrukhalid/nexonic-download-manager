import NexonicKit
import SwiftUI

/// The app's view of the shared download list. It reloads when the Share extension or a background download changes it.
@Observable
final class AppModel {
    var jobs: [DownloadJob] = []
    var speeds: [UUID: Int64] = [:]
    var tab: AppTab = .downloads
    var addInput: DownloadInput?
    var torrentRequest: TorrentRequest?
    var showSetup = false
    var showPaste = false
    var toast: String?
    var browserURL: URL?
    var selection: UUID?
    /// Screenshots: open Found on this page once the page has loaded.
    var demoShowFound = false

    @ObservationIgnored private var observer: ChangeObserver?
    // Download limit: bytes seen at the last tick, and downloads stopped by the limit (started again in the next period).
    @ObservationIgnored private var lastWritten: [UUID: Int64] = [:]
    @ObservationIgnored private var heldByCap: Set<UUID> = []
    @ObservationIgnored private var cap = DataCap.saved
    @ObservationIgnored private var timer: Timer?

    init() {
        // Reconnect to downloads that kept running while the app was closed.
        _ = Downloader.shared(Downloader.appSessionID)
        observer = ChangeObserver { [weak self] in self?.refresh() }
        refresh(importing: true)
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.tick() }
        Demo.runIfRequested(self)
    }

    var activeCount: Int { jobs.filter { $0.isActive }.count }

    func refresh(importing: Bool = false) {
        if importing { Library.importFinished() }
        jobs = Store.shared.load()
        // Downloads started from the Share extension: follow their progress here once the extension has closed.
        if jobs.contains(where: { $0.sessionID == Downloader.shareSessionID && $0.status == .downloading }) {
            _ = Downloader.shared(Downloader.shareSessionID)
        }
        if jobs.contains(where: { $0.incomingPath != nil && $0.savedPath == nil }) && !importing {
            Library.importFinished(); jobs = Store.shared.load()
        }
    }

    private func tick() {
        countData()
        guard jobs.contains(where: { $0.status == .downloading }) else { if !speeds.isEmpty { speeds = [:] }; return }
        var s: [UUID: Int64] = [:]
        for id in Set(jobs.filter { $0.status == .downloading }.compactMap { $0.sessionID }) { s.merge(Downloader.shared(id).currentSpeeds) { a, _ in a } }
        speeds = s
        jobs = Store.shared.load()
    }

    /// Download limit (while the app is open): at the limit, downloads stop until the next period starts.
    private func countData() {
        var got: Int64 = 0
        for j in jobs {
            if j.status == .downloading, let before = lastWritten[j.id], j.written > before { got += j.written - before }
            lastWritten[j.id] = j.written
        }
        let limit = SharedSettings.dataCapMB, hours = SharedSettings.dataCapHours
        if cap.roll(Date(), hours: hours) || limit == 0 || !cap.reached(limitMB: limit) {
            if !heldByCap.isEmpty {
                let held = heldByCap
                heldByCap = []
                jobs.filter { held.contains($0.id) && $0.status == .paused }.forEach(resume)
            }
        }
        guard limit > 0 else { return }
        cap.add(got)
        if got > 0 { DataCap.saved = cap }
        guard cap.reached(limitMB: limit), heldByCap.isEmpty else { return }
        let active = jobs.filter { $0.isActive }
        guard !active.isEmpty else { return }
        heldByCap = Set(active.map(\.id))
        active.forEach(pause)
        toast = "Download limit reached. Downloads continue " + cap.nextPeriod(hours: hours).formatted(date: .abbreviated, time: .shortened) + "."
    }

    func firstLaunch() {
        if !UserDefaults.standard.bool(forKey: "setupShown") && !Demo.active {
            UserDefaults.standard.set(true, forKey: "setupShown")
            showSetup = true
        }
    }

    // MARK: Actions

    func pause(_ job: DownloadJob) { Downloader.shared(job.sessionID ?? Downloader.appSessionID).pause(job.id) }

    func resume(_ job: DownloadJob) {
        guard var j = Store.shared.job(job.id) else { return }
        j.status = .queued
        Downloader.shared(Downloader.appSessionID).start(j)
    }

    func startLater() { jobs.filter { $0.status == .later || $0.status == .paused || $0.status == .failed }.forEach(resume) }
    func pauseAll() { jobs.filter { $0.isActive }.forEach(pause) }

    func remove(_ job: DownloadJob, deleteFile: Bool) {
        Downloader.shared(job.sessionID ?? Downloader.appSessionID).cancel(job.id)
        Keychain.remove(job.id)
        if deleteFile, let url = Library.fileURL(job) { try? FileManager.default.removeItem(at: url) }
        if let incoming = job.incomingPath { try? FileManager.default.removeItem(at: AppGroup.folder.appendingPathComponent(incoming).deletingLastPathComponent()) }
        Store.shared.update { $0.removeAll { $0.id == job.id } }
        refresh()
    }

    func clearFinished() {
        Store.shared.update { $0.removeAll { $0.status == .complete } }
        refresh()
    }

    func setLogin(_ job: DownloadJob, user: String, password: String) { Keychain.setLogin(user: user, password: password, for: job.id) }

    /// nexonic://download?url=… (used by the Shortcut guide) and web links opened with Nexonic.
    func open(_ url: URL) {
        if url.scheme?.lowercased() == "magnet" { tab = .downloads; torrentRequest = TorrentRequest(source: url.absoluteString); return }
        if url.isFileURL, url.pathExtension.lowercased() == "torrent" {
            if let local = TorrentAddView.copyIn(url) { tab = .downloads; torrentRequest = TorrentRequest(source: local.path) }
            return
        }
        if url.scheme == "nexonic" {
            let items = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems ?? []
            let links = items.filter { $0.name == "url" }.compactMap { $0.value.flatMap(Net.webURL) }
            if !links.isEmpty { tab = .downloads; addInput = DownloadInput(urls: links) }
        } else if let web = Net.webURL(url.absoluteString) {
            tab = .browser; browserURL = web
        }
    }
}

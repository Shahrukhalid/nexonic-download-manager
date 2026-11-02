import NexonicKit
import SwiftUI

/// The app's view of the shared download list. It reloads when the Share extension or a background download changes it.
@Observable
final class AppModel {
    var jobs: [DownloadJob] = []
    var speeds: [UUID: Int64] = [:]
    var tab: AppTab = .downloads
    var addInput: DownloadInput?
    var showSetup = false
    var showPaste = false
    var toast: String?
    var browserURL: URL?
    var selection: UUID?
    /// Screenshots: open Found on this page once the page has loaded.
    var demoShowFound = false

    @ObservationIgnored private var observer: ChangeObserver?
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
        guard jobs.contains(where: { $0.status == .downloading }) else { if !speeds.isEmpty { speeds = [:] }; return }
        var s: [UUID: Int64] = [:]
        for id in Set(jobs.filter { $0.status == .downloading }.compactMap { $0.sessionID }) { s.merge(Downloader.shared(id).currentSpeeds) { a, _ in a } }
        speeds = s
        jobs = Store.shared.load()
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
        if url.scheme == "nexonic" {
            let items = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems ?? []
            let links = items.filter { $0.name == "url" }.compactMap { $0.value.flatMap(Net.webURL) }
            if !links.isEmpty { tab = .downloads; addInput = DownloadInput(urls: links) }
        } else if let web = Net.webURL(url.absoluteString) {
            tab = .browser; browserURL = web
        }
    }
}

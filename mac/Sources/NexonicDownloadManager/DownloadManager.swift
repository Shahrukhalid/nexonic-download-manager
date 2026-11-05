import AppKit
import CoreServices
import Foundation
import NexonicCore
import NexonicTorrent
import UserNotifications

/// What the Add window opens with (from the toolbar, the clipboard or a batch).
struct AddRequest: Identifiable {
    let id = UUID()
    var links: String
    var queueID: String = DownloadQueue.mainID
}

@MainActor
final class DownloadManager: ObservableObject {
    static let shared = DownloadManager()
    /// Opens and fronts the main window (set by the app once its scene exists).
    static var showMainWindow: (() -> Void)?

    @Published private(set) var items: [DownloadRecord] = []
    @Published private(set) var speeds: [UUID: Double] = [:]  // bytes per second
    @Published var queues: [DownloadQueue] = [] {
        didSet { save(queues, to: AppPaths.queues) }
    }
    @Published var addRequest: AddRequest?
    @Published var settings: AppSettings {
        didSet {
            limiter.setLimit(bytesPerSecond: settings.speedLimitKBps * 1024)
            if TorrentPolicy.unavailable(settings) != nil {
                for item in items where item.torrent != nil && item.status.isActive { pause(item.id) }
            }
            saveSettings()
            schedule()
        }
    }

    /// Cookies and passwords are kept in a private file (see FileStore); `stored` is what it holds now.
    private static let vault: SecretStore = FileStore()
    private var vaultReadable = false
    private var stored: [String: String] = [:]
    /// Plain-text secrets found in files saved by 0.6.1, kept there (never added to) until the secrets file takes them.
    private var legacySecrets: [String: String] = [:]
    /// The download list could not be read: its saved secrets are kept, not pruned (see SavedFile).
    private var listDamaged = false

    private var transfers: [UUID: Downloader] = [:]
    private var recent: [UUID: [Int64]] = [:]  // received bytes at each of the last ticks
    private var requeue: Set<UUID> = []
    private var pendingRefresh: [UUID: String?] = [:]
    private var queuesWithWork: Set<String> = []
    private var lastMinute = ""
    private var clipboardCount = NSPasteboard.general.changeCount
    private let limiter = SpeedLimiter()
    private var timer: Timer?
    private var ticks = 0

    init() {
        try? FileManager.default.createDirectory(at: AppPaths.support, withIntermediateDirectories: true)
        var loadedSettings = Self.load(AppSettings.self, from: AppPaths.settings) ?? AppSettings()
        let list = SavedFile.read([DownloadRecord].self, from: AppPaths.downloads)
        var loadedItems = list.value ?? []
        listDamaged = list.damaged
        legacySecrets = Secrets.collect(loadedItems, loadedSettings)
        if let saved = Self.vault.read() {
            vaultReadable = true
            stored = saved
            Secrets.fill(&loadedItems, &loadedSettings, from: saved)
        }
        settings = loadedSettings
        items = loadedItems
        var qs = Self.load([DownloadQueue].self, from: AppPaths.queues) ?? []
        if !qs.contains(where: \.isMain) { qs.insert(.main, at: 0) }
        queues = qs
        for i in items.indices where items[i].status.isActive { items[i].status = .queued }  // interrupted by quit
        limiter.setLimit(bytesPerSecond: settings.speedLimitKBps * 1024)
        timer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        if settings.notifications {
            UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
        }
        // Lists saved by 0.6.1 or older hold cookies and passwords in the file: move them into the secrets file now.
        persist()
        saveSettings()
        collectInbox()
        schedule()
        DispatchQueue.global(qos: .utility).async { SiteVideoTools.prepare() }  // keeps the video engine current
    }

    /// Sample list for the README screenshots (only used when the app is started with NDM_DEMO=1).
    func loadDemo(_ records: [DownloadRecord], queues extra: [DownloadQueue]) {
        items = records
        queues = [.main] + extra
        schedule()
    }

    // MARK: adding

    struct AddOptions {
        var folder: URL?
        var fileName: String?
        var queueID: String = DownloadQueue.mainID
        var start = true
        var username = ""
        var password = ""
        var checksum = ""
        var speedLimitKBps = 0
        var audioOnly = false
    }

    func add(links text: String, options: AddOptions = AddOptions()) {
        let lines = Links.split(text)
        for line in lines {
            var job = DownloadJob(url: line)
            if lines.count == 1, let n = options.fileName, !n.isEmpty { job.fileName = n }
            if job.validURL != nil { add(job, options: options) }
        }
    }

    @discardableResult
    func add(_ job: DownloadJob, options o: AddOptions = AddOptions()) -> UUID? {
        guard let url = job.validURL else { return nil }
        let site = job.site == true || (SiteVideo.isSupported(url) && job.stream != true)
        let stream = !site && (job.stream == true || HLS.isPlaylistURL(url))
        var name = job.fileName.map(FileNames.sanitize) ?? FileNames.fromURL(url)
        if stream { name = (name as NSString).deletingPathExtension + ".mp4" }
        if site && job.fileName == nil { name = "Video from \(url.host ?? "site")" }
        // A link the user asked to refresh: keep what is downloaded and continue from the new address.
        if let i = items.firstIndex(where: { $0.awaitingRefresh == true && $0.fileName == name }) {
            items[i].url = url
            items[i].cookies = job.cookies ?? items[i].cookies
            items[i].referrer = job.referrer ?? items[i].referrer
            items[i].awaitingRefresh = nil
            items[i].status = .queued
            items[i].error = nil
            persist()
            schedule()
            return items[i].id
        }
        // Settings for this website (Settings → Sites): its folder unless another one was chosen, its login, user agent and limit.
        let profile = HostProfiles.match(settings.hostProfiles, url)
        var folder = job.folder.map { URL(fileURLWithPath: $0, isDirectory: true) } ?? o.folder ?? settings.folder
        if let siteFolder = profile?.folder, !siteFolder.isEmpty, job.folder == nil, o.folder == nil || o.folder == settings.folder {
            folder = URL(fileURLWithPath: siteFolder, isDirectory: true)
        }
        let agent = (profile?.userAgent).flatMap { $0.isEmpty ? nil : $0 } ?? job.userAgent
        var record = DownloadRecord(url: url, folder: folder, fileName: name, referrer: job.referrer,
                                    cookies: job.cookies, userAgent: agent)
        if TorrentSource.isTorrent(url.absoluteString) { record.torrent = TorrentTask(source: url.absoluteString) }
        if stream { record.isStream = true }
        if site {
            record.siteVideo = true
            record.audioOnly = (job.audioOnly ?? o.audioOnly) ? true : nil
            record.maxHeight = record.audioOnly == true ? nil : job.maxHeight
            record.infoJSON = job.infoJSON
        }
        record.queueID = job.queue.map(queueID(named:))
            ?? (o.queueID == DownloadQueue.mainID ? (profile?.queueName).flatMap { $0.isEmpty ? nil : queueID(named: $0) } : nil)
            ?? o.queueID
        record.username = job.username ?? (o.username.isEmpty ? nil : o.username) ?? (profile?.username).flatMap { $0.isEmpty ? nil : $0 }
        let ownLogin = job.username != nil || !o.username.isEmpty
        record.password = job.password ?? (o.password.isEmpty ? nil : o.password) ?? (ownLogin ? nil : (profile?.password).flatMap { $0.isEmpty ? nil : $0 })
        record.checksum = job.checksum ?? (o.checksum.isEmpty ? nil : o.checksum)
        let limit = job.speedLimitKBps ?? (o.speedLimitKBps > 0 ? o.speedLimitKBps : profile?.speedLimitKBps ?? 0)
        record.speedLimitKBps = limit > 0 ? limit : nil
        if !(job.start ?? (profile?.startAtOnce == false ? false : o.start)) { record.status = .paused }
        items.append(record)
        schedule()
        persist()
        return record.id
    }

    func addBatch(_ pattern: String, options: AddOptions) throws -> Int {
        let links = try BatchPattern.expand(pattern)
        add(links: links.joined(separator: "\n"), options: options)
        return links.count
    }

    /// Downloads handed over by the browser extension and the ndm command.
    /// A link the user clicked in the browser opens the download window (unless turned off); the same link
    /// already downloading is never added twice, its progress window is shown instead.
    func collectInbox() {
        let jobs = Inbox().collect()
        guard !jobs.isEmpty else { return }
        var browserJob = false
        for job in jobs {
            if job.fromBrowser == true { browserJob = true }
            if let dup = items.first(where: { $0.url.absoluteString == job.url && ($0.status.isActive || $0.status == .queued) }) {
                if settings.showProgressWindow && job.fromBrowser == true { ProgressWindow.show(dup.id, manager: self) }
                continue
            }
            if DownloadInfoWindow.isOpen(for: job.url) { continue }
            if job.fromBrowser == true && settings.showDownloadDialog {
                DownloadInfoWindow.show(job, manager: self)
                continue
            }
            let id = add(job)
            if let id, job.fromBrowser == true, job.start != false, settings.showProgressWindow {
                ProgressWindow.show(id, manager: self)
            }
        }
        if browserJob && settings.bringToFrontOnBrowserDownload { Self.showMainWindow?() }
    }

    // MARK: control

    func pause(_ id: UUID) {
        if let t = transfers[id] { t.pause() } else { update(id) { if $0.status != .finished { $0.status = .paused } } }
    }

    func resume(_ id: UUID) {
        update(id) { if [.paused, .failed].contains($0.status) { $0.status = .queued; $0.error = nil } }
        if let q = items.first(where: { $0.id == id })?.queue, let i = queues.firstIndex(where: { $0.id == q }), !queues[i].running {
            queues[i].running = true  // resuming a download starts its queue
        }
        schedule()
    }

    func remove(_ id: UUID, deleteFile: Bool = false) {
        transfers.removeValue(forKey: id)?.discard()
        if deleteFile, let path = items.first(where: { $0.id == id })?.savedPath {
            try? FileManager.default.trashItem(at: URL(fileURLWithPath: path), resultingItemURL: nil)
        }
        try? FileManager.default.removeItem(at: AppPaths.parts.appendingPathComponent(id.uuidString))
        if let info = items.first(where: { $0.id == id })?.infoJSON { try? FileManager.default.removeItem(atPath: info) }
        items.removeAll { $0.id == id }
        persist()
        schedule()
    }

    func pauseAll() { for i in items where i.status.isActive || i.status == .queued { pause(i.id) } }
    func resumeAll() { for i in items where i.status == .paused || i.status == .failed { resume(i.id) } }
    func clearFinished() { for i in items where i.status == .finished { remove(i.id) } }

    func reveal(_ id: UUID) {
        guard let path = items.first(where: { $0.id == id })?.savedPath else { return }
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: path)])
    }

    func open(_ id: UUID) {
        guard let path = items.first(where: { $0.id == id })?.savedPath else { return }
        NSWorkspace.shared.open(URL(fileURLWithPath: path))
    }

    /// Continues from a new address, keeping what is already downloaded.
    func refresh(_ id: UUID, with link: String?) {
        if let t = transfers[id] {
            pendingRefresh[id] = .some(link)  // applied once the transfer has stopped
            t.pause()
            return
        }
        update(id) { r in
            if let link, let u = DownloadJob(url: link).validURL {
                r.url = u
                r.awaitingRefresh = nil
                r.status = .queued
                r.error = nil
            } else {
                r.awaitingRefresh = true  // the next browser download with this file name continues it
                r.status = .paused
                r.error = "Waiting for a fresh link from the browser"
            }
        }
        schedule()
    }

    func setDetails(_ id: UUID, username: String, password: String, checksum: String, speedLimitKBps: Int, queueID: String) {
        update(id) { r in
            r.username = username.isEmpty ? nil : username
            r.password = password.isEmpty ? nil : password
            r.checksum = checksum.isEmpty ? nil : checksum
            r.speedLimitKBps = speedLimitKBps > 0 ? speedLimitKBps : nil
            r.queueID = queueID
        }
        transfers[id]?.ownLimiter.setLimit(bytesPerSecond: max(0, speedLimitKBps) * 1024)
        schedule()
    }

    func verify(_ id: UUID, expected: String) -> String {
        guard let r = items.first(where: { $0.id == id }), let path = r.savedPath else { return "The file is not downloaded yet" }
        guard let (algorithm, hex) = Checksum.parse(expected) else { return "Paste an MD5, SHA-1, SHA-256 or SHA-512 value" }
        guard let actual = try? Checksum.compute(algorithm, file: URL(fileURLWithPath: path)) else { return "The file could not be read" }
        let result = actual == hex ? "\(algorithm.rawValue) matches" : "\(algorithm.rawValue) does not match (got \(actual))"
        update(id) { $0.checksum = expected; $0.checksumResult = result }
        return result
    }

    // MARK: queues and scheduler

    func queueName(_ id: String) -> String { queues.first { $0.id == id }?.name ?? "Main queue" }

    private func queueID(named name: String) -> String {
        if let q = queues.first(where: { $0.name.caseInsensitiveCompare(name) == .orderedSame }) { return q.id }
        let q = DownloadQueue(name: name, running: true)
        queues.append(q)
        return q.id
    }

    func newQueue() -> String {
        var n = queues.count + 1
        while queues.contains(where: { $0.name == "Queue \(n)" }) { n += 1 }
        let q = DownloadQueue(name: "Queue \(n)")
        queues.append(q)
        return q.id
    }

    func deleteQueue(_ id: String) {
        guard id != DownloadQueue.mainID else { return }
        for i in items.indices where items[i].queueID == id { items[i].queueID = nil }
        queues.removeAll { $0.id == id }
        persist()
        schedule()
    }

    func startQueue(_ id: String) {
        guard let i = queues.firstIndex(where: { $0.id == id }) else { return }
        queues[i].running = true
        for j in items.indices where items[j].queue == id && [.paused, .failed].contains(items[j].status) && items[j].awaitingRefresh != true {
            items[j].status = .queued
            items[j].error = nil
        }
        persist()
        schedule()
    }

    func stopQueue(_ id: String) {
        guard let i = queues.firstIndex(where: { $0.id == id }) else { return }
        queues[i].running = false
        for item in items where item.queue == id && item.status.isActive {
            requeue.insert(item.id)
            transfers[item.id]?.pause()
        }
    }

    private func runTimetable() {
        let now = Date()
        let minute = QueueClock.hhmm(now)
        guard minute != lastMinute else { return }
        lastMinute = minute
        for q in queues {
            if QueueClock.shouldStart(q, at: now) {
                startQueue(q.id)
                if let i = queues.firstIndex(where: { $0.id == q.id }) { queues[i].lastStartDay = QueueClock.dayKey(now) }
            } else if QueueClock.shouldStop(q, at: now) {
                stopQueue(q.id)
            }
        }
    }

    private func checkQueuesDone() {
        for q in queues where q.running {
            let busy = items.contains { $0.queue == q.id && ($0.status.isActive || $0.status == .queued) }
            if busy { queuesWithWork.insert(q.id); continue }
            guard queuesWithWork.remove(q.id) != nil else { continue }
            if !q.isMain, let i = queues.firstIndex(where: { $0.id == q.id }) { queues[i].running = false }
            afterQueue(q)
        }
    }

    private func afterQueue(_ q: DownloadQueue) {
        switch q.afterDone {
        case .nothing:
            return
        case .quit:
            persist()
            NSApp.terminate(nil)
        case .sleep:
            post("Queue finished", "\(q.name) is done. The Mac will sleep now.")
            run("/usr/bin/pmset", ["sleepnow"])
        case .shutDown:
            persist()
            post("Queue finished", "\(q.name) is done. Shutting down.")
            var error: NSDictionary?
            NSAppleScript(source: "tell application \"System Events\" to shut down")?.executeAndReturnError(&error)
        }
    }

    private func schedule() {
        for q in queues where q.running {
            let limit = q.isMain ? settings.maxActive : q.maxActive
            var slots = max(1, limit) - items.filter { $0.queue == q.id && $0.status.isActive && $0.status != .seeding }.count
            for item in items where item.queue == q.id && item.status == .queued && slots > 0 && transfers[item.id] == nil {
                slots -= 1
                let t: Downloader
                let profile = HostProfiles.match(settings.hostProfiles, item.url)
                let proxy = profile?.proxy ?? settings.proxy
                let connections = max(1, min(32, profile?.maxConnections ?? settings.connections))
                if item.torrent != nil {
                    t = TorrentTransfer(record: item, settings: settings)
                } else if item.siteVideo == true {
                    t = SiteVideoTransfer(record: item, sortIntoCategories: settings.sortIntoCategoryFolders,
                                          speedLimitKBps: settings.speedLimitKBps, proxy: proxy)
                } else if item.isStream == true {
                    t = StreamTransfer(record: item, connections: connections, limiter: limiter,
                                       sortIntoCategories: settings.sortIntoCategoryFolders, proxy: proxy)
                } else {
                    t = Transfer(record: item, connections: connections, limiter: limiter,
                                 sortIntoCategories: settings.sortIntoCategoryFolders, proxy: proxy,
                                 options: .from(settings.network, profile))
                }
                t.onChange = { [weak self] rec in Task { @MainActor in self?.changed(rec) } }
                transfers[item.id] = t
                update(item.id) { $0.status = .probing }
                t.start()
            }
        }
    }

    private func changed(_ rec: DownloadRecord) {
        guard let i = items.firstIndex(where: { $0.id == rec.id }) else { return }
        let before = items[i].status
        var rec = rec
        // Fields edited in the app while the transfer runs stay as edited.
        rec.queueID = items[i].queueID
        rec.speedLimitKBps = items[i].speedLimitKBps
        if rec.status == .paused, requeue.remove(rec.id) != nil { rec.status = .queued }
        if items[i].torrent?.notified == true { rec.torrent?.notified = true }
        if rec.torrent?.metadata?.payloadComplete == true, rec.torrent?.notified != true {
            rec.torrent?.notified = true
            finished(rec)
        }
        items[i] = rec
        if rec.status == .seeding, before != .seeding { persist(); schedule() }
        if rec.status == .paused, let link = pendingRefresh.removeValue(forKey: rec.id) {
            transfers.removeValue(forKey: rec.id)
            refresh(rec.id, with: link)
            return
        }
        if before != rec.status, [.finished, .failed, .paused, .queued].contains(rec.status) {
            transfers.removeValue(forKey: rec.id)
            if rec.status == .finished, rec.torrent == nil { finished(rec) }
            if rec.status == .failed { post("Download failed", "\(rec.fileName): \(rec.error ?? "")") }
            persist()
            schedule()
        }
    }

    func addTorrent(_ task: TorrentTask, folder: URL, queueID: String, limit: Int, start: Bool) throws {
        if let reason = TorrentPolicy.unavailable(settings) { throw TorrentFailure(reason) }
        _ = try task.selectedPriorities(); try task.options.validate()
        let url = URL(string: task.source).flatMap { $0.scheme == nil ? nil : $0 } ?? URL(fileURLWithPath: task.source)
        var record = DownloadRecord(url: url, folder: folder, fileName: task.metadata?.name)
        record.torrent = task; record.size = task.selectedSize; record.queueID = queueID
        record.speedLimitKBps = limit; record.status = start ? .queued : .paused
        items.append(record); persist(); schedule()
    }

    func updateTorrent(_ id: UUID, task: TorrentTask, limit: Int) throws {
        _ = try task.selectedPriorities(); try task.options.validate()
        guard let index = items.firstIndex(where: { $0.id == id }) else { return }
        // Editing is allowed once the native worker has fully paused and flushed its checkpoint.
        guard transfers[id] == nil else { pause(id); throw TorrentFailure("Pausing and saving checkpoint. Open settings again after the task stops.") }
        var updated = task
        if updated.priorities != items[index].torrent?.priorities { updated.notified = false; items[index].finished = nil; items[index].savedPath = nil }
        items[index].torrent = updated; items[index].size = updated.selectedSize
        items[index].speedLimitKBps = limit; items[index].status = .paused
        persist()
    }

    // MARK: after a download

    private func finished(_ rec: DownloadRecord) {
        guard let path = rec.savedPath else { return }
        let file = URL(fileURLWithPath: path)
        if settings.markDownloads { markAsDownloaded(file, from: rec.url) }
        if settings.playSound { NSSound(named: "Glass")?.play() }
        var body = rec.fileName
        if let c = rec.checksumResult { body += " – \(c)" }
        post("Download finished", body)
        if settings.scanEnabled { scan(file) }
        if settings.showCompleteDialog { CompleteWindow.show(rec, manager: self) }
    }

    /// Lets macOS check the file the first time it is opened, as it does for browser downloads.
    private func markAsDownloaded(_ file: URL, from source: URL) {
        var values = URLResourceValues()
        values.quarantineProperties = [
            kLSQuarantineAgentNameKey as String: "Nexonic Download Manager",
            kLSQuarantineTypeKey as String: kLSQuarantineTypeWebDownload as String,
            kLSQuarantineDataURLKey as String: source.absoluteString,
        ]
        var f = file
        try? f.setResourceValues(values)
    }

    private func scan(_ file: URL) {
        var words = CommandLineTool.words(settings.scanCommand)
        guard !words.isEmpty else { return }
        let exe = words.removeFirst()
        let args = words + [file.path]
        let name = file.lastPathComponent
        DispatchQueue.global().async {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: exe)
            p.arguments = args
            let ok: Bool
            do {
                try p.run()
                p.waitUntilExit()
                ok = p.terminationStatus == 0
            } catch {
                ok = false
            }
            Task { @MainActor in
                self.post(ok ? "Virus scan passed" : "Virus scan warning",
                          ok ? name : "\(name): the scanner reported a problem or could not run")
            }
        }
    }

    private func run(_ path: String, _ args: [String]) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        try? p.run()
    }

    // MARK: clipboard

    private func watchClipboard() {
        let pb = NSPasteboard.general
        guard pb.changeCount != clipboardCount else { return }
        clipboardCount = pb.changeCount
        guard settings.monitorClipboard, addRequest == nil, let text = pb.string(forType: .string),
              let url = FileTypes.downloadLink(in: text, types: FileTypes.parse(settings.clipboardTypes)),
              !items.contains(where: { $0.url == url }) else { return }
        addRequest = AddRequest(links: url.absoluteString)
        NSApp.activate(ignoringOtherApps: true)
    }

    // MARK: timer

    private func update(_ id: UUID, _ change: (inout DownloadRecord) -> Void) {
        guard let i = items.firstIndex(where: { $0.id == id }) else { return }
        change(&items[i])
        persist()
    }

    private func tick() {
        ticks += 1
        var s: [UUID: Double] = [:]
        // Averaged over the last 6 seconds: video sites deliver in bursts, so a single tick is often 0.
        for item in items where item.status.isActive {
            var h = recent[item.id] ?? []
            h.append(item.received)
            if h.count > 13 { h.removeFirst(h.count - 13) }
            recent[item.id] = h
            if let first = h.first, let last = h.last, h.count > 1 {
                s[item.id] = max(0, Double(last - first) / (Double(h.count - 1) * 0.5))
            }
        }
        let activeIDs = Set(items.filter { $0.status.isActive }.map(\.id))
        recent = recent.filter { activeIDs.contains($0.key) }
        speeds = s
        let active = items.filter { $0.status.isActive }.count
        NSApp.dockTile.badgeLabel = active > 0 ? "\(active)" : nil
        watchClipboard()
        if ticks % 2 == 0 {
            collectInbox()
            runTimetable()
            checkQueuesDone()
        }
        if ticks % 4 == 0 { persist() }
    }

    func eta(_ item: DownloadRecord) -> String {
        guard let size = item.size, let speed = speeds[item.id], speed > 0 else { return "" }
        let seconds = max(0, Double(size - item.received) / speed)  // never negative when a size was only an estimate
        if seconds > 99 * 24 * 3600 { return "more than 99 days" }
        let f = DateComponentsFormatter()
        f.allowedUnits = seconds > 3600 ? [.hour, .minute] : [.minute, .second]
        f.unitsStyle = .abbreviated
        return f.string(from: seconds) ?? ""
    }

    // MARK: notifications and storage

    func post(_ title: String, _ body: String) {
        guard settings.notifications else { return }
        let c = UNMutableNotificationContent()
        c.title = title
        c.body = body
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: c, trigger: nil))
    }

    // Secrets live only in the private secrets file, not in the lists. If it can't be written, the values from 0.6.1
    // files stay where they were and new ones are kept while the app runs; it is tried again on the next save.
    private func persist() {
        var list = Secrets.stripped(items)
        if !syncSecrets() {
            var s = AppSettings()
            Secrets.fill(&list, &s, from: legacySecrets)
        }
        save(list, to: AppPaths.downloads)
    }

    // MARK: backup

    /// Settings, queues and the list as a backup file (passwords and cookies are left out).
    func exportBackup(version: String) throws -> Data {
        try Backup.export(settings: settings, queues: queues, downloads: items, app: version)
    }

    /// Adds the backup's queues and downloads that are not here yet; optionally takes its settings. Returns how many downloads were added.
    func importBackup(_ data: Data, settingsToo: Bool) throws -> Int {
        let file = try Backup.parse(data)
        for q in file.queues ?? [] where !q.isMain && !queues.contains(where: { $0.id == q.id }) {
            var q = q
            q.running = false
            queues.append(q)
        }
        var added = Backup.newDownloads(file, existing: items)
        for i in added.indices where added[i].queueID != nil && !queues.contains(where: { $0.id == added[i].queueID }) {
            added[i].queueID = nil
        }
        items.append(contentsOf: added)
        if settingsToo, var s = file.settings {
            // Passwords are not in backups: keep the ones saved here.
            s.proxy.password = settings.proxy.password
            for i in s.hostProfiles.indices {
                if let mine = settings.hostProfiles.first(where: { HostProfiles.normalize($0.host) == HostProfiles.normalize(s.hostProfiles[i].host) }) {
                    s.hostProfiles[i].password = mine.password
                    if s.hostProfiles[i].proxy != nil { s.hostProfiles[i].proxy?.password = mine.proxy?.password ?? "" }
                }
            }
            settings = s
        }
        persist()
        schedule()
        return added.count
    }

    private func saveSettings() {
        var s = Secrets.stripped(settings)
        if !syncSecrets() {
            var none: [DownloadRecord] = []
            Secrets.fill(&none, &s, from: legacySecrets)
        }
        save(s, to: AppPaths.settings)
    }

    /// Writes the current secrets to the private file when they changed. False when it can't be read or written.
    private func syncSecrets() -> Bool {
        var recovered = false
        if !vaultReadable, let saved = Self.vault.read() {  // try again quietly
            vaultReadable = true
            recovered = true
            stored = saved
            // Put the recovered values back where they are missing (the settings change saves itself afterwards).
            var list = items
            var s = settings
            Secrets.fill(&list, &s, from: saved)
            items = list
            if s.proxy.password != settings.proxy.password {
                let password = s.proxy.password
                Task { @MainActor [weak self] in self?.settings.proxy.password = password }
            }
        }
        guard vaultReadable else { return false }
        var all = Secrets.collect(items, settings)
        // The unreadable list's secrets stay recoverable; recovered ones are never replaced by values not yet filled in.
        if listDamaged || recovered { all = stored.merging(all) { _, new in new } }
        if all == stored { legacySecrets = [:]; return true }
        guard Self.vault.write(all) else { return false }
        stored = all
        legacySecrets = [:]  // migrated: the files are written without them from now on
        return true
    }

    private func save<T: Encodable>(_ value: T, to url: URL) {
        guard let data = try? JSONEncoder().encode(value) else { return }
        try? data.write(to: url, options: .atomic)
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)  // may hold passwords
    }

    private static func load<T: Decodable>(_ type: T.Type, from url: URL) -> T? { SavedFile.load(type, from: url) }
}

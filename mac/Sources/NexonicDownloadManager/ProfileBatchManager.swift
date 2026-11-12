import Foundation
import NexonicCore

/// A profile listed in the background with the browser's cookies (the extension's Download all).
struct ProfileListing {
    /// Stop, from the window; read by the listing on its own thread.
    final class StopFlag: @unchecked Sendable {
        private let lock = NSLock()
        private var value = false
        var isSet: Bool { lock.lock(); defer { lock.unlock() }; return value }
        func set() { lock.lock(); value = true; lock.unlock() }
    }
    var url: String
    var running = false
    let stop = StopFlag()
    var stopped = false
    var visitorsOnly = false
    var found = 0
    /// What was found so far (in order), and whose it is.
    var entries: [BatchEntry] = []
    var owner = ""
    /// Download Now or Add to Queue while the search runs: everything found so far, then each new file as it is found.
    var follow: Follow?
    /// Why the signed-in search found nothing, in the gallery engine's own words.
    var error: String?
    struct Follow { var folder: URL; var quality: String; var parallel: Int; var start: Bool }
    var scan: ProfileBatch.Scan?
}

/// Profile Batch: found videos go into a queue of their own; saved profiles are checked for new ones.
extension DownloadManager {
    /// Adds the chosen videos to the queue `owner` (its own limit), in `quality`, into `folder`; starts it when `start`.
    @discardableResult
    func queueBatch(owner: String, _ take: [BatchEntry], folder: URL, quality: String, maxActive: Int, start: Bool) -> Int {
        let (height, audio) = ProfileBatch.quality(quality)
        let qid = queueID(named: owner.isEmpty ? "Profile Batch" : owner)
        if let i = queues.firstIndex(where: { $0.id == qid }), !queues[i].isMain {
            queues[i].maxActive = max(1, min(10, maxActive))
            queues[i].running = start
        }
        var n = 0
        for e in take {
            var job = DownloadJob(url: e.url)
            job.site = e.direct != true  // a gallery engine listing gives the file itself
            job.maxHeight = audio ? nil : height
            job.audioOnly = audio
            job.fileName = e.title.isEmpty || e.title == e.url ? nil : e.title
            var o = AddOptions()
            o.folder = folder; o.queueID = qid; o.start = start; o.rememberFolder = false
            if e.direct != true { o.audioOnly = audio; o.maxHeight = height }
            if add(job, options: o) != nil { n += 1 }
        }
        return n
    }

    /// Starts searching a profile with the gallery engine and the browser's cookies (the extension's Download all), in
    /// the background, as a row in the downloads list: several can run at once, the Profile Batch window may close, and
    /// double-clicking the row opens it again. Files show up as they are found, and Download Now or Add to Queue can
    /// start them before the search ends (see follow).
    func startListing(_ url: String, max: Int) {
        let key = ProfileBatch.key(url)
        guard let signedIn = profileCookies[key], listings[key]?.running != true else { return }
        let proxy = settings.proxy
        var listing = ProfileListing(url: url, running: true)
        listing.owner = SocialPages.find(url)?.owner ?? ""
        listings[key] = listing
        let stop = listing.stop
        // Its row: "Photos and videos of <name>", in the list until removed.
        let name = SocialPages.find(url)?.owner ?? URL(string: url).map { ($0.host ?? "") + $0.path } ?? url
        let row: UUID? = URL(string: url).map { u in
            var r = DownloadRecord(url: u, folder: settings.folder, fileName: String(format: String(localized: "Photos and videos of %@"), name))
            r.listing = true; r.status = .probing; r.streamDone = 0
            putListingRow(r)
            return r.id
        }
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result {
                try GalleryDl.list(SocialPages.listable(url), cookies: signedIn.cookies, userAgent: signedIn.userAgent, proxy: proxy, max: max,
                                   found: { fresh, owner in Task { @MainActor in self.listingFound(key, row: row, fresh, owner: owner) } },
                                   stop: { stop.isSet })
            }
            Task { @MainActor in
                var l = self.listings[key] ?? ProfileListing(url: url)
                l.running = false
                switch result {
                case .success(let listed):
                    let owner = l.owner.isEmpty ? listed.owner : l.owner
                    let found = listed.entries
                    let videos = found.filter { $0.kind == "video" }.count
                    l.scan = SocialPages.scan(url: url, owner: owner, entries: found,
                        note: String(format: String(localized: "Found %d videos and %d photos with your browser's sign-in."), videos, found.count - videos))
                    if let row { self.update(row) { $0.status = .finished; $0.streamDone = found.count; $0.error = nil } }
                    self.post(String(localized: "Profile Batch"), (owner.isEmpty ? "" : owner + ": ")
                              + String(format: String(localized: "%d videos and %d photos found."), videos, found.count - videos))
                case .failure(is CancellationError):
                    l.stopped = true
                    if let row { self.update(row) { $0.status = .paused } }
                case .failure(let e):
                    // The engine couldn't search this site or address: Profile Batch takes what visitors see, and says why.
                    l.visitorsOnly = true
                    l.error = e.localizedDescription
                    self.profileCookies[key] = nil
                    if let row { self.update(row) { $0.status = .failed; $0.error = String(localized: "Signed-in search didn't work; double-click for what visitors see.") } }
                }
                self.listings[key] = l
            }
        }
    }

    /// New files from a running search: counted on its row, and queued at once when the user already chose Download Now
    /// or Add to Queue.
    private func listingFound(_ key: String, row: UUID?, _ fresh: [BatchEntry], owner: String) {
        guard var l = listings[key] else { return }
        if l.owner.isEmpty { l.owner = owner }
        l.entries += fresh
        l.found = l.entries.count
        listings[key] = l
        if let row { update(row) { $0.streamDone = l.found; $0.status = .downloading } }
        if let f = l.follow { queueFound(fresh, of: l, follow: f) }
    }

    /// Download Now or Add to Queue during a search: what was found so far now, and the rest as it is found.
    func followListing(_ key: String, folder: URL, quality: String, parallel: Int, start: Bool) {
        guard var l = listings[key] else { return }
        let f = ProfileListing.Follow(folder: folder, quality: quality, parallel: parallel, start: start)
        l.follow = f
        listings[key] = l
        queueFound(l.entries, of: l, follow: f)
    }

    private func queueFound(_ entries: [BatchEntry], of l: ProfileListing, follow f: ProfileListing.Follow) {
        let take = entries.filter { !alreadyDownloaded($0.url) }
        if !take.isEmpty { queueBatch(owner: l.owner, take, folder: f.folder, quality: f.quality, maxActive: f.parallel, start: f.start) }
    }

    func alreadyDownloaded(_ url: String) -> Bool {
        let k = ProfileBatch.key(url)
        return items.contains { ProfileBatch.key($0.url.absoluteString) == k }
    }

    func saveProfiles() { SavedProfiles.save(profiles) }

    func checkProfiles() {
        let now = Date()
        for p in profiles where p.due(now) && !checkingProfiles.contains(p.id) {
            checkingProfiles.insert(p.id)
            let proxy = settings.proxy
            DispatchQueue.global(qos: .utility).async {
                let result = Result { try ProfileBatch.discover(p.url, proxy: proxy, max: p.filter.max, fresh: true) }
                Task { @MainActor in
                    self.checkingProfiles.remove(p.id)
                    guard let i = self.profiles.firstIndex(where: { $0.id == p.id }) else { return }
                    self.profiles[i].lastChecked = Date()
                    switch result {
                    case .failure(let e): self.profiles[i].lastResult = e.localizedDescription
                    case .success(let scan):
                        var seen = Set(p.seen)
                        var (take, counts) = ProfileBatch.choose(scan.entries, p.filter, alreadyDownloaded: self.alreadyDownloaded, seen: seen)
                        counts.queued = take.isEmpty ? 0 : self.queueBatch(owner: p.title.isEmpty ? scan.owner : p.title, take,
                            folder: URL(fileURLWithPath: p.folder, isDirectory: true), quality: p.quality, maxActive: 2, start: true)
                        for e in scan.entries where seen.insert(ProfileBatch.key(e.url)).inserted { self.profiles[i].seen.append(ProfileBatch.key(e.url)) }
                        self.profiles[i].lastResult = counts.text
                        if counts.queued > 0 { self.post(String(localized: "New videos"), "\(p.title): \(counts.queued)") }
                        _ = take
                    }
                    self.saveProfiles()
                }
            }
        }
    }
}

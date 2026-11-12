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
            o.folder = folder; o.queueID = qid; o.start = start
            if e.direct != true { o.audioOnly = audio; o.maxHeight = height }
            if add(job, options: o) != nil { n += 1 }
        }
        return n
    }

    /// Starts listing a profile with the gallery engine and the browser's cookies (the extension's Download all), in the
    /// background: the Profile Batch window may close meanwhile. When it is done Nexonic says so and opens Profile Batch
    /// with what was found (or with what visitors see, when the engine can't read the site).
    func startListing(_ url: String, max: Int) {
        let key = ProfileBatch.key(url)
        guard let signedIn = profileCookies[key], listings[key]?.running != true else { return }
        let proxy = settings.proxy
        let listing = ProfileListing(url: url, running: true)
        listings[key] = listing
        let stop = listing.stop
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { try GalleryDl.list(SocialPages.listable(url), cookies: signedIn.cookies, userAgent: signedIn.userAgent, proxy: proxy,
                                                     max: max,
                                                     found: { n in Task { @MainActor in self.listings[key]?.found = n } }, stop: { stop.isSet }) }
            Task { @MainActor in
                var l = self.listings[key] ?? ProfileListing(url: url)
                l.running = false
                switch result {
                case .success(let listed?):
                    // The profile's name (its folder): Instagram's or Facebook's owner, else the account the files belong to.
                    let owner = SocialPages.find(url)?.owner ?? listed.owner
                    let found = listed.entries
                    let videos = found.filter { $0.kind == "video" }.count
                    l.scan = SocialPages.scan(url: url, owner: owner, entries: found,
                        note: String(format: String(localized: "Found %d videos and %d photos with your browser's sign-in."), videos, found.count - videos))
                    self.post(String(localized: "Profile Batch"), (owner.isEmpty ? "" : owner + ": ")
                              + String(format: String(localized: "%d videos and %d photos found"), videos, found.count - videos))
                case .failure(is CancellationError):
                    l.stopped = true
                default:
                    l.visitorsOnly = true  // the engine can't read this site or address
                    self.profileCookies[key] = nil
                }
                self.listings[key] = l
                if !l.stopped { MediaWindows.showPlaylist(url, replace: !MediaWindows.isShowing(url)) }
            }
        }
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

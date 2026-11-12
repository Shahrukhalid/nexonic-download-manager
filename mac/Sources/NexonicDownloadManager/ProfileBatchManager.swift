import Foundation
import NexonicCore

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

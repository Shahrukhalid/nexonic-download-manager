import NexonicKit
import SwiftUI

/// Screenshots only (debug builds, like the desktop NDM_DEMO and the Android demo): a sample list, one real running
/// download from scripts/demo-server.py on this Mac (the simulator shares its network) and the screen to show.
/// Launched by ios/take-screenshots.sh with `-ndm_demo 1 -ndm_screen <name>`.
enum Demo {
    static var active: Bool {
        #if DEBUG
        return UserDefaults.standard.string(forKey: "ndm_demo") == "1"
        #else
        return false
        #endif
    }

    static func runIfRequested(_ model: AppModel) {
        guard active else { return }
        seed()
        model.refresh()
        let screen = UserDefaults.standard.string(forKey: "ndm_screen") ?? "downloads"
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
            switch screen {
            case "detail": model.selection = model.jobs.first { $0.status == .downloading }?.id
            case "add": model.addInput = DownloadInput(urls: [URL(string: "http://127.0.0.1:8099/studio-suite-2026-installer.zip")!])
            case "browser", "found":
                model.tab = .browser
                model.browserURL = URL(string: "http://127.0.0.1:8098/page.html")
                if screen == "found" { DispatchQueue.main.asyncAfter(deadline: .now() + 6) { model.demoShowFound = true } }
            case "settings": model.tab = .settings
            case "setup": model.showSetup = true
            default: break
            }
        }
    }

    private static func seed() {
        let now = Date()
        func job(_ name: String, _ size: Int64, _ status: JobStatus, done: Double, hoursAgo: Double) -> DownloadJob {
            var j = DownloadJob(url: URL(string: "https://example.com/files/\(name)")!, fileName: name, status: status, size: size)
            j.written = status == .complete ? size : Int64(Double(size) * done)
            j.addedAt = now.addingTimeInterval(-hoursAgo * 3600)
            if status == .complete {
                j.finishedAt = j.addedAt.addingTimeInterval(600)
                j.savedPath = (j.category.folder.map { $0 + "/" } ?? "") + name
            }
            return j
        }
        let sample = [
            job("nature-documentary-4k.mkv", 6_200_000_000, .paused, done: 0.37, hoursAgo: 2),
            job("dataset-2026.tar.gz", 3_900_000_000, .later, done: 0, hoursAgo: 3),
            job("course-lecture-08-1080p.mp4", 1_420_000_000, .complete, done: 1, hoursAgo: 6),
            job("project-assets.zip", 824_000_000, .complete, done: 1, hoursAgo: 20),
            job("podcast-episode-42.mp3", 96_300_000, .complete, done: 1, hoursAgo: 26),
            job("design-handbook.pdf", 18_400_000, .complete, done: 1, hoursAgo: 30),
        ]
        Store.shared.update { $0 = sample }
        // One real download, so speed and time left are real.
        let live = DownloadJob(url: URL(string: "http://127.0.0.1:8099/conference-keynote-1080p.mp4")!,
                               fileName: "conference-keynote-1080p.mp4", size: 943_718_400)
        Library.add(live, startNow: true, sessionID: Downloader.appSessionID)
    }
}

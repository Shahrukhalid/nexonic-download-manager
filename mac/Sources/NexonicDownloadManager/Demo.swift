import AppKit
import NexonicCore

/// Fills the app with a sample list and saves pictures of its windows, for the README screenshots.
/// Only active when started with NDM_DEMO=1; NDM_SNAPSHOT=<folder> saves the pictures and quits,
/// NDM_SHEET=add|scheduler|batch|info|progress opens that window first, NDM_LIVE=<link> adds one real running download.
@MainActor
enum Demo {
    static let nightQueueID = "demo-night"
    static let liveName = "conference-keynote-1080p.mp4"
    /// What the browser hands over in the download-window picture.
    static var infoJob: DownloadJob {
        var j = DownloadJob(url: "https://example.com/releases/studio-suite-2026-installer.dmg")
        j.size = 1_840_000_000
        j.fromBrowser = true
        return j
    }
    private static var started = false
    private static var env: [String: String] { ProcessInfo.processInfo.environment }

    static func start(_ manager: DownloadManager, open: @escaping (String) -> Void) {
        guard env["NDM_DEMO"] == "1", !started else { return }
        started = true
        if env["NDM_APPEARANCE"] == "dark" { NSApp.appearance = NSAppearance(named: .darkAqua) }
        else { NSApp.appearance = NSAppearance(named: .aqua) }
        seed(manager)
        let sheet = env["NDM_SHEET"]
        let dir = env["NDM_SNAPSHOT"]
        let name = env["NDM_NAME"] ?? "main"
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 1_000_000_000)
            NSApp.activate(ignoringOtherApps: true)
            if let w = NSApp.windows.first(where: { $0.isVisible && $0.contentView != nil && !($0 is NSPanel) }) {
                w.setFrame(NSRect(x: 80, y: 80, width: 1240, height: 720), display: true)
                w.makeKeyAndOrderFront(nil)
            }
            if let sheet { open(sheet) }
            guard let dir else { return }
            try? await Task.sleep(nanoseconds: 5_000_000_000)
            snapshot(to: URL(fileURLWithPath: dir), name: name)
            NSApp.terminate(nil)
        }
    }

    private static func seed(_ m: DownloadManager) {
        let folder = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask)[0]
        func rec(_ name: String, _ size: Int64, _ status: DownloadStatus, done: Double, parts: Int = 8, queue: String? = nil,
                 hoursAgo: Double = 1) -> DownloadRecord {
            var r = DownloadRecord(url: URL(string: "https://example.com/files/\(name)")!, folder: folder, fileName: name)
            r.size = size
            r.acceptsRanges = true
            r.status = status
            r.queueID = queue
            r.added = Date().addingTimeInterval(-hoursAgo * 3600)
            let each = size / Int64(parts)
            r.segments = (0..<parts).map { i in
                let start = Int64(i) * each
                let end = i == parts - 1 ? size - 1 : start + each - 1
                let len = end - start + 1
                let share = done >= 1 ? 1 : max(0, min(1, done * Double(parts) - Double(i) * 0.6))
                return Segment(id: i, start: start, end: end, written: Int64(Double(len) * share))
            }
            if status == .finished {
                r.savedPath = folder.appendingPathComponent(name).path
                r.finished = r.added.addingTimeInterval(600)
            }
            return r
        }
        var records = [
            rec("design-handbook.pdf", 18_400_000, .finished, done: 1, parts: 1, hoursAgo: 30),
            rec("podcast-episode-42.mp3", 96_300_000, .finished, done: 1, hoursAgo: 26),
            rec("project-assets.zip", 824_000_000, .finished, done: 1, hoursAgo: 20),
            rec("course-lecture-08-1080p.mp4", 1_420_000_000, .finished, done: 1, hoursAgo: 6),
            rec("dataset-2026.tar.gz", 3_900_000_000, .queued, done: 0, queue: nightQueueID, hoursAgo: 3),
            rec("nature-documentary-4k.mkv", 6_200_000_000, .paused, done: 0.37, hoursAgo: 2),
            rec("photo-library-backup.zip", 2_100_000_000, .paused, done: 0.62, hoursAgo: 1.5),
        ]
        records[0].checksum = "sha256:demo"
        records[0].checksumResult = "SHA-256 matches"
        if let live = env["NDM_LIVE"], let url = URL(string: live) {
            var r = DownloadRecord(url: url, folder: folder.appendingPathComponent("NDM Demo"), fileName: liveName)
            r.added = Date()
            records.append(r)
            var s = m.settings
            s.speedLimitKBps = Int(env["NDM_LIMIT"] ?? "") ?? s.speedLimitKBps
            m.settings = s
        }
        var night = DownloadQueue(id: nightQueueID, name: "Night", running: false, maxActive: 2)
        night.startEnabled = true
        night.startTime = "01:30"
        night.stopEnabled = true
        night.stopTime = "07:00"
        night.days = [1, 2, 3, 4, 5]
        night.afterDone = .sleep
        m.loadDemo(records, queues: [night])
    }

    private static func image(of window: NSWindow) -> NSBitmapImageRep? {
        guard let view = window.contentView?.superview ?? window.contentView,
              let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return nil }
        view.cacheDisplay(in: view.bounds, to: rep)
        return rep
    }

    /// Saves the main window, with any open sheet drawn where it sits, as <name>.png.
    private static func snapshot(to dir: URL, name: String) {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        guard let main = NSApp.windows.first(where: { $0.isVisible && $0.sheetParent == nil && $0.frame.width > 600 }),
              let base = image(of: main) else { return }
        let size = NSSize(width: base.pixelsWide, height: base.pixelsHigh)
        let scale = CGFloat(base.pixelsWide) / main.frame.width
        let out = NSImage(size: size)
        out.lockFocus()
        base.draw(in: NSRect(origin: .zero, size: size))
        // A separate window (download or progress window) is drawn centred over the main window.
        if main.attachedSheet == nil,
           let panel = NSApp.windows.first(where: { $0 !== main && $0.isVisible && (400...720).contains($0.frame.width) }),
           let p = image(of: panel) {
            NSColor.black.withAlphaComponent(0.18).setFill()
            NSRect(origin: .zero, size: size).fill(using: .sourceAtop)
            let w = CGFloat(p.pixelsWide), h = CGFloat(p.pixelsHigh)
            p.draw(in: NSRect(x: (size.width - w) / 2, y: (size.height - h) / 2, width: w, height: h))
        }
        if let sheet = main.attachedSheet, let s = image(of: sheet) {
            NSColor.black.withAlphaComponent(0.12).setFill()
            NSRect(origin: .zero, size: size).fill(using: .sourceAtop)
            let x = (sheet.frame.minX - main.frame.minX) * scale
            let y = (sheet.frame.minY - main.frame.minY) * scale
            s.draw(in: NSRect(x: x, y: y, width: CGFloat(s.pixelsWide), height: CGFloat(s.pixelsHigh)))
        }
        out.unlockFocus()
        guard let tiff = out.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff),
              let png = rep.representation(using: .png, properties: [:]) else { return }
        try? png.write(to: dir.appendingPathComponent("\(name).png"))
    }
}

import AppIntents
import NexonicKit

/// "Download with Nexonic" in Shortcuts and Siri. Takes one or more links (or a Shortcut's input from the Share sheet),
/// starts them in the background and puts the files in Files › Nexonic. Web pages: the first video or file on the page.
struct DownloadWithNexonicIntent: AppIntent {
    static var title: LocalizedStringResource = "Download with Nexonic"
    static var description = IntentDescription("Downloads links with Nexonic. Finished files appear in Files › Nexonic.")
    static var openAppWhenRun = false

    @Parameter(title: "Links", inputConnectionBehavior: .connectToPreviousIntentResult)
    var links: [URL]

    @Parameter(title: "Start Now", default: true)
    var startNow: Bool

    static var parameterSummary: some ParameterSummary {
        Summary("Download \(\.$links) with Nexonic") { \.$startNow }
    }

    func perform() async throws -> some IntentResult & ReturnsValue<Int> & ProvidesDialog {
        let web = links.compactMap { Net.webURL($0.absoluteString) }.map { Links.unwrap($0) }
        guard !web.isEmpty else { throw $links.needsValueError("Which link should Nexonic download?") }
        var started = 0
        for url in web {
            var target = url
            var probe = try? await Probe.run(url)
            if let p = probe, p.isPage, let first = await Probe.scan(page: p.finalURL).first {
                target = first.url
                probe = try? await Probe.run(first.url)
            }
            if probe?.isPage == true { continue } // a page with nothing to download
            let job = DownloadJob(url: probe?.finalURL ?? target, fileName: probe?.fileName ?? FileNames.fromURL(target),
                                  size: probe?.size, referer: target == url ? nil : url)
            Library.add(job, startNow: startNow, sessionID: Downloader.appSessionID)
            started += 1
        }
        if started == 0 { return .result(value: 0, dialog: "No file or video to download was found.") }
        let what = started == 1 ? "1 download" : "\(started) downloads"
        let text = startNow ? "\(what) started in Nexonic." : "\(what) saved for later in Nexonic."
        return .result(value: started, dialog: "\(text)")
    }
}

/// "Pause Nexonic Downloads": stops every running download (they continue later where they stopped).
struct PauseAllDownloadsIntent: AppIntent {
    static var title: LocalizedStringResource = "Pause Nexonic Downloads"
    static var description = IntentDescription("Pauses every download that is running in Nexonic.")
    static var openAppWhenRun = false

    func perform() async throws -> some IntentResult & ReturnsValue<Int> & ProvidesDialog {
        let active = Store.shared.load().filter(\.isActive)
        for job in active { Downloader.shared(job.sessionID ?? Downloader.appSessionID).pause(job.id) }
        let text = active.isEmpty ? "Nothing is downloading." : active.count == 1 ? "1 download paused." : "\(active.count) downloads paused."
        return .result(value: active.count, dialog: "\(text)")
    }
}

/// "Resume Nexonic Downloads": starts paused, failed and saved-for-later downloads.
struct ResumeAllDownloadsIntent: AppIntent {
    static var title: LocalizedStringResource = "Resume Nexonic Downloads"
    static var description = IntentDescription("Starts the paused, failed and saved-for-later downloads in Nexonic.")
    static var openAppWhenRun = false

    func perform() async throws -> some IntentResult & ReturnsValue<Int> & ProvidesDialog {
        let waiting = Store.shared.load().filter { $0.status == .paused || $0.status == .failed || $0.status == .later }
        for job in waiting {
            var j = job
            j.status = .queued
            Downloader.shared(Downloader.appSessionID).start(j)
        }
        let text = waiting.isEmpty ? "Nothing is waiting." : waiting.count == 1 ? "1 download started." : "\(waiting.count) downloads started."
        return .result(value: waiting.count, dialog: "\(text)")
    }
}

/// "Nexonic Download Status": how many downloads are running, waiting and finished.
struct DownloadStatusIntent: AppIntent {
    static var title: LocalizedStringResource = "Nexonic Download Status"
    static var description = IntentDescription("Tells how many downloads are running, waiting and finished in Nexonic.")
    static var openAppWhenRun = false

    func perform() async throws -> some IntentResult & ReturnsValue<String> & ProvidesDialog {
        let jobs = Store.shared.load()
        let running = jobs.filter(\.isActive).count
        let waiting = jobs.filter { $0.status == .paused || $0.status == .later || $0.status == .failed }.count
        let done = jobs.filter { $0.status == .complete }.count
        let text = "\(running) running, \(waiting) waiting, \(done) finished."
        return .result(value: text, dialog: "\(text)")
    }
}

struct NexonicShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(
            intent: DownloadWithNexonicIntent(),
            phrases: ["Download with \(.applicationName)", "Save a link with \(.applicationName)"],
            shortTitle: "Download Link",
            systemImageName: "arrow.down.circle"
        )
        AppShortcut(
            intent: PauseAllDownloadsIntent(),
            phrases: ["Pause \(.applicationName) downloads", "Pause downloads in \(.applicationName)"],
            shortTitle: "Pause All",
            systemImageName: "pause.circle"
        )
        AppShortcut(
            intent: ResumeAllDownloadsIntent(),
            phrases: ["Resume \(.applicationName) downloads", "Start downloads in \(.applicationName)"],
            shortTitle: "Resume All",
            systemImageName: "play.circle"
        )
        AppShortcut(
            intent: DownloadStatusIntent(),
            phrases: ["\(.applicationName) download status", "How are my \(.applicationName) downloads"],
            shortTitle: "Download Status",
            systemImageName: "list.bullet.circle"
        )
    }
}

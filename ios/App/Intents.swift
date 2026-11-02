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

struct NexonicShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(
            intent: DownloadWithNexonicIntent(),
            phrases: ["Download with \(.applicationName)", "Save a link with \(.applicationName)"],
            shortTitle: "Download Link",
            systemImageName: "arrow.down.circle"
        )
    }
}

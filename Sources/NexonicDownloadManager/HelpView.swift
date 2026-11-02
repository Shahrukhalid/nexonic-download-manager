import AppKit
import SwiftUI

/// Links shown in the About window and the Help menu.
enum AppLinks {
    static let author = "Shahrukh Khalid"
    static let website = URL(string: "https://www.shahrukhalid.com")!
    static let github = URL(string: "https://github.com/Shahrukhalid")!
    static let project = URL(string: "https://github.com/Shahrukhalid/nexonic-download-manager")!
    static let onlineHelp = URL(string: "https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/HELP.md")!
    static let releases = URL(string: "https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest")!
    static let reportProblem = URL(string: "https://github.com/Shahrukhalid/nexonic-download-manager/issues/new")!
    static let terms = URL(string: "https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/TERMS.md")!
    static let license = URL(string: "https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/LICENSE")!

    static var version: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
    }

    static func open(_ url: URL) { NSWorkspace.shared.open(url) }
}

struct HelpSection {
    let heading: String
    let text: String
}

struct HelpTopic: Identifiable, Hashable {
    let id: String
    let title: String
    let symbol: String
    let sections: [HelpSection]

    static func == (a: HelpTopic, b: HelpTopic) -> Bool { a.id == b.id }
    func hash(into h: inout Hasher) { h.combine(id) }
}

enum HelpContent {
    static let topics: [HelpTopic] = [
        HelpTopic(id: "start", title: "Getting Started", symbol: "star", sections: [
            HelpSection(heading: "Add a download", text: "Click Add URL (⌘N), paste one or more links and press Start Download. Links you copy are offered automatically when the Add window opens."),
            HelpSection(heading: "Pause and resume", text: "Select a download and press Stop or Resume. Downloads continue where they stopped, even after you quit the app or restart the Mac."),
            HelpSection(heading: "Find your files", text: "Finished files are saved in the folder chosen in the Add window (your Downloads folder by default). Double-click a download to open it, or right-click and choose Show in Finder."),
        ]),
        HelpTopic(id: "browser", title: "Browser Extension", symbol: "puzzlepiece.extension", sections: [
            HelpSection(heading: "Chrome, Edge, Brave and Opera", text: "Open the app once. Unzip NexonicExtension-Chrome.zip, open the browser's extensions page, turn on Developer mode, click Load unpacked and choose the folder. You can also save the extension folder from Settings, under Browsers & Command Line."),
            HelpSection(heading: "Firefox", text: "Drag NexonicAddon-Firefox.xpi onto Firefox. For a quick test, open about:debugging and choose Load Temporary Add-on."),
            HelpSection(heading: "Download and progress windows", text: "When you click a download link in the browser, the download window shows the file name, size, category and folder: choose Start Download, Download Later or Cancel. Each download you start gets its own progress window with the speed, time left, a speed graph and every connection. Turn either window off in Settings, General."),
            HelpSection(heading: "What it does", text: "Downloads from the browser are handed to the app automatically. Right-click a link and choose Download with Nexonic, or use the video button that appears on pages with video."),
        ]),
        HelpTopic(id: "video", title: "Saving Videos", symbol: "play.rectangle", sections: [
            HelpSection(heading: "From a website", text: "Click the video button the extension shows on the page, or paste the page link into Add URL. Videos are saved in the best available quality as .mp4."),
            HelpSection(heading: "Quality", text: "The download window and the Download this video button list the qualities the video is offered in. Pick one, or Sound only to save just the audio."),
            HelpSection(heading: "Streamed video", text: "Streams (.m3u8) are found by the extension, downloaded in parallel and joined into one .mp4. Copy-protected services are not supported."),
        ]),
        HelpTopic(id: "queues", title: "Queues and Scheduler", symbol: "calendar.badge.clock", sections: [
            HelpSection(heading: "Queues", text: "Put downloads in a queue to run them one after another or a few at a time. Right-click a download and choose Move to Queue."),
            HelpSection(heading: "Scheduler", text: "Start a queue at a set time, stop it at another, and optionally quit the app, sleep or shut down the Mac when it is done."),
        ]),
        HelpTopic(id: "tools", title: "Batch and Site Grabber", symbol: "square.stack.3d.down.right", sections: [
            HelpSection(heading: "Batch download", text: "Download a numbered series such as file01.jpg to file50.jpg by writing the link once with a pattern, for example file[01-50].jpg."),
            HelpSection(heading: "Site Grabber", text: "Enter a page address to list every file linked from it, filter by type and download the ones you check."),
        ]),
        HelpTopic(id: "settings", title: "Speed and Settings", symbol: "gearshape", sections: [
            HelpSection(heading: "Faster downloads", text: "Each file is split into parts that download at the same time. Change Connections per download in Settings (up to 32)."),
            HelpSection(heading: "Speed limit", text: "Set a limit for all downloads in Settings, or for one download in its Properties."),
            HelpSection(heading: "Proxy and sign-in", text: "Set a proxy in Settings. For password-protected downloads, add the user name and password in the Add window or under Properties."),
        ]),
        HelpTopic(id: "cli", title: "Command Line", symbol: "terminal", sections: [
            HelpSection(heading: "Install", text: "Open Settings, go to Browsers & Command Line and click Install ndm Command…. Then open Terminal and type ndm help."),
            HelpSection(heading: "Examples", text: "ndm https://example.com/file.zip adds a download. ndm https://example.com/file.zip --queue Night adds it to a queue, and ndm help lists every option."),
        ]),
        HelpTopic(id: "trouble", title: "Troubleshooting", symbol: "wrench.and.screwdriver", sections: [
            HelpSection(heading: "A link has expired", text: "Right-click the download, choose Refresh Link and paste a fresh link. The download continues without starting over."),
            HelpSection(heading: "macOS won't open the app", text: "Install or update with one Terminal command and macOS won't ask: curl -fsSL https://shahrukhalid.github.io/nexonic-download-manager/install.sh | bash — otherwise open System Settings, then Privacy & Security, and click Open Anyway. On macOS 13 and 14 you can also right-click the app and choose Open."),
            HelpSection(heading: "macOS says the app is damaged", text: "Open Terminal and run: xattr -cr \"/Applications/Nexonic Download Manager.app\" Then open the app again. This happens because the app is free and not sold through Apple."),
            HelpSection(heading: "The old icon still shows", text: "Install the new version over the old one in Applications, quit the app and open it again. Restarting the Mac refreshes the icon cache."),
            HelpSection(heading: "The browser doesn't hand over downloads", text: "Open the app once so it can register itself with your browsers, then restart the browser."),
            HelpSection(heading: "A video site stopped working", text: "The video engine updates itself every day. Try again later, and if it still fails, report the problem with the page link."),
            HelpSection(heading: "Updating the app", text: "When a new version is released, the app tells you when it opens, with what's new. Choose Update Now to install it (the app closes, updates and opens again), Remind Me Later, or Skip This Version. You can also use Check for Updates in the menu, or turn the check off in Settings."),
        ]),
        HelpTopic(id: "keys", title: "Keyboard Shortcuts", symbol: "keyboard", sections: [
            HelpSection(heading: "Downloads", text: "⌘N  Add URL\n⇧⌘P  Stop all\n⇧⌘R  Resume all\n⌘,  Settings\n⌘?  Help"),
        ]),
    ]
}

/// The in-app Help window: works offline, with a sidebar of topics.
struct HelpView: View {
    @State private var selection: HelpTopic? = HelpContent.topics.first

    var body: some View {
        NavigationSplitView {
            List(HelpContent.topics, selection: $selection) { topic in
                Label(topic.title, systemImage: topic.symbol).tag(topic)
            }
            .navigationSplitViewColumnWidth(min: 190, ideal: 210)
        } detail: {
            ScrollView {
                if let topic = selection {
                    VStack(alignment: .leading, spacing: 18) {
                        Label(topic.title, systemImage: topic.symbol).font(.largeTitle.weight(.semibold))
                        ForEach(topic.sections, id: \.heading) { section in
                            VStack(alignment: .leading, spacing: 6) {
                                Text(section.heading).font(.headline)
                                Text(section.text).foregroundStyle(.secondary).textSelection(.enabled)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                        }
                        Divider().padding(.top, 8)
                        HStack(spacing: 16) {
                            Button("Online Help") { AppLinks.open(AppLinks.onlineHelp) }
                            Button("Report a Problem") { AppLinks.open(AppLinks.reportProblem) }
                            Button("Check for Updates") { Updater.check(manual: true) }
                        }
                        .buttonStyle(.link)
                    }
                    .padding(28)
                    .frame(maxWidth: 640, alignment: .leading)
                }
            }
        }
        .frame(minWidth: 720, minHeight: 460)
    }
}

/// A designed About window with version, author and links.
struct AboutView: View {
    var body: some View {
        VStack(spacing: 10) {
            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 96, height: 96)
            Text("Nexonic Download Manager").font(.title2.weight(.semibold))
            Text("Version \(AppLinks.version)").foregroundStyle(.secondary)
            Text("Free, open-source download manager for Mac and Windows.")
                .font(.callout).foregroundStyle(.secondary).multilineTextAlignment(.center)
            HStack(spacing: 4) {
                Text("Made by")
                Link(AppLinks.author, destination: AppLinks.website)
            }
            .padding(.top, 6)
            HStack(spacing: 14) {
                Link("Website", destination: AppLinks.website)
                Link("GitHub", destination: AppLinks.github)
                Link("Source Code", destination: AppLinks.project)
                Link("Report a Problem", destination: AppLinks.reportProblem)
            }
            .font(.callout)
            Divider().padding(.vertical, 4)
            HStack(spacing: 4) {
                Text("© 2026 \(AppLinks.author).")
                Link("MIT License", destination: AppLinks.license)
                Text("·")
                Link("Terms of Use", destination: AppLinks.terms)
            }
            .font(.caption).foregroundStyle(.secondary)
            Button("Check for Updates") { Updater.check(manual: true) }.padding(.top, 4)
        }
        .padding(28)
        .frame(width: 420)
    }
}

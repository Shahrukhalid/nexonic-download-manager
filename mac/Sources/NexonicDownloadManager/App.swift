import AppKit
import NexonicCore
import SwiftUI

@main
struct NexonicDownloadManagerApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var manager = DownloadManager.shared

    init() { HostInstaller.install() }

    var body: some Scene {
        // A single main window: links from the browser are added to it instead of opening another window.
        Window("Nexonic Download Manager", id: "main") {
            ContentView()
                .environmentObject(manager)
                .frame(minWidth: 900, minHeight: 480)
        }
        .commands {
            AppCommands()
            MainMenus()
        }
        Settings {
            SettingsView().environmentObject(manager)
        }
        Window("About Nexonic Download Manager", id: "about") {
            AboutView()
        }
        .windowResizability(.contentSize)
        Window("Nexonic Download Manager Help", id: "help") {
            HelpView()
        }
        MenuBarExtra {
            MenuBarView().environmentObject(manager)
        } label: {
            Image(nsImage: Self.menuBarIcon)
        }
    }
    private static var menuBarIcon: NSImage {
        let image = NSImage(named: "MenuBarIcon") ?? NSApp.applicationIconImage ?? NSImage()
        image.size = NSSize(width: 18, height: 18)
        image.isTemplate = true
        return image
    }

}

/// Links from the browser helper (nexonicdm://inbox, nexonicdm://show) are handled here, so a download from the
/// browser is added to the running app without opening or fronting a window unless the user wants that.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        Updater.start()
    }

    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls {
            if TorrentInbox.shared.accept(url) {  // a .torrent file opened in Finder or a magnet link
                if let show = DownloadManager.showMainWindow { show() } else { NSApp.activate(ignoringOtherApps: true) }
            } else if url.host == "show" {
                if let show = DownloadManager.showMainWindow { show() } else { NSApp.activate(ignoringOtherApps: true) }
            } else {
                DownloadManager.shared.collectInbox()
            }
        }
    }
}

/// Replaces the standard About panel and the empty Help menu.
struct AppCommands: Commands {
    @Environment(\.openWindow) private var openWindow

    var body: some Commands {
        CommandGroup(replacing: .appInfo) {
            Button("About Nexonic Download Manager") { show("about") }
            Button("Check for Updates…") { Updater.check(manual: true) }
        }
        CommandGroup(replacing: .help) {
            Button("Nexonic Download Manager Help") { show("help") }.keyboardShortcut("?", modifiers: .command)
            Button("Online Help") { AppLinks.open(AppLinks.onlineHelp) }
            Divider()
            Button("Report a Problem…") { AppLinks.open(AppLinks.reportProblem) }
            Button("Release Notes") { AppLinks.open(AppLinks.releases) }
            Divider()
            Button("Website") { AppLinks.open(AppLinks.website) }
            Button("Source Code on GitHub") { AppLinks.open(AppLinks.project) }
            Button("Terms of Use and License") { AppLinks.open(AppLinks.terms) }
        }
    }

    private func show(_ id: String) {
        openWindow(id: id)
        NSApp.activate(ignoringOtherApps: true)
    }
}

struct MenuBarView: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        let active = manager.items.filter { $0.status.isActive }
        if active.isEmpty { Text("No active downloads") }
        ForEach(active.prefix(8)) { item in
            Text("\(item.fileName) – \(Int(item.progress * 100))%")
        }
        Divider()
        Button("Show Downloads") {
            openWindow(id: "main")
            NSApp.activate(ignoringOtherApps: true)
        }
        Button("Stop All") { manager.pauseAll() }
        Button("Resume All") { manager.resumeAll() }
        Divider()
        Button("Quit") { NSApp.terminate(nil) }
    }
}

/// Registers the browser helper so the extension can reach the app (only for browsers that are installed).
enum HostInstaller {
    static let browsers = [
        "Google/Chrome", "Google/Chrome Beta", "Google/Chrome Dev", "Google/Chrome Canary", "Chromium",
        "BraveSoftware/Brave-Browser", "Microsoft Edge", "Vivaldi",
    ]

    static func install() {
        let host = Bundle.main.bundleURL.appendingPathComponent("Contents/MacOS/nexonic-host")
        guard FileManager.default.isExecutableFile(atPath: host.path),
              let data = try? JSONSerialization.data(withJSONObject: NativeMessaging.manifest(hostPath: host.path),
                                                     options: [.prettyPrinted, .sortedKeys]) else { return }
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        if FileManager.default.fileExists(atPath: support.appendingPathComponent("Firefox").path),
           let ff = try? JSONSerialization.data(withJSONObject: NativeMessaging.firefoxManifest(hostPath: host.path),
                                                 options: [.prettyPrinted, .sortedKeys]) {
            let dir = support.appendingPathComponent("Mozilla/NativeMessagingHosts")
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            try? ff.write(to: dir.appendingPathComponent("\(NativeMessaging.hostName).json"), options: .atomic)
        }
        for browser in browsers {
            let base = support.appendingPathComponent(browser)
            guard FileManager.default.fileExists(atPath: base.path) else { continue }
            let dir = base.appendingPathComponent("NativeMessagingHosts")
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            try? data.write(to: dir.appendingPathComponent("\(NativeMessaging.hostName).json"), options: .atomic)
        }
    }

    /// Copies the bundled browser extension (folder for Chrome-type browsers, .xpi for Firefox) somewhere the user can reach.
    static func saveExtension(firefox: Bool, to folder: URL) throws -> URL {
        let name = firefox ? "Firefox Add-on.xpi" : "Extension"
        guard let src = Bundle.main.resourceURL?.appendingPathComponent(name),
              FileManager.default.fileExists(atPath: src.path) else { throw CocoaError(.fileNoSuchFile) }
        let dest = FileNames.unique(firefox ? "Nexonic Firefox Add-on.xpi" : "Nexonic Extension", in: folder)
        try FileManager.default.copyItem(at: src, to: dest)
        return dest
    }

    static var commandPath: String { Bundle.main.bundleURL.appendingPathComponent("Contents/MacOS/nexonic-host").path }

    /// Links the `ndm` command into /usr/local/bin (asks for an administrator password).
    static func installCommand() -> String {
        let script = "do shell script \"mkdir -p /usr/local/bin && ln -sf " + "'\(commandPath)' /usr/local/bin/ndm\" with administrator privileges"
        var error: NSDictionary?
        NSAppleScript(source: script)?.executeAndReturnError(&error)
        return error == nil ? "Installed. Open Terminal and type ndm help." : "Not installed: \(error?[NSAppleScript.errorMessage] ?? "cancelled")"
    }
}

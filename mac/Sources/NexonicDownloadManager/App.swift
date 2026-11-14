import AppKit
import NexonicCore
import SwiftUI

@main
struct NexonicDownloadManagerApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var manager = DownloadManager.shared

    init() {
        if Portable.root() == nil { DataLocation.applyPending(AppPaths.usual) }  // before anything opens the data folder
        AppLanguage.start()
        CrashReports.install(version: AppLinks.version)
        HostInstaller.install()
    }

    var body: some Scene {
        // A single main window: links from the browser are added to it instead of opening another window.
        Window("Nexonic Download Manager", id: "main") {
            ContentView()
                .environmentObject(manager)
                .frame(minWidth: 900, minHeight: 480)
                .liveLanguage()
        }
        // Links from the browser never open or bring up this window (AppDelegate handles them): like a classic download
        // manager, Nexonic stays in the menu bar and only the Download File Info window appears.
        .handlesExternalEvents(matching: [])
        .commands {
            AppCommands()
            MainMenus()
        }
        Settings {
            SettingsView().environmentObject(manager).liveLanguage()
        }
        Window("About Nexonic Download Manager", id: "about") {
            AboutView().liveLanguage()
        }
        .windowResizability(.contentSize)
        Window("Nexonic Download Manager Help", id: "help") {
            HelpView().liveLanguage()
        }
        MenuBarExtra {
            MenuBarView().environmentObject(manager).liveLanguage()
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
    /// Settings key: keep Nexonic in the menu bar only (no Dock icon) while none of its windows are open.
    static let menuBarOnlyKey = "menuBarOnly"
    /// Started by the browser helper (a nexonicdm:// link) rather than by the user: the main window stays closed.
    static var launchedForLink = false

    func applicationWillFinishLaunching(_ notification: Notification) {
        // In the menu bar only by default (the Dock icon shows while a window is open); Settings can change it.
        UserDefaults.standard.register(defaults: [Self.menuBarOnlyKey: true])
        Self.launchedForLink = NSAppleEventManager.shared().currentAppleEvent?.eventID == AEEventID(kAEGetURL)
    }

    /// Closes the main window SwiftUI opens at launch when the browser helper started the app.
    static func closeMainWindowIfLaunchedForLink() {
        guard launchedForLink else { return }
        launchedForLink = false
        DispatchQueue.main.async {
            NSApp.windows.filter { $0.identifier?.rawValue.hasPrefix("main") == true && !($0 is NSPanel) }.forEach { $0.close() }
            updateDockIcon()
        }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        Updater.start()
        let center = NotificationCenter.default
        // A window coming up shows the Dock icon (and the app's menus); closing the last one hides it again when chosen.
        center.addObserver(forName: NSWindow.didBecomeKeyNotification, object: nil, queue: .main) { _ in
            Task { @MainActor in AppDelegate.updateDockIcon() }
        }
        center.addObserver(forName: NSWindow.willCloseNotification, object: nil, queue: .main) { note in
            let closing = note.object as? NSWindow
            Task { @MainActor in AppDelegate.updateDockIcon(closing: closing) }
        }
    }

    /// Dock icon only while a window is open, when "menu bar only" is on; always otherwise.
    @MainActor static func updateDockIcon(closing: NSWindow? = nil) {
        let menuBarOnly = UserDefaults.standard.bool(forKey: menuBarOnlyKey)
        let open = NSApp.windows.contains { $0 !== closing && $0.isVisible && $0.canBecomeMain && !($0 is NSPanel) }
        let policy: NSApplication.ActivationPolicy = menuBarOnly && !open ? .accessory : .regular
        if NSApp.activationPolicy() != policy { NSApp.setActivationPolicy(policy) }
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
    @ObservedObject private var language = LanguageState.shared

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
            Button("Crash Reports") {
                try? FileManager.default.createDirectory(at: CrashReports.folder, withIntermediateDirectories: true)
                NSWorkspace.shared.open(CrashReports.folder)
            }
            Button("Release Notes") { AppLinks.open(AppLinks.releases) }
            Divider()
            Button("Website") { AppLinks.open(AppLinks.website) }
            Button("Source Code on GitHub") { AppLinks.open(AppLinks.project) }
            Button("Terms of Use and License") { AppLinks.open(AppLinks.terms) }
            Divider()
            Button("Support the Project") { AppLinks.open(AppLinks.support) }
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
        Button("Start All Queues") { for q in manager.queues where !q.isMain { manager.startQueue(q.id) } }
        Button("Stop All Queues") { for q in manager.queues where !q.isMain { manager.stopQueue(q.id) } }
        Button("Open Downloads Folder") { NSWorkspace.shared.open(manager.settings.folder) }
        Toggle("Watch Copied Links", isOn: $manager.settings.monitorClipboard)
        Toggle("Drop Target", isOn: $manager.settings.finish.dropTarget)
        Divider()
        Button("Quit") { NSApp.terminate(nil) }
    }
}

/// Registers the browser helper so the extension can reach the app (only for browsers that are installed).
enum HostInstaller {
    static let browsers = [
        "Google/Chrome", "Google/Chrome Beta", "Google/Chrome Dev", "Google/Chrome Canary", "Chromium",
        "BraveSoftware/Brave-Browser", "BraveSoftware/Brave-Browser-Beta", "Microsoft Edge", "Microsoft Edge Beta", "Microsoft Edge Dev",
        "Vivaldi", "Arc/User Data", "com.operasoftware.Opera", "com.operasoftware.OperaGX", "Thorium",
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

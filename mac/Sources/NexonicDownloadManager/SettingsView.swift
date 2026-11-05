import AppKit
import NexonicCore
import NexonicTorrent
import SwiftUI
import UniformTypeIdentifiers

struct SettingsView: View {
    @EnvironmentObject var manager: DownloadManager
    @State private var message = ""
    @State private var editing: HostProfile?
    @State private var selectedSite: HostProfile.ID?
    @State private var takeSettings = false
    @State private var backupMessage = ""

    var body: some View {
        TabView {
            Form {
                Stepper("Connections per download: \(manager.settings.connections)", value: $manager.settings.connections, in: 1...32)
                Stepper("Downloads at the same time: \(manager.settings.maxActive)", value: $manager.settings.maxActive, in: 1...10)
                LabeledContent("Save to") {
                    HStack {
                        Text(manager.settings.folder.path).lineLimit(1).truncationMode(.middle)
                        Button("Choose…") { if let u = chooseFolder(manager.settings.folder) { manager.settings.folder = u } }
                    }
                }
                Toggle("Put each type in its own folder (Video, Music, Documents, Programs, Compressed, Images)", isOn: $manager.settings.sortIntoCategoryFolders)
                Toggle("Notifications", isOn: $manager.settings.notifications)
                HStack {
                    Toggle("Check for updates when the app opens", isOn: $manager.settings.checkForUpdates)
                    Spacer()
                    Button("Check Now") { Updater.check(manual: true) }
                }
                Section("Browser downloads") {
                    Toggle("Show the download window when a link is clicked in the browser", isOn: $manager.settings.showDownloadDialog)
                    Toggle("Show a progress window for each download I start", isOn: $manager.settings.showProgressWindow)
                    Toggle("Bring the main window to the front for browser downloads", isOn: $manager.settings.bringToFrontOnBrowserDownload)
                    Text("With the download window off, downloads start right away. The app keeps running in the menu bar when its window is closed.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            .formStyle(.grouped)
            .tabItem { Label("General", systemImage: "gearshape") }

            Form {
                TextField("Speed limit for all downloads in KB/s (0 = none)", value: $manager.settings.speedLimitKBps, format: .number)
                Picker("Proxy", selection: $manager.settings.proxy.mode) {
                    ForEach(ProxyMode.allCases, id: \.self) { Text($0.title).tag($0) }
                }
                if manager.settings.proxy.mode == .http || manager.settings.proxy.mode == .socks {
                    TextField("Proxy address", text: $manager.settings.proxy.host, prompt: Text("proxy.example.com"))
                    TextField("Port", value: $manager.settings.proxy.port, format: .number.grouping(.never))
                    TextField("User name (optional)", text: $manager.settings.proxy.username)
                    SecureField("Password", text: $manager.settings.proxy.password)
                }
                Text("Proxy changes apply to downloads started afterwards. Password-protected downloads: add the user name and password in the Add window or under Properties.")
                    .font(.caption).foregroundStyle(.secondary)
                Section("Advanced") {
                    Stepper("Smallest part of a file: \(manager.settings.network.minSegmentKB) KB", value: $manager.settings.network.minSegmentKB, in: 64...65536, step: 64)
                    Stepper("Tries per part before a download stops: \(manager.settings.network.retries)", value: $manager.settings.network.retries, in: 1...50)
                    Stepper("Reconnect a part that receives nothing for \(manager.settings.network.readTimeoutSeconds) s", value: $manager.settings.network.readTimeoutSeconds, in: 5...600, step: 5)
                }
            }
            .formStyle(.grouped)
            .tabItem { Label("Connection", systemImage: "network") }

            Form {
                Toggle("Torrents on", isOn: $manager.settings.torrentsEnabled)
                Toggle("Forward the listening port on my router (UPnP and NAT-PMP)", isOn: $manager.settings.torrentNetwork.portForwarding)
                    .help("Lets more peers connect to you, which usually makes torrents faster.")
                Picker("Encryption", selection: $manager.settings.torrentNetwork.encryption) {
                    Text("Prefer encrypted connections").tag(TorrentNetwork.Encryption.prefer)
                    Text("Only encrypted connections").tag(TorrentNetwork.Encryption.require)
                    Text("Off").tag(TorrentNetwork.Encryption.off)
                }
                Text("Speed limits, connections per torrent, upload slots and seeding are set for each torrent when you add it, or later under Torrent Settings. Changes here apply to torrents started afterwards. Peers and trackers can see your IP address.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            .formStyle(.grouped)
            .tabItem { Label("Torrents", systemImage: "arrow.down.circle.dotted") }

            VStack(alignment: .leading, spacing: 8) {
                Text("Settings used for every download from a website. A site also covers its subdomains (example.com covers www.example.com). Logins are only sent to that site and are kept with the app's other secrets.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                List(manager.settings.hostProfiles, selection: $selectedSite) { p in
                    Text((p.enabled ? "" : "(off) ") + HostProfiles.normalize(p.host) + SettingsView.summary(p)).tag(p.id)
                }
                HStack {
                    Button("Add Site…") { editing = HostProfile() }
                    Button("Edit…") { editing = manager.settings.hostProfiles.first { $0.id == selectedSite } }.disabled(selectedSite == nil)
                    Button("Remove") { manager.settings.hostProfiles.removeAll { $0.id == selectedSite }; selectedSite = nil }.disabled(selectedSite == nil)
                }
            }
            .padding()
            .tabItem { Label("Sites", systemImage: "list.bullet") }
            .sheet(item: $editing) { profile in
                SiteEditor(profile: profile) { saved in
                    if let i = manager.settings.hostProfiles.firstIndex(where: { $0.id == saved.id }) { manager.settings.hostProfiles[i] = saved }
                    else { manager.settings.hostProfiles.append(saved) }
                }
            }

            Form {
                Text("A backup holds your settings, site settings, queues and the downloads list. Passwords and cookies are never put in a backup.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                HStack {
                    Button("Export Backup…") { exportBackup() }
                    Button("Import Backup…") { importBackup() }
                    Button("Export Links…") { exportLinks() }
                }
                Toggle("When importing, also take the backup's settings", isOn: $takeSettings)
                if !backupMessage.isEmpty { Text(backupMessage).font(.caption).foregroundStyle(.secondary) }
            }
            .formStyle(.grouped)
            .tabItem { Label("Backup", systemImage: "externaldrive") }

            Form {
                Toggle("Watch the clipboard for copied download links", isOn: $manager.settings.monitorClipboard)
                TextField("File types", text: $manager.settings.clipboardTypes, axis: .vertical).lineLimit(2...4)
                Text("When you copy a link that ends in one of these types, the Add window opens with it.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            .formStyle(.grouped)
            .tabItem { Label("Clipboard", systemImage: "doc.on.clipboard") }

            Form {
                Toggle("Show the Download Complete window", isOn: $manager.settings.showCompleteDialog)
                Toggle("Play a sound", isOn: $manager.settings.playSound)
                Toggle("Mark files as downloaded so macOS checks them when first opened", isOn: $manager.settings.markDownloads)
                Toggle("Scan each finished file with a virus scanner", isOn: $manager.settings.scanEnabled)
                TextField("Scanner command (the file is added at the end)", text: $manager.settings.scanCommand,
                          prompt: Text("/usr/local/bin/clamscan --no-summary"))
                    .disabled(!manager.settings.scanEnabled)
                Text("Expected checksums are checked automatically when a download finishes. Queues can also quit the app, sleep or shut down the Mac when they are done (Scheduler).")
                    .font(.caption).foregroundStyle(.secondary)
            }
            .formStyle(.grouped)
            .tabItem { Label("After Download", systemImage: "checkmark.seal") }

            Form {
                Section("Chrome, Edge, Brave, Vivaldi") {
                    Text("Save the extension folder, open the browser's extensions page, switch on Developer mode, choose Load unpacked and pick the folder.")
                        .fixedSize(horizontal: false, vertical: true)
                    Button("Save Extension Folder…") { save(firefox: false) }
                }
                Section("Firefox") {
                    Text("Save the add-on file and drag it onto a Firefox window. Firefox installs add-ons that are signed; see the README for signing your copy.")
                        .fixedSize(horizontal: false, vertical: true)
                    Button("Save Firefox Add-on…") { save(firefox: true) }
                }
                Section("Command line") {
                    Text("Install the ndm command, then for example: ndm https://example.com/file.zip --queue Night")
                        .fixedSize(horizontal: false, vertical: true)
                    Button("Install ndm Command…") { message = HostInstaller.installCommand() }
                }
                if !message.isEmpty { Text(message).font(.caption).foregroundStyle(.secondary) }
            }
            .formStyle(.grouped)
            .tabItem { Label("Browsers & Command Line", systemImage: "globe") }
        }
        .frame(width: 580, height: 400)
        .tint(brand)
    }

    static func summary(_ p: HostProfile) -> String {
        var parts: [String] = []
        if let c = p.maxConnections { parts.append("\(c) connections") }
        if let l = p.speedLimitKBps, l > 0 { parts.append("\(l) KB/s") }
        if !(p.folder ?? "").isEmpty { parts.append("own folder") }
        if !(p.username ?? "").isEmpty { parts.append("login") }
        if p.proxy != nil { parts.append("own proxy") }
        return parts.isEmpty ? "" : "   ·   " + parts.joined(separator: ", ")
    }

    private func exportBackup() {
        let panel = NSSavePanel()
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd"
        panel.nameFieldStringValue = "nexonic-backup-\(f.string(from: Date())).json"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try manager.exportBackup(version: AppLinks.version).write(to: url, options: .atomic)
            backupMessage = "Saved \(url.lastPathComponent): \(manager.items.count) downloads, \(manager.queues.count) queues."
        } catch { backupMessage = error.localizedDescription }
    }

    private func importBackup() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let n = try manager.importBackup(try Data(contentsOf: url), settingsToo: takeSettings)
            backupMessage = "Added \(n) download\(n == 1 ? "" : "s")" + (takeSettings ? " and took the backup's settings." : ".")
        } catch { backupMessage = error.localizedDescription }
    }

    private func exportLinks() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "nexonic-links.txt"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try (manager.items.map(\.url.absoluteString).joined(separator: "\n") + "\n").write(to: url, atomically: true, encoding: .utf8)
            backupMessage = "Saved \(manager.items.count) links to \(url.lastPathComponent)."
        } catch { backupMessage = error.localizedDescription }
    }

    private func save(firefox: Bool) {
        guard let folder = chooseFolder(FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]) else { return }
        do {
            let dest = try HostInstaller.saveExtension(firefox: firefox, to: folder)
            message = "Saved to \(dest.path)"
            NSWorkspace.shared.activateFileViewerSelecting([dest])
        } catch {
            message = "Could not save: \(error.localizedDescription)"
        }
    }
}

/// Edits one site's settings. Empty fields keep the general setting.
struct SiteEditor: View {
    @Environment(\.dismiss) private var dismiss
    let original: HostProfile
    let onSave: (HostProfile) -> Void
    @State private var host = ""
    @State private var enabled = true
    @State private var connections = ""
    @State private var limit = ""
    @State private var tries = ""
    @State private var timeout = ""
    @State private var userAgent = ""
    @State private var folder = ""
    @State private var queue = ""
    @State private var later = false
    @State private var username = ""
    @State private var password = ""
    @State private var ownProxy = false
    @State private var proxy = ProxySettings()
    @State private var problem = ""

    init(profile: HostProfile, onSave: @escaping (HostProfile) -> Void) {
        original = profile
        self.onSave = onSave
        _host = State(initialValue: profile.host)
        _enabled = State(initialValue: profile.enabled)
        _connections = State(initialValue: profile.maxConnections.map(String.init) ?? "")
        _limit = State(initialValue: profile.speedLimitKBps.map(String.init) ?? "")
        _tries = State(initialValue: profile.retries.map(String.init) ?? "")
        _timeout = State(initialValue: profile.timeoutSeconds.map(String.init) ?? "")
        _userAgent = State(initialValue: profile.userAgent ?? "")
        _folder = State(initialValue: profile.folder ?? "")
        _queue = State(initialValue: profile.queueName ?? "")
        _later = State(initialValue: profile.startAtOnce == false)
        _username = State(initialValue: profile.username ?? "")
        _password = State(initialValue: profile.password ?? "")
        _ownProxy = State(initialValue: profile.proxy != nil)
        _proxy = State(initialValue: profile.proxy ?? ProxySettings())
    }

    private static func number(_ text: String, _ range: ClosedRange<Int>) -> Int? {
        Int(text.trimmingCharacters(in: .whitespaces)).map { min(max($0, range.lowerBound), range.upperBound) }
    }

    private static func text(_ s: String) -> String? {
        let t = s.trimmingCharacters(in: .whitespaces)
        return t.isEmpty ? nil : t
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Form {
                TextField("Website", text: $host, prompt: Text("example.com"))
                Toggle("Use these settings", isOn: $enabled)
                TextField("Connections per download (1-32)", text: $connections)
                TextField("Speed limit in KB/s (0 = none)", text: $limit)
                TextField("Tries per part (1-50)", text: $tries)
                TextField("Reconnect after this many silent seconds", text: $timeout)
                TextField("User agent", text: $userAgent)
                HStack {
                    TextField("Save to (empty = the usual folder)", text: $folder)
                    Button("Choose…") { if let u = chooseFolder(URL(fileURLWithPath: folder.isEmpty ? NSHomeDirectory() : folder)) { folder = u.path } }
                }
                TextField("Queue (name)", text: $queue)
                Toggle("Add downloads from this site stopped", isOn: $later)
                TextField("User name", text: $username)
                SecureField("Password", text: $password)
                Toggle("Use its own proxy", isOn: $ownProxy)
                if ownProxy {
                    Picker("Proxy", selection: $proxy.mode) {
                        ForEach(ProxyMode.allCases, id: \.self) { Text($0.title).tag($0) }
                    }
                    TextField("Proxy address", text: $proxy.host)
                    TextField("Port", value: $proxy.port, format: .number.grouping(.never))
                }
                if !problem.isEmpty { Text(problem).foregroundStyle(.red).font(.caption) }
            }
            .formStyle(.grouped)
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("OK") { save() }.keyboardShortcut(.defaultAction)
            }
            .padding()
        }
        .frame(width: 520, height: 600)
    }

    private func save() {
        let h = HostProfiles.normalize(host)
        guard !h.isEmpty, URL(string: "https://\(h)/")?.host != nil, !h.contains(" ") else {
            problem = "Enter the website's address, for example example.com."
            return
        }
        var p = original
        p.host = h
        p.enabled = enabled
        p.maxConnections = Self.number(connections, 1...32)
        p.speedLimitKBps = Self.number(limit, 0...10_000_000)
        p.retries = Self.number(tries, 1...50)
        p.timeoutSeconds = Self.number(timeout, 5...3600)
        p.userAgent = Self.text(userAgent)
        p.folder = Self.text(folder)
        p.queueName = Self.text(queue)
        p.startAtOnce = later ? false : nil
        p.username = Self.text(username)
        p.password = password.isEmpty ? nil : password
        p.proxy = ownProxy ? proxy : nil
        onSave(p)
        dismiss()
    }
}

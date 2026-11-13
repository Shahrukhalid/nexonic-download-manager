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
    @State private var language = AppLanguage.chosen
    @AppStorage(Units.sizeKey) private var sizeUnits = "binary"
    @AppStorage(Units.speedKey) private var speedUnits = "bytes"
    @AppStorage(Units.dateKey) private var dateStyle = "absolute"
    @AppStorage(AppFont.nameKey) private var appFont = ""
    @AppStorage(AppFont.sizeKey) private var appFontSize = 0.0
    @AppStorage(AudioConversion.key) private var audioFormat = "original"
    @AppStorage(EnginePaths.ytdlpKey) private var ownYtDlp = ""
    @AppStorage(EnginePaths.ffmpegKey) private var ownFFmpeg = ""
    @AppStorage(DownloadManager.manualKey) private var manualActive = 0
    @AppStorage(MissingFiles.removeOnStartKey) private var removeMissingOnStart = false
    @State private var missingMessage = ""
    @AppStorage(AppDelegate.menuBarOnlyKey) private var menuBarOnly = true
    @State private var dataFolder = AppPaths.support.path
    @State private var dataMessage = DataLocation.pending(AppPaths.usual).map { "Moves to \($0) the next time the app starts." } ?? DataLocation.lastProblem ?? ""
    private static let fontFamilies = NSFontManager.shared.availableFontFamilies.sorted()

    /// The user's own scheme (the one saved, or a starting point built from the app's colours).
    private var customScheme: ColorSchemeChoice {
        ColorSchemeChoice.custom(manager.settings.colorScheme)
            ?? ColorSchemeChoice.custom(ColorSchemeChoice.customCode(dark: false, accent: "#2563EB", background: "#F6F8FC"))!
    }

    private func setCustom(dark: Bool? = nil, accent: String? = nil, background: String? = nil) {
        let c = customScheme
        manager.settings.colorScheme = ColorSchemeChoice.customCode(dark: dark ?? c.dark, accent: accent ?? c.accent, background: background ?? c.background)
    }

    static func color(_ hex: String) -> Color {
        guard let (r, g, b) = ColorSchemeChoice.rgb(hex) else { return .accentColor }
        return Color(red: r, green: g, blue: b)
    }

    static func hex(_ color: Color) -> String {
        guard let c = NSColor(color).usingColorSpace(.sRGB) else { return "#000000" }
        return String(format: "#%02X%02X%02X", Int((c.redComponent * 255).rounded()), Int((c.greenComponent * 255).rounded()), Int((c.blueComponent * 255).rounded()))
    }

    var body: some View {
        TabView {
            Form {
                Picker("Language", selection: $language) {
                    Text("Same as the system").tag("")
                    ForEach(AppLanguage.all, id: \.code) { Text(verbatim: $0.name).tag($0.code) }
                }
                .onChange(of: language) { v in AppLanguage.use(v) }
                Picker("Appearance", selection: $manager.settings.appearance) {
                    Text("Same as macOS").tag("system")
                    Text("Light").tag("light")
                    Text("Dark").tag("dark")
                }
                Picker("Colour scheme", selection: $manager.settings.colorScheme) {
                    Text("None").tag("")
                    ForEach(ColorSchemeChoice.builtIn, id: \.name) { s in Text(LocalizedStringKey(s.name)).tag(s.name) }
                    Text("My own").tag(customScheme.name)
                }
                .help("A scheme sets light or dark, the accent colour and the background; None follows Appearance.")
                if ColorSchemeChoice.custom(manager.settings.colorScheme) != nil {
                    Toggle("Dark", isOn: Binding(get: { customScheme.dark }, set: { setCustom(dark: $0) }))
                    ColorPicker("Accent colour", selection: Binding(get: { Self.color(customScheme.accent) }, set: { setCustom(accent: Self.hex($0)) }), supportsOpacity: false)
                    ColorPicker("Background", selection: Binding(get: { Self.color(customScheme.background) }, set: { setCustom(background: Self.hex($0)) }), supportsOpacity: false)
                    if let c = ColorSchemeChoice.contrast(customScheme.accent, customScheme.background), c < 3 {
                        Text(String(format: String(localized: "The accent is hard to see on this background (contrast %.1f:1, at least 3:1 is needed)."), c))
                            .font(.caption).foregroundStyle(.orange)
                    }
                }
                Picker("Font", selection: $appFont) {
                    Text("Same as macOS").tag("")
                    ForEach(Self.fontFamilies, id: \.self) { Text(verbatim: $0).tag($0) }
                }
                Picker("Text size", selection: $appFontSize) {
                    Text("Same as macOS").tag(0.0)
                    ForEach([11.0, 12, 13, 14, 15, 16, 18, 20], id: \.self) { Text("\(Int($0)) pt").tag($0) }
                }
                Picker("Sizes", selection: $sizeUnits) {
                    Text("KB, MB, GB (1 KB = 1024 bytes)").tag("binary")
                    Text("KiB, MiB, GiB (1 KiB = 1024 bytes)").tag("iec")
                    Text("KB, MB, GB (1 KB = 1000 bytes)").tag("decimal")
                }
                Picker("Speeds", selection: $speedUnits) {
                    Text("Bytes per second (MB/s)").tag("bytes")
                    Text("Bits per second (Mbit/s)").tag("bits")
                }
                Picker("Dates in the list", selection: $dateStyle) {
                    Text("Date and time").tag("absolute")
                    Text("How long ago (5 min ago)").tag("relative")
                }
                Toggle("Automatic connections", isOn: $manager.settings.autoConnections)
                    .help("Starts with a few connections and adds more while the speed keeps rising; slows down when a server asks.")
                Stepper((manager.settings.autoConnections ? "Most connections per download: " : "Connections per download: ")
                        + "\(manager.settings.connections)", value: $manager.settings.connections, in: 1...256)
                Stepper("Downloads at the same time: \(manager.settings.maxActive)", value: $manager.settings.maxActive, in: 1...10)
                LabeledContent("Save to") {
                    HStack {
                        Text(manager.settings.folder.path).lineLimit(1).truncationMode(.middle)
                        Button("Choose…") { if let u = chooseFolder(manager.settings.folder) { manager.settings.folder = u } }
                    }
                }
                Toggle("Put each type in its own folder (Video, Music, Documents, Programs, Compressed, Images)", isOn: $manager.settings.sortIntoCategoryFolders)
                CategoriesEditor()
                Toggle("Start the Add window in the folder I used last time", isOn: $manager.settings.rememberLastFolder)
                Toggle("Keep Nexonic in the menu bar when its window is closed (no Dock icon)", isOn: $menuBarOnly)
                    .onChange(of: menuBarOnly) { _ in AppDelegate.updateDockIcon() }
                    .help("Downloads keep running; open the window again from the menu bar icon.")
                Toggle("Notifications", isOn: $manager.settings.notifications)
                HStack {
                    Toggle("Check for updates when the app opens", isOn: $manager.settings.checkForUpdates)
                    Spacer()
                    Button("Check Now") { Updater.check(manual: true) }
                }
                Section("Video tools") {
                    TextField("Your own yt-dlp program (full path)", text: $ownYtDlp)
                    TextField("Your own FFmpeg program (full path)", text: $ownFFmpeg)
                    if let p = EnginePaths.problem(ownYtDlp) ?? EnginePaths.problem(ownFFmpeg) {
                        Text(p).font(.caption).foregroundStyle(.red)
                    }
                    Text("Leave empty to use the ones the app ships and keeps current. Your own yt-dlp is never updated or replaced by the app; FFmpeg needs ffprobe in the same folder.")
                        .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    Picker("Sound-only downloads", selection: $audioFormat) {
                        Text("Original format (no conversion)").tag("original")
                        Text("MP3").tag("mp3")
                        Text("Opus").tag("opus")
                        Text("FLAC").tag("flac")
                    }
                    Text("Converting needs FFmpeg. MP3 uses the best variable quality; FLAC keeps every detail of the sound the site sends.")
                        .font(.caption).foregroundStyle(.secondary)
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
                Stepper("Downloads I resume myself, beside the queues: \(manualActive == 0 ? String(localized: "off") : String(manualActive))",
                        value: $manualActive, in: 0...10)
                    .help("0 = a resumed download waits for a slot in its queue and starts the queue.")
                VStack(alignment: .leading, spacing: 4) {
                    Text("Speed schedule (one rule per line: days start-end KB/s, 0 = no limit)")
                    TextEditor(text: $manager.settings.speedSchedule)
                        .font(.system(.body, design: .monospaced)).frame(height: 60)
                    Text(SpeedSchedule.parse(manager.settings.speedSchedule).problem
                         ?? "For example Mon-Fri 09:00-17:00 300 or weekends 23:00-07:00 0. The first rule that applies wins; at other times the limit above applies.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Picker("When a link is already in the list", selection: $manager.settings.duplicateAction) {
                    Text("Ask").tag("ask")
                    Text("Skip it").tag("skip")
                    Text("Download a new copy").tag("again")
                }
                Section("Download limit") {
                    TextField("At most this many MB (0 = no limit)", value: $manager.settings.dataCapMB, format: .number.grouping(.never))
                    Stepper("… every \(manager.settings.dataCapHours) hours", value: $manager.settings.dataCapHours, in: 1...744)
                    Text("When the amount is reached, downloads stop and continue by themselves when the next period starts."
                         + (manager.dataCapStatus.map { " Now: \($0)." } ?? ""))
                        .font(.caption).foregroundStyle(.secondary)
                }
                Picker("Proxy", selection: $manager.settings.proxy.mode) {
                    ForEach(ProxyMode.allCases, id: \.self) { Text($0.title).tag($0) }
                }
                if manager.settings.proxy.mode == .http || manager.settings.proxy.mode == .socks {
                    TextField("Proxy address", text: $manager.settings.proxy.host, prompt: Text("proxy.example.com"))
                    TextField("Port", value: $manager.settings.proxy.port, format: .number.grouping(.never))
                    TextField("User name (optional)", text: $manager.settings.proxy.username)
                    SecureField("Password", text: $manager.settings.proxy.password)
                }
                if manager.settings.proxy.mode == .pac {
                    TextField("Script address or file", text: $manager.settings.proxy.host, prompt: Text("http://wpad/proxy.pac"))
                }
                Text("Proxy changes apply to downloads started afterwards. Password-protected downloads: add the user name and password in the Add window or under Properties.")
                    .font(.caption).foregroundStyle(.secondary)
                Section("Advanced") {
                    Stepper("Smallest part of a file: \(manager.settings.network.minSegmentKB) KB", value: $manager.settings.network.minSegmentKB, in: 64...65536, step: 64)
                    Stepper("Tries per part before a download stops: \(manager.settings.network.retries)", value: $manager.settings.network.retries, in: 1...50)
                    Stepper("Reconnect a part that receives nothing for \(manager.settings.network.readTimeoutSeconds) s", value: $manager.settings.network.readTimeoutSeconds, in: 5...600, step: 5)
                }
                Section("Network adapter") {
                    Picker("Network adapter", selection: $manager.settings.network.bindAdapter) {
                        ForEach(Adapters.choices(current: manager.settings.network.bindAdapter), id: \.self) { name in
                            Text(name.isEmpty ? String(localized: "Any adapter") : name).tag(name)
                        }
                    }
                    Text("Downloads, videos and torrents use only this adapter (for example your VPN). If it disconnects, downloads stop instead of using another connection.")
                        .font(.caption).foregroundStyle(.secondary)
                    TextField("Torrent blocklist: a file or an https:// address (empty = none)", text: $manager.settings.network.torrentBlocklist)
                    if let problem = TorrentBlocklist.problem(manager.settings.network.torrentBlocklist) {
                        Text(LocalizedStringKey(problem)).font(.caption).foregroundStyle(.red)
                    }
                    Text("Torrents never connect to the addresses in this list. P2P, DAT and CIDR lists work, also as .gz. An address is downloaded again once a day.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Section("Debrid service (optional)") {
                    Picker("Debrid service (optional)", selection: $manager.settings.debrid.service) {
                        ForEach(DebridService.allCases, id: \.self) { s in Text(LocalizedStringKey(s.title)).tag(s) }
                    }
                    SecureField("Your API key for the service", text: $manager.settings.debrid.key)
                    TextField("Sites whose links go through it (comma separated, e.g. example-host.com)", text: $manager.settings.debrid.hosts)
                    Text("Off by default. Uses your own account; links from other sites are never sent to the service. The key is saved in a private file and never in backups.")
                        .font(.caption).foregroundStyle(.secondary)
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
                Text("Speed limits, connections per torrent, upload slots and seeding are set for each torrent when you add it, or later under Torrent Settings. Torrents use the proxy from the Connection tab (HTTP or SOCKS5, or the Mac's proxy with \"Use the system settings\") and the speed limit for all downloads. Changes apply to torrents started afterwards. Peers and trackers can see your IP address, or your proxy's.")
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
                if Portable.root() == nil {
                    Section("Data folder") {
                        TextField("Settings, the download list and unfinished parts are kept in", text: $dataFolder)
                        HStack {
                            Button("Choose…") { if let u = chooseFolder(URL(fileURLWithPath: dataFolder)) { dataFolder = u.path } }
                            Button("Move at Next Start") {
                                dataMessage = DataLocation.request(usual: AppPaths.usual, target: dataFolder.trimmingCharacters(in: .whitespaces))
                                    ?? "The data moves the next time the app starts. Everything is copied and checked before the old copy is removed."
                            }
                            Button("Use the Usual Folder") { dataFolder = AppPaths.usual.path }
                        }
                        if !dataMessage.isEmpty { Text(LocalizedStringKey(dataMessage)).font(.caption).foregroundStyle(.secondary) }
                    }
                }
                Section("Missing files") {
                    Toggle("When the app starts, remove finished downloads whose file was deleted or moved", isOn: $removeMissingOnStart)
                    HStack {
                        Button("Remove Missing Files Now") {
                            missingMessage = String(format: String(localized: "Removed %d from the list. Files on disk are not touched."), manager.removeMissing())
                        }
                        Text(missingMessage.isEmpty ? String(format: String(localized: "%d finished downloads have no file now."), manager.items.filter(MissingFiles.isMissing).count) : missingMessage)
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
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
                TextField("Connections per download (1-256)", text: $connections)
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
        p.maxConnections = Self.number(connections, 1...256)
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

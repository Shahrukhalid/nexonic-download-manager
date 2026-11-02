import AppKit
import NexonicCore
import SwiftUI

struct SettingsView: View {
    @EnvironmentObject var manager: DownloadManager
    @State private var message = ""

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
            }
            .formStyle(.grouped)
            .tabItem { Label("Connection", systemImage: "network") }

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

import NexonicKit
import SwiftUI

struct SettingsView: View {
    @Environment(AppModel.self) private var model
    @Environment(BrowserState.self) private var browser
    @State private var wifiOnly = SharedSettings.wifiOnly
    @State private var folders = SharedSettings.categoryFolders
    @State private var confirmClear = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    NavigationLink { SetupGuideView() } label: {
                        Label("Set Up Download Shortcut", systemImage: "square.and.arrow.up.on.square")
                    }
                } footer: { Text("Download from Safari, Chrome and other apps with Share → Download with Nexonic, Siri or Shortcuts.") }

                Section("Downloads") {
                    Toggle("Wi-Fi only", isOn: $wifiOnly).onChange(of: wifiOnly) { _, v in SharedSettings.wifiOnly = v }
                    Toggle("Folders by type", isOn: $folders).onChange(of: folders) { _, v in SharedSettings.categoryFolders = v }
                    Button("Start All Downloads Kept for Later") { model.startLater() }
                    Button("Clear Finished from List") { confirmClear = true }
                }

                Section {
                    Button("Clear Browser Cookies, Cache and History") {
                        Task { await browser.clearData(); model.toast = "Browser data cleared" }
                    }
                } header: { Text("Privacy") } footer: {
                    Text("No account, no ads, no tracking. Logins for downloads are kept in the Keychain on this device and sent only to their own site.")
                }

                Section("About") {
                    LabeledContent("Version", value: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "")
                    Link("Website", destination: URL(string: "https://shahrukhalid.github.io/nexonic-download-manager/")!)
                    Link("Source Code and Help", destination: URL(string: "https://github.com/Shahrukhalid/nexonic-download-manager")!)
                    Link("Report a Problem", destination: URL(string: "https://github.com/Shahrukhalid/nexonic-download-manager/issues/new")!)
                    Text("Free and open source (MIT or Apache-2.0) by Shahrukh Khalid. Only download what you have the right to save; copy-protected video is not supported.")
                        .font(.footnote).foregroundStyle(.secondary)
                }
            }
            .navigationTitle("Settings")
            .confirmationDialog("Clear finished downloads from the list? The files stay in Files.", isPresented: $confirmClear, titleVisibility: .visible) {
                Button("Clear", role: .destructive) { model.clearFinished() }
            }
        }
    }
}

/// "Set Up Download Shortcut": how to put Download with Nexonic in the Share sheet, and the optional Shortcut.
struct SetupGuideView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(\.openURL) private var openURL

    var body: some View {
        List {
            Section {
                VStack(alignment: .leading, spacing: 8) {
                    Image(systemName: "square.and.arrow.up.circle.fill").font(.system(size: 44)).foregroundStyle(.tint)
                    Text("Download with one tap").font(.title2.bold())
                    Text("Nexonic is already in your Share sheet. These steps put it at the front, so it's one tap away.")
                        .foregroundStyle(.secondary)
                }
                .padding(.vertical, 6)
            }

            Section("Share sheet (works right away)") {
                Step(1, "In Safari or Chrome, open a page or a file link and tap **Share**.")
                Step(2, "Swipe the row of apps to the end and tap **More**.")
                Step(3, "Tap **Edit**, then the green **+** next to **Download with Nexonic** to add it to Favorites. Drag it to the top.")
                Step(4, "Next time: **Share → Download with Nexonic**, pick the quality if there's a choice, and tap **Download Now**.")
            }

            Section {
                Step(1, "Open **Shortcuts**. **Download with Nexonic** is already there under **Nexonic**, and you can say *\"Download with Nexonic\"* to Siri.")
                Step(2, "To use it from the Share sheet too: tap **+**, add the **Download with Nexonic** action and set **Links** to **Shortcut Input**.")
                Step(3, "Tap **ⓘ**, turn on **Show in Share Sheet**, and tap **Done**.")
                Button("Open Shortcuts") { openURL(URL(string: "shortcuts://")!) }
            } header: { Text("Shortcuts and Siri (optional)") } footer: {
                Text("You don't need a Shortcut: the Share sheet works on its own. Shortcuts are handy for automations and Siri.")
            }

            Section("Where your files go") {
                Step(1, "Open the **Files** app → **On My iPhone** (or **On My iPad**) → **Nexonic**. Files are sorted into Video, Music, Documents and more.")
                Step(2, "Downloads keep running when you leave the app. iOS may pause them on a weak network or in Low Power Mode; they continue later.")
                Button("Allow Notifications") { Notify.requestPermission() }
            }
        }
        .navigationTitle("Set Up Download Shortcut")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } } }
    }
}

private struct Step: View {
    let n: Int
    let text: LocalizedStringKey
    init(_ n: Int, _ text: LocalizedStringKey) { self.n = n; self.text = text }

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text("\(n)").font(.subheadline.bold()).foregroundStyle(.white)
                .frame(width: 24, height: 24).background(Circle().fill(.tint))
            Text(text)
        }
        .padding(.vertical, 2)
    }
}

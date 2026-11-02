import AppKit
import NexonicCore

/// Tells the user when a new version is published and installs it in place (Update Now, Remind Me Later, Skip This Version).
@MainActor
enum Updater {
    private static var busy = false
    private static var timer: Timer?
    private static var panel: NSPanel?

    /// Checks a few seconds after the app opens, then once a day while it keeps running.
    static func start() {
        guard ProcessInfo.processInfo.environment["NDM_DEMO"] != "1" else { return }  // not while taking screenshots
        DispatchQueue.main.asyncAfter(deadline: .now() + 5) { check(manual: false) }
        timer = Timer.scheduledTimer(withTimeInterval: 24 * 3600, repeats: true) { _ in
            Task { @MainActor in check(manual: false) }
        }
    }

    static func check(manual: Bool) {
        guard manual || DownloadManager.shared.settings.checkForUpdates, !busy else { return }
        busy = true
        Task {
            // GitHub's API first; when it refuses or fails, the copy of the same details attached to the release.
            var release = await fetch(Updates.latestURL)
            if release == nil { release = await fetch(Updates.mirrorURL) }
            busy = false
            found(release, manual: manual)
        }
    }

    private nonisolated static func fetch(_ url: URL) async -> Updates.Release? {
        var request = URLRequest(url: url, timeoutInterval: 20)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("NexonicDownloadManager/\(AppLinks.version)", forHTTPHeaderField: "User-Agent")
        guard let (data, response) = try? await URLSession.shared.data(for: request),
              (response as? HTTPURLResponse)?.statusCode == 200 else { return nil }
        return Updates.parse(data)
    }

    private static func found(_ release: Updates.Release?, manual: Bool) {
        let current = AppLinks.version
        guard let release else {
            if manual {
                message("Couldn't check for updates", "GitHub didn't answer. Check your internet connection and try again "
                        + "later, or download the newest version from the website.")
            }
            return
        }
        guard Updates.isNewer(release.version, than: current) else {
            if manual { message("You're up to date", "Nexonic Download Manager \(current) is the newest version.") }
            return
        }
        if !manual, DownloadManager.shared.settings.skippedVersion == release.version { return }

        let alert = NSAlert()
        alert.messageText = "A new version of Nexonic Download Manager is available"
        alert.informativeText = "Version \(release.version) is ready to install. You have \(current).\n\nWhat's new:"
        alert.accessoryView = notesView(Updates.notes(release.notes, after: current))
        alert.addButton(withTitle: "Update Now")
        alert.addButton(withTitle: "Remind Me Later")
        alert.addButton(withTitle: "Skip This Version")
        NSApp.activate(ignoringOtherApps: true)
        switch alert.runModal() {
        case .alertFirstButtonReturn: install(release)
        case .alertThirdButtonReturn: DownloadManager.shared.settings.skippedVersion = release.version
        default: break
        }
    }

    private static func notesView(_ text: String) -> NSView {
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 420, height: 180))
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        let view = NSTextView(frame: scroll.bounds)
        view.isEditable = false
        view.string = text.isEmpty ? "Improvements and fixes." : text
        view.font = .systemFont(ofSize: 12)
        view.textContainerInset = NSSize(width: 6, height: 6)
        view.autoresizingMask = [.width]
        scroll.documentView = view
        return scroll
    }

    // MARK: installing

    /// Downloads the new app, then a small helper replaces this copy after it quits and opens the new one.
    private static func install(_ release: Updates.Release) {
        let app = Bundle.main.bundleURL
        let asset = "NexonicDownloadManager-Mac.zip"
        // Only this app's own release file, and only when GitHub tells us its checksum; otherwise the release page.
        guard let zip = release.assets[asset], Updates.trustedAsset(zip), let digest = release.digests[asset], digest.count == 64,
              app.pathExtension == "app",
              FileManager.default.isWritableFile(atPath: app.deletingLastPathComponent().path) else {
            AppLinks.open(release.page)  // e.g. opened from the disk image: download it from the release page instead
            return
        }
        showProgress("Downloading version \(release.version)…")
        URLSession.shared.downloadTask(with: zip) { temp, response, _ in
            var file: URL?
            if let temp, (response as? HTTPURLResponse)?.statusCode == 200 {
                let dir = FileManager.default.temporaryDirectory.appendingPathComponent("nexonic-update-\(UUID().uuidString)")
                try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                let dest = dir.appendingPathComponent("update.zip")
                if (try? FileManager.default.moveItem(at: temp, to: dest)) != nil, Updates.verify(dest, sha256: digest) { file = dest }
            }
            let downloaded = file
            Task { @MainActor in
                panel?.close()
                panel = nil
                guard let file = downloaded else {
                    message("The update could not be downloaded or did not match the published file",
                            "Nothing was changed. Try again later, or download it from the release page.")
                    return
                }
                relaunch(with: file, app: app)
            }
        }.resume()
    }

    private static func relaunch(with zip: URL, app: URL) {
        let dir = zip.deletingLastPathComponent()
        let script = dir.appendingPathComponent("update.sh")
        let text = """
        #!/bin/bash
        pid="$1"; zip="$2"; dir="$3"; app="$4"
        while kill -0 "$pid" 2>/dev/null; do sleep 0.3; done
        if /usr/bin/ditto -x -k "$zip" "$dir/new"; then
          new="$(/bin/ls -d "$dir"/new/*.app 2>/dev/null | /usr/bin/head -1)"
          # only this app, with an intact signature, replaces this copy
          id="$(/usr/bin/defaults read "$new/Contents/Info" CFBundleIdentifier 2>/dev/null)"
          if [ "$id" != "com.nexonic.downloadmanager" ] || ! /usr/bin/codesign --verify --deep --strict "$new" 2>/dev/null; then new=""; fi
          if [ -d "$new" ] && /bin/mv "$app" "$app.old"; then
            if /usr/bin/ditto "$new" "$app"; then /bin/rm -rf "$app.old"; else /bin/rm -rf "$app"; /bin/mv "$app.old" "$app"; fi
          fi
        fi
        /usr/bin/xattr -dr com.apple.quarantine "$app" 2>/dev/null
        /usr/bin/open "$app"
        /bin/rm -rf "$dir"
        """
        do {
            try text.write(to: script, atomically: true, encoding: .utf8)
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/bin/bash")
            p.arguments = [script.path, "\(ProcessInfo.processInfo.processIdentifier)", zip.path, dir.path, app.path]
            try p.run()
        } catch {
            message("The update could not be installed", error.localizedDescription)
            return
        }
        NSApp.terminate(nil)
    }

    private static func showProgress(_ text: String) {
        let p = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 340, height: 90), styleMask: [.titled], backing: .buffered, defer: false)
        p.title = "Updating Nexonic Download Manager"
        let label = NSTextField(labelWithString: text)
        label.frame = NSRect(x: 20, y: 50, width: 300, height: 20)
        let bar = NSProgressIndicator(frame: NSRect(x: 20, y: 22, width: 300, height: 16))
        bar.isIndeterminate = true
        bar.startAnimation(nil)
        p.contentView?.addSubview(label)
        p.contentView?.addSubview(bar)
        p.center()
        p.makeKeyAndOrderFront(nil)
        panel = p
    }

    private static func message(_ title: String, _ text: String) {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = text
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
    }
}

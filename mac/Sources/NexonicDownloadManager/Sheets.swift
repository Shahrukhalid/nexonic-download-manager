import AppKit
import NexonicCore
import NexonicTorrent
import SwiftUI

@MainActor func chooseFolder(_ start: URL) -> URL? {
    let panel = NSOpenPanel()
    panel.canChooseDirectories = true
    panel.canChooseFiles = false
    panel.directoryURL = start
    return panel.runModal() == .OK ? panel.url : nil
}

/// Folder and queue rows shared by the Add, Batch and Grabber windows.
struct SaveOptions: View {
    @EnvironmentObject var manager: DownloadManager
    @Binding var folder: URL
    @Binding var queueID: String

    var body: some View {
        LabeledContent("Save to") {
            HStack {
                Text(folder.path).lineLimit(1).truncationMode(.middle).foregroundStyle(.secondary)
                Spacer()
                Button("Choose…") { if let u = chooseFolder(folder) { folder = u } }
            }
        }
        Picker("Queue", selection: $queueID) {
            ForEach(manager.queues) { q in Text(q.name).tag(q.id) }
        }
    }
}

// MARK: Add

struct AddSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    let request: AddRequest
    let onClose: () -> Void
    @State private var text = ""
    @State private var name = ""
    @State private var folder = URL(fileURLWithPath: NSHomeDirectory())
    @State private var queueID = DownloadQueue.mainID
    @State private var username = ""
    @State private var password = ""
    @State private var checksum = ""
    @State private var limit = 0
    @State private var more = false
    @State private var audioOnly = false

    private var hasSiteVideo: Bool { lines.contains { URL(string: $0).map(SiteVideo.isSupported) ?? false } }

    private var lines: [String] {
        text.split(whereSeparator: \.isNewline).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Add Download").font(.title2.bold())
            Text("Paste one or more links, one per line.").foregroundStyle(.secondary)
            TextEditor(text: $text).font(.body.monospaced()).frame(minHeight: 90)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(.quaternary))
            Form {
                if lines.count <= 1 {
                    TextField("File name", text: $name, prompt: Text(lines.first.flatMap { URL(string: $0) }.map(FileNames.fromURL) ?? "From the link"))
                }
                SaveOptions(folder: $folder, queueID: $queueID)
                if hasSiteVideo {
                    Toggle("Sound only (video sites)", isOn: $audioOnly)
                    Text("Video sites are saved in the best quality available, with picture and sound joined.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                DisclosureGroup("Login, checksum and speed limit", isExpanded: $more) {
                    TextField("User name", text: $username)
                    SecureField("Password", text: $password)
                    TextField("Checksum (MD5, SHA-1, SHA-256 or SHA-512)", text: $checksum)
                    TextField("Speed limit in KB/s (0 = none)", value: $limit, format: .number)
                }
            }
            .formStyle(.grouped)
            HStack {
                if !checksum.isEmpty && Checksum.parse(checksum) == nil {
                    Text("The checksum is not a valid value").font(.caption).foregroundStyle(.red)
                }
                Spacer()
                Button("Cancel") { close() }.keyboardShortcut(.cancelAction)
                Button("Download Later") { add(start: false) }.disabled(lines.isEmpty)
                Button("Start Download") { add(start: true) }.keyboardShortcut(.defaultAction).disabled(lines.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 560)
        .onAppear {
            text = request.links
            folder = manager.settings.folder
            queueID = request.queueID
        }
    }

    private func add(start: Bool) {
        // Torrent files, links and magnets open the Torrents window (file list, priorities, seeding) instead.
        let torrents = lines.map(TorrentInbox.normalized).filter(TorrentSource.isTorrent)
        if !torrents.isEmpty, torrents.count == lines.count {
            close()
            TorrentInbox.shared.source = torrents[0]
            return
        }
        var o = DownloadManager.AddOptions()
        o.folder = folder
        o.fileName = name.isEmpty ? nil : name
        o.queueID = queueID
        o.start = start
        o.username = username
        o.password = password
        o.checksum = Checksum.parse(checksum) == nil ? "" : checksum
        o.speedLimitKBps = max(0, limit)
        o.audioOnly = audioOnly
        manager.add(links: text, options: o)
        close()
    }

    private func close() {
        onClose()
        dismiss()
    }
}

// MARK: Batch

struct BatchSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    let queueID: String
    @State private var pattern = "https://example.com/photos/img[001-050].jpg"
    @State private var folder = URL(fileURLWithPath: NSHomeDirectory())
    @State private var queue = DownloadQueue.mainID
    @State private var message = ""

    private var preview: (Int, String, String) {
        let n = BatchPattern.count(pattern)
        guard n <= BatchPattern.limit, let all = try? BatchPattern.expand(pattern) else { return (n, "", "") }
        return (n, all.first ?? "", all.last ?? "")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Batch Download").font(.title2.bold())
            Text("Put a range in square brackets: [001-100] keeps three digits, [1-100] does not, [a-z] uses letters and [0-100:5] counts in fives. Several ranges multiply.")
                .foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            TextField("Pattern", text: $pattern).font(.body.monospaced()).textFieldStyle(.roundedBorder)
            let p = preview
            Group {
                if p.0 > BatchPattern.limit {
                    Text("\(p.0) links – the limit is \(BatchPattern.limit)").foregroundStyle(.red)
                } else {
                    Text("\(p.0) link\(p.0 == 1 ? "" : "s")")
                    if p.0 > 1 { Text("First: \(p.1)\nLast: \(p.2)").foregroundStyle(.secondary).textSelection(.enabled) }
                }
            }
            .font(.caption)
            Form { SaveOptions(folder: $folder, queueID: $queue) }.formStyle(.grouped)
            HStack {
                Text(message).font(.caption).foregroundStyle(.red)
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Download Later") { add(false) }
                Button("Start Download") { add(true) }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 600)
        .onAppear {
            folder = manager.settings.folder
            queue = queueID
        }
    }

    private func add(_ start: Bool) {
        var o = DownloadManager.AddOptions()
        o.folder = folder
        o.queueID = queue
        o.start = start
        do {
            _ = try manager.addBatch(pattern, options: o)
            dismiss()
        } catch {
            message = "Too many links. Make the ranges smaller."
        }
    }
}

// MARK: Scheduler

struct SchedulerSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    @State var selected: String
    static let dayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]

    private var index: Int? { manager.queues.firstIndex { $0.id == selected } }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Scheduler").font(.title2.bold())
            HStack(alignment: .top, spacing: 16) {
                VStack(alignment: .leading) {
                    List(manager.queues, selection: Binding<String?>(get: { selected }, set: { if let v = $0 { selected = v } })) { q in
                        Label(q.name, systemImage: q.running ? "play.circle.fill" : "pause.circle").tag(q.id)
                    }
                    .frame(width: 180, height: 300)
                    HStack {
                        Button { selected = manager.newQueue() } label: { Image(systemName: "plus") }.help("New queue").accessibilityLabel("New queue")
                        Button {
                            let old = selected
                            selected = DownloadQueue.mainID
                            manager.deleteQueue(old)
                        } label: { Image(systemName: "minus") }
                            .disabled(selected == DownloadQueue.mainID).help("Delete queue").accessibilityLabel("Delete queue")
                    }
                }
                if let i = index { editor(i) }
            }
            HStack {
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 680)
    }

    @ViewBuilder private func editor(_ i: Int) -> some View {
        let q = $manager.queues[i]
        Form {
            TextField("Name", text: q.name).disabled(manager.queues[i].isMain)
            Toggle("Start at", isOn: q.startEnabled)
            if manager.queues[i].startEnabled {
                DatePicker("Time", selection: timeBinding(q.startTime), displayedComponents: .hourAndMinute)
                HStack {
                    ForEach(1...7, id: \.self) { d in
                        Toggle(Self.dayNames[d - 1], isOn: Binding(
                            get: { manager.queues[i].days.contains(d) },
                            set: { on in
                                var days = Set(manager.queues[i].days)
                                if on { days.insert(d) } else { days.remove(d) }
                                manager.queues[i].days = days.sorted()
                            }))
                        .toggleStyle(.button)
                    }
                }
            }
            Toggle("Stop at", isOn: q.stopEnabled)
            if manager.queues[i].stopEnabled {
                DatePicker("Time", selection: timeBinding(q.stopTime), displayedComponents: .hourAndMinute)
            }
            if manager.queues[i].isMain {
                Stepper("Downloads at the same time: \(manager.settings.maxActive)", value: $manager.settings.maxActive, in: 1...10)
            } else {
                Stepper("Downloads at the same time: \(manager.queues[i].maxActive)", value: q.maxActive, in: 1...10)
            }
            Picker("When the queue is done", selection: q.afterDone) {
                ForEach(AfterQueue.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            LabeledContent("Files in this queue") {
                Text("\(manager.items.filter { $0.queue == selected && $0.status != .finished }.count) waiting")
            }
            HStack {
                Button("Start Now") { manager.startQueue(selected) }
                Button("Stop") { manager.stopQueue(selected) }
                Spacer()
                Text(manager.queues[i].running ? "Running" : "Stopped").foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private func timeBinding(_ text: Binding<String>) -> Binding<Date> {
        Binding(
            get: {
                let parts = text.wrappedValue.split(separator: ":").compactMap { Int($0) }
                return Calendar.current.date(bySettingHour: parts.first ?? 0, minute: parts.count > 1 ? parts[1] : 0, second: 0, of: Date()) ?? Date()
            },
            set: { text.wrappedValue = QueueClock.hhmm($0) })
    }
}

// MARK: Grabber

struct GrabberSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    let queueID: String
    @State private var start = ""
    @State private var depth = 1
    @State private var types = "zip rar 7z pdf mp3 mp4 jpg png"
    @State private var stayInFolder = true
    @State private var found: [URL] = []
    @State private var checked = Set<URL>()
    @State private var status = ""
    @State private var task: Task<Void, Never>?
    @State private var folder = URL(fileURLWithPath: NSHomeDirectory())
    @State private var queue = DownloadQueue.mainID

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Site Grabber").font(.title2.bold())
            Text("Explores a website section and lists the files it links to. Only use it on sites you are allowed to download from.")
                .foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Form {
                TextField("Start page", text: $start, prompt: Text("https://example.com/files/"))
                Stepper("Follow links \(depth) level\(depth == 1 ? "" : "s") deep", value: $depth, in: 0...5)
                TextField("File types (empty = every file)", text: $types)
                Toggle("Stay inside the start page's folder", isOn: $stayInFolder)
            }
            .formStyle(.grouped)
            HStack {
                Button(task == nil ? "Explore" : "Stop") { if task == nil { explore() } else { stop() } }
                    .disabled(DownloadJob(url: start).validURL == nil)
                Text(status).font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("All") { checked = Set(found) }.disabled(found.isEmpty)
                Button("None") { checked = [] }.disabled(found.isEmpty)
            }
            List(found, id: \.self) { u in
                Toggle(isOn: Binding(get: { checked.contains(u) }, set: { on in if on { checked.insert(u) } else { checked.remove(u) } })) {
                    Text(u.absoluteString).lineLimit(1).truncationMode(.middle)
                }
            }
            .frame(height: 180)
            Form { SaveOptions(folder: $folder, queueID: $queue) }.formStyle(.grouped)
            HStack {
                Spacer()
                Button("Close") { stop(); dismiss() }.keyboardShortcut(.cancelAction)
                Button("Download Later") { add(false) }.disabled(checked.isEmpty)
                Button("Download Checked") { add(true) }.keyboardShortcut(.defaultAction).disabled(checked.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 640)
        .onAppear {
            folder = manager.settings.folder
            queue = queueID
            start = clipboardLink()
        }
    }

    @MainActor private func explore() {
        guard let url = DownloadJob(url: start).validURL else { return }
        let o = GrabOptions(start: url, depth: depth, types: FileTypes.parse(types), stayInFolder: stayInFolder)
        let session = URLSession(configuration: Network.configuration(proxy: manager.settings.proxy))
        found = []
        checked = []
        status = "Exploring…"
        task = Task {
            let files = await SiteGrabber.explore(o, session: session) { pages, files in
                Task { @MainActor in status = "\(pages) pages read, \(files) files found" }
            }
            found = files
            checked = Set(files)
            status = "\(files.count) files found"
            task = nil
        }
    }

    private func stop() {
        task?.cancel()
        task = nil
    }

    private func add(_ start: Bool) {
        var o = DownloadManager.AddOptions()
        o.folder = folder
        o.queueID = queue
        o.start = start
        manager.add(links: found.filter(checked.contains).map(\.absoluteString).joined(separator: "\n"), options: o)
        dismiss()
    }
}

// MARK: Properties

struct PropertiesSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    let id: UUID
    @State private var username = ""
    @State private var password = ""
    @State private var checksum = ""
    @State private var limit = 0
    @State private var queue = DownloadQueue.mainID
    @State private var result = ""

    var body: some View {
        let r = manager.items.first { $0.id == id }
        VStack(alignment: .leading, spacing: 12) {
            Text("Properties").font(.title2.bold())
            if let r {
                Form {
                    LabeledContent("File", value: r.fileName)
                    LabeledContent("Link") { Text(r.url.absoluteString).lineLimit(2).textSelection(.enabled) }
                    LabeledContent("Folder", value: r.folder.path)
                    LabeledContent("Size", value: r.size.map(bytes) ?? "Unknown")
                    LabeledContent("Status", value: statusText(r))
                    Picker("Queue", selection: $queue) { ForEach(manager.queues) { q in Text(q.name).tag(q.id) } }
                    Section("Login") {
                        TextField("User name", text: $username)
                        SecureField("Password", text: $password)
                    }
                    Section("Checksum") {
                        TextField("Expected MD5, SHA-1, SHA-256 or SHA-512", text: $checksum)
                        if r.status == .finished {
                            HStack {
                                Button("Check Now") { result = manager.verify(id, expected: checksum) }.disabled(Checksum.parse(checksum) == nil)
                                Text(result.isEmpty ? (r.checksumResult ?? "") : result).font(.caption)
                            }
                        }
                    }
                    TextField("Speed limit in KB/s (0 = none)", value: $limit, format: .number)
                }
                .formStyle(.grouped)
            }
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Save") {
                    manager.setDetails(id, username: username, password: password,
                                       checksum: Checksum.parse(checksum) == nil ? "" : checksum,
                                       speedLimitKBps: max(0, limit), queueID: queue)
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 560)
        .onAppear {
            guard let r else { return }
            username = r.username ?? ""
            password = r.password ?? ""
            checksum = r.checksum ?? ""
            limit = r.speedLimitKBps ?? 0
            queue = r.queue
        }
    }
}

// MARK: Refresh link

struct RefreshSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    let id: UUID
    @State private var link = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Refresh Link").font(.title2.bold())
            Text("""
            Links from many sites stop working after a while. Paste a fresh link for the same file and the download \
            continues from where it stopped. Or choose Wait for Browser, then start the same download again in your \
            browser: the extension hands the new link over automatically.
            """)
            .foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            TextField("New link", text: $link).textFieldStyle(.roundedBorder)
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Wait for Browser") { manager.refresh(id, with: nil); dismiss() }
                Button("Use This Link") { manager.refresh(id, with: link); dismiss() }
                    .keyboardShortcut(.defaultAction).disabled(DownloadJob(url: link).validURL == nil)
            }
        }
        .padding(20)
        .frame(width: 520)
        .onAppear { link = clipboardLink() }
    }
}

// MARK: Download complete

@MainActor
enum CompleteWindow {
    private static var panels: [NSPanel] = []

    static func show(_ rec: DownloadRecord, manager: DownloadManager) {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 440, height: 180), styleMask: [.titled, .closable],
                            backing: .buffered, defer: false)
        panel.title = "Download Complete"
        panel.isReleasedWhenClosed = false
        let close = { [weak panel] in
            panel?.close()
            panels.removeAll { $0 === panel }
        }
        panel.contentView = NSHostingView(rootView: CompleteView(record: rec, close: close).environmentObject(manager))
        panel.center()
        panel.level = .floating
        panel.makeKeyAndOrderFront(nil)
        panels.append(panel)
    }
}

struct CompleteView: View {
    @EnvironmentObject var manager: DownloadManager
    let record: DownloadRecord
    let close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label(record.fileName, systemImage: record.category.symbol).font(.headline).lineLimit(1)
            Text("\(record.size.map(bytes) ?? "") saved to \(record.savedPath ?? "")")
                .font(.caption).foregroundStyle(.secondary).lineLimit(2).truncationMode(.middle)
            if let c = record.checksumResult { Text(c).font(.caption).foregroundStyle(c.contains("not") ? .red : .green) }
            Toggle("Don't show this again", isOn: Binding(get: { !manager.settings.showCompleteDialog },
                                                         set: { manager.settings.showCompleteDialog = !$0 }))
                .font(.caption)
            HStack {
                Spacer()
                Button("Close") { close() }.keyboardShortcut(.cancelAction)
                Button("Open Folder") { manager.reveal(record.id); close() }
                Button("Open") { manager.open(record.id); close() }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(16)
        .frame(width: 440)
        .tint(brand)
    }
}

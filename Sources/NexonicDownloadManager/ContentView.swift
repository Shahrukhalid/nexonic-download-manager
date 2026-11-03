import AppKit
import NexonicCore
import SwiftUI
import UniformTypeIdentifiers

enum SidebarFilter: Hashable {
    case all, active, unfinished, finished, category(FileCategory), queue(String)
    case unfinishedIn(FileCategory), finishedIn(FileCategory)  // IDM-style: a type inside Unfinished or Finished

    @MainActor func title(_ m: DownloadManager) -> String {
        switch self {
        case .all: return "All Downloads"
        case .active: return "In Progress"
        case .unfinished: return "Unfinished"
        case .finished: return "Finished"
        case .category(let c): return c.label
        case .unfinishedIn(let c): return "Unfinished · \(c.label)"
        case .finishedIn(let c): return "Finished · \(c.label)"
        case .queue(let id): return m.queueName(id)
        }
    }

    func matches(_ r: DownloadRecord) -> Bool {
        switch self {
        case .all: return true
        case .active: return r.status.isActive || r.status == .queued
        case .unfinished: return r.status != .finished
        case .finished: return r.status == .finished
        case .category(let c): return r.category == c
        case .unfinishedIn(let c): return r.status != .finished && r.category == c
        case .finishedIn(let c): return r.status == .finished && r.category == c
        case .queue(let id): return r.queue == id
        }
    }
}

extension FileCategory {
    /// Name in the sidebar: files of no particular type are "General", as in IDM.
    var label: String { self == .other ? "General" : rawValue }
}

enum Sheet: Identifiable {
    case add(AddRequest), batch, scheduler(String), grabber, properties(UUID), refresh(UUID)

    var id: String {
        switch self {
        case .add(let r): return "add-\(r.id)"
        case .batch: return "batch"
        case .scheduler: return "scheduler"
        case .grabber: return "grabber"
        case .properties(let id): return "properties-\(id)"
        case .refresh(let id): return "refresh-\(id)"
        }
    }
}

let brand = Color(red: 0.0, green: 0.56, blue: 0.62)

struct ContentView: View {
    @Environment(\.openWindow) private var openWindow
    @EnvironmentObject var manager: DownloadManager
    @State private var filter: SidebarFilter = .all
    @State private var selection = Set<UUID>()
    @State private var sheet: Sheet?
    @State private var search = ""
    @State private var openAll = true
    @State private var openUnfinished = false
    @State private var openFinished = false

    private func row(_ f: SidebarFilter) -> some View {
        Label(f.title(manager), systemImage: icon(f)).badge(count(f)).tag(f)
    }

    private func categoryRows(_ make: @escaping (FileCategory) -> SidebarFilter) -> some View {
        ForEach(FileCategory.allCases, id: \.self) { c in
            Label(c.label, systemImage: c.symbol).badge(count(make(c))).tag(make(c))
        }
    }

    private var shown: [DownloadRecord] {
        let q = search.trimmingCharacters(in: .whitespaces).lowercased()
        return Array(manager.items.filter { filter.matches($0) && (q.isEmpty || $0.fileName.lowercased().contains(q)
            || $0.url.absoluteString.lowercased().contains(q)) }.reversed())
    }

    private func count(_ f: SidebarFilter) -> Int { manager.items.filter(f.matches).count }
    private var currentQueue: String {
        if case .queue(let id) = filter { return id }
        return DownloadQueue.mainID
    }

    var body: some View {
        NavigationSplitView {
            List(selection: $filter) {
                // Like IDM: each list opens into the file types it holds (sorted automatically by file type).
                Section("Downloads") {
                    DisclosureGroup(isExpanded: $openAll) {
                        categoryRows(SidebarFilter.category)
                    } label: { row(.all) }
                    row(.active)
                    DisclosureGroup(isExpanded: $openUnfinished) {
                        categoryRows(SidebarFilter.unfinishedIn)
                    } label: { row(.unfinished) }
                    DisclosureGroup(isExpanded: $openFinished) {
                        categoryRows(SidebarFilter.finishedIn)
                    } label: { row(.finished) }
                }
                Section("Queues") {
                    ForEach(manager.queues) { q in
                        Label(q.name, systemImage: q.running ? "play.circle.fill" : "pause.circle")
                            .badge(count(.queue(q.id))).tag(SidebarFilter.queue(q.id))
                    }
                }
            }
            .navigationSplitViewColumnWidth(min: 180, ideal: 200)
        } detail: {
            VStack(spacing: 0) {
                if shown.isEmpty { emptyState } else { table }
                if selection.count == 1, let id = selection.first, let item = manager.items.first(where: { $0.id == id }) {
                    Divider()
                    DetailPane(item: item, bps: manager.speeds[id] ?? 0, eta: manager.eta(item), queue: manager.queueName(item.queue))
                }
                Divider()
                StatusBar()
            }
            .searchable(text: $search, placement: .toolbar, prompt: "Search downloads")
            .navigationTitle(filter.title(manager))
            .toolbar { toolbar }
        }
        .tint(brand)
        .sheet(item: $sheet) { s in sheetView(s).environmentObject(manager) }
        .onChange(of: manager.addRequest?.id) { _ in
            if let r = manager.addRequest { sheet = .add(r) }
        }
        .onOpenURL { _ in manager.collectInbox() }
        .onAppear {
            let open = openWindow
            DownloadManager.showMainWindow = {
                open(id: "main")
                NSApp.activate(ignoringOtherApps: true)
            }
        }
        .onAppear {
            Demo.start(manager) { name in
                switch name {
                case "add": sheet = .add(AddRequest(links: "https://example.com/files/course-lecture-09-1080p.mp4"))
                case "scheduler": sheet = .scheduler(Demo.nightQueueID)
                case "batch": sheet = .batch
                case "info": DownloadInfoWindow.show(Demo.infoJob, manager: manager)
                case "progress":
                    if let r = manager.items.first(where: { $0.fileName == Demo.liveName }) { ProgressWindow.show(r.id, manager: manager) }
                default: break
                }
            }
        }
        .onDrop(of: [.url, .plainText], isTargeted: nil) { providers in
            for p in providers {
                _ = p.loadObject(ofClass: URL.self) { url, _ in
                    if let url { Task { @MainActor in manager.addRequest = AddRequest(links: url.absoluteString) } }
                }
            }
            return true
        }
    }

    private var table: some View {
        Table(shown, selection: $selection) {
            TableColumn("Name") { r in
                HStack(spacing: 6) {
                    Image(nsImage: FileIcon.image(for: r.fileName)).resizable().frame(width: 16, height: 16)
                    Text(r.fileName).lineLimit(1).truncationMode(.middle)
                }
            }
            TableColumn("Size") { r in Text(r.size.map(bytes) ?? "–") }.width(80)
            TableColumn("Progress") { r in ProgressView(value: r.status == .finished ? 1 : r.progress).progressViewStyle(.linear).tint(r.status == .finished ? .green : brand) }.width(110)
            TableColumn("Speed") { r in Text(r.status.isActive ? speed(manager.speeds[r.id] ?? 0) : "") }.width(90)
            TableColumn("Time Left") { r in Text(r.status.isActive ? manager.eta(r) : "") }.width(70)
            TableColumn("Status") { r in
                HStack(spacing: 5) {
                    Circle().fill(statusColor(r)).frame(width: 7, height: 7)
                    Text(statusText(r)).foregroundStyle(r.status == .failed ? .red : .primary)
                }
            }.width(120)
            TableColumn("Queue") { r in Text(manager.queueName(r.queue)).foregroundStyle(.secondary) }.width(90)
            TableColumn("Added") { r in Text(r.added, style: .date).foregroundStyle(.secondary) }.width(90)
        }
        .contextMenu(forSelectionType: UUID.self) { ids in
            rowMenu(ids)
        } primaryAction: { ids in
            if let id = ids.first { manager.open(id) }
        }
    }

    private var emptyState: some View {
        VStack(spacing: 12) {
            Image(systemName: "arrow.down.circle").font(.system(size: 52, weight: .light)).foregroundStyle(brand)
            Text(search.isEmpty ? "No downloads here yet" : "Nothing matches “\(search)”").font(.title3.weight(.semibold))
            Text("Click Add URL, paste a link, drop one on this window, or download from your browser with the extension.")
                .foregroundStyle(.secondary).multilineTextAlignment(.center).frame(maxWidth: 380)
            Button { sheet = .add(AddRequest(links: clipboardLink(), queueID: currentQueue)) } label: {
                Label("Add URL", systemImage: "plus")
            }
            .buttonStyle(.borderedProminent).controlSize(.large)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(40)
    }

    @ToolbarContentBuilder private var toolbar: some ToolbarContent {
        ToolbarItemGroup {
            Button { sheet = .add(AddRequest(links: clipboardLink(), queueID: currentQueue)) } label: {
                Label("Add URL", systemImage: "plus.circle")
            }
            .keyboardShortcut("n")
            .help("Add one or more links")
            Button { selection.forEach(manager.resume) } label: { Label("Resume", systemImage: "play.fill") }
                .disabled(selection.isEmpty).help("Resume the selected downloads")
            Button { selection.forEach(manager.pause) } label: { Label("Stop", systemImage: "pause.fill") }
                .disabled(selection.isEmpty).help("Stop the selected downloads")
            Button { manager.pauseAll() } label: { Label("Stop All", systemImage: "stop.fill") }.help("Stop every download")
            Button { toggleSelectAll() } label: {
                Label(allSelected ? "Select None" : "Select All", systemImage: allSelected ? "checklist.unchecked" : "checklist.checked")
            }
            .disabled(shown.isEmpty).help(allSelected ? "Deselect all downloads" : "Select every download in this list")
            Button { confirmDelete(selection) } label: { Label("Delete", systemImage: "trash") }
                .disabled(selection.isEmpty).help("Remove the selected downloads from the list")
            Button { confirmClearFinished() } label: { Label("Delete Completed", systemImage: "checkmark.circle") }
                .help("Remove finished downloads from the list")
        }
        ToolbarItemGroup {
            Button { sheet = .scheduler(currentQueue) } label: { Label("Scheduler", systemImage: "calendar.badge.clock") }
                .help("Queues and timetables")
            Button { manager.startQueue(currentQueue) } label: { Label("Start Queue", systemImage: "play.circle") }
                .help("Start \(manager.queueName(currentQueue))")
            Button { manager.stopQueue(currentQueue) } label: { Label("Stop Queue", systemImage: "stop.circle") }
                .help("Stop \(manager.queueName(currentQueue))")
            Button { sheet = .grabber } label: { Label("Grabber", systemImage: "globe") }.help("Download files from a website section")
            Button { sheet = .batch } label: { Label("Batch", systemImage: "number") }.help("Download a numbered series of files")
            if #available(macOS 14, *) {
                SettingsLink { Label("Options", systemImage: "gearshape") }.help("Options")
            } else {
                Button { NSApp.sendAction(Selector(("showSettingsWindow:")), to: nil, from: nil) } label: {
                    Label("Options", systemImage: "gearshape")
                }
            }
        }
    }

    @ViewBuilder private func sheetView(_ s: Sheet) -> some View {
        switch s {
        case .add(let r): AddSheet(request: r, onClose: closeAdd)
        case .batch: BatchSheet(queueID: currentQueue)
        case .scheduler(let q): SchedulerSheet(selected: q)
        case .grabber: GrabberSheet(queueID: currentQueue)
        case .properties(let id): PropertiesSheet(id: id)
        case .refresh(let id): RefreshSheet(id: id)
        }
    }

    private func closeAdd() {
        manager.addRequest = nil
        sheet = nil
    }

    @ViewBuilder private func rowMenu(_ ids: Set<UUID>) -> some View {
        if ids.count == 1, let id = ids.first {
            Button("Open") { manager.open(id) }
            Button("Show in Finder") { manager.reveal(id) }
            Button("Show Progress") { ProgressWindow.show(id, manager: manager) }
            Divider()
        }
        Button("Resume") { ids.forEach(manager.resume) }
        Button("Stop") { ids.forEach(manager.pause) }
        if ids.count == 1, let id = ids.first {
            Button("Refresh Link…") { sheet = .refresh(id) }
            Button("Properties…") { sheet = .properties(id) }
            Button("Copy Link") {
                if let u = manager.items.first(where: { $0.id == id })?.url {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(u.absoluteString, forType: .string)
                }
            }
        }
        Button(ids.count == 1 ? "Show QR Code…" : "Show QR Code for \(ids.count) Downloads…") {
            QrWindow.show(manager.items.filter { ids.contains($0.id) }.map(\.url))
        }
        Menu("Move to Queue") {
            ForEach(manager.queues) { q in
                Button(q.name) { moveTo(ids, q.id) }
            }
        }
        Divider()
        Button("Delete…") { confirmDelete(ids) }
        Button("Delete and Move File to Trash…") { confirmDelete(ids, deleteFile: true) }
    }

    private var allSelected: Bool { !shown.isEmpty && selection.isSuperset(of: shown.map(\.id)) }

    private func toggleSelectAll() {
        selection = allSelected ? [] : Set(shown.map(\.id))
    }

    /// Asks before anything is removed. Files stay on the Mac unless the user ticks the box (or chose the Trash item).
    private func confirmDelete(_ ids: Set<UUID>, deleteFile: Bool = false) {
        guard !ids.isEmpty else { return }
        let n = ids.count
        let what = n == 1 ? "this download" : "these \(n) downloads"
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = deleteFile ? "Delete \(what) and move the \(n == 1 ? "file" : "files") to the Trash?"
                                       : "Remove \(what) from the list?"
        alert.informativeText = deleteFile ? "You can still restore the files from the Trash."
                                           : "The downloaded files stay on your Mac."
        alert.addButton(withTitle: deleteFile ? "Move to Trash" : "Remove")
        alert.addButton(withTitle: "Cancel")
        if !deleteFile {
            alert.showsSuppressionButton = true
            alert.suppressionButton?.title = "Also move the downloaded files to the Trash"
        }
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        let trash = deleteFile || alert.suppressionButton?.state == .on
        ids.forEach { manager.remove($0, deleteFile: trash) }
        selection.subtract(ids)
    }

    private func confirmClearFinished() {
        let ids = manager.items.filter { $0.status == .finished }.map(\.id)  // only the ones counted in the question
        let n = ids.count
        guard n > 0 else { return }
        let alert = NSAlert()
        alert.messageText = "Remove \(n) completed \(n == 1 ? "download" : "downloads") from the list?"
        alert.informativeText = "The downloaded files stay on your Mac."
        alert.addButton(withTitle: "Remove")
        alert.addButton(withTitle: "Cancel")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        ids.forEach { manager.remove($0) }
        selection.subtract(ids)
    }

    private func moveTo(_ ids: Set<UUID>, _ queue: String) {
        for id in ids {
            guard let r = manager.items.first(where: { $0.id == id }) else { continue }
            manager.setDetails(id, username: r.username ?? "", password: r.password ?? "", checksum: r.checksum ?? "",
                               speedLimitKBps: r.speedLimitKBps ?? 0, queueID: queue)
        }
    }

    private func icon(_ f: SidebarFilter) -> String {
        switch f {
        case .all: return "tray.full"
        case .active: return "arrow.down.circle"
        case .unfinished: return "clock"
        case .finished: return "checkmark.circle"
        case .category(let c), .unfinishedIn(let c), .finishedIn(let c): return c.symbol
        case .queue: return "list.bullet"
        }
    }
}

func statusColor(_ r: DownloadRecord) -> Color {
    switch r.status {
    case .downloading, .probing: return brand
    case .queued: return .orange
    case .paused: return .gray
    case .finished: return r.checksumResult?.contains("not") == true ? .red : .green
    case .failed: return .red
    }
}

/// The system's own icon for each file type, cached by extension.
@MainActor enum FileIcon {
    private static var cache: [String: NSImage] = [:]

    static func image(for name: String) -> NSImage {
        let ext = (name as NSString).pathExtension.lowercased()
        if let i = cache[ext] { return i }
        let icon = NSWorkspace.shared.icon(for: UTType(filenameExtension: ext) ?? .data)
        cache[ext] = icon
        return icon
    }
}

/// Totals along the bottom of the window.
struct StatusBar: View {
    @EnvironmentObject var manager: DownloadManager

    var body: some View {
        let active = manager.items.filter { $0.status.isActive }.count
        let queued = manager.items.filter { $0.status == .queued }.count
        let total = manager.speeds.values.reduce(0, +)
        HStack(spacing: 14) {
            Label("\(active) downloading", systemImage: "arrow.down.circle")
            Label("\(queued) queued", systemImage: "clock")
            if total > 0 { Label(speed(total), systemImage: "speedometer") }
            Spacer()
            if manager.settings.speedLimitKBps > 0 { Label("Limit \(manager.settings.speedLimitKBps) KB/s", systemImage: "tortoise") }
            Text("\(manager.queues.filter(\.running).count) of \(manager.queues.count) queues running")
        }
        .labelStyle(.titleAndIcon)
        .font(.caption)
        .foregroundStyle(.secondary)
        .padding(.horizontal, 12)
        .padding(.vertical, 5)
        .background(.bar)
    }
}

@MainActor func clipboardLink() -> String {
    guard let s = NSPasteboard.general.string(forType: .string)?.trimmingCharacters(in: .whitespacesAndNewlines),
          DownloadJob(url: s).validURL != nil else { return "" }
    return s
}

func bytes(_ n: Int64) -> String { ByteCountFormatter.string(fromByteCount: n, countStyle: .file) }
func speed(_ bps: Double) -> String { bps > 0 ? "\(bytes(Int64(bps)))/s" : "" }

/// Video sites and streams take a few seconds to look up the best quality before the first byte arrives.
func isPreparingVideo(_ r: DownloadRecord) -> Bool {
    r.status.isActive && (r.siteVideo == true || r.isStream == true) && r.received == 0
}

func statusText(_ r: DownloadRecord) -> String {
    if isPreparingVideo(r) { return "Preparing video…" }
    switch r.status {
    case .queued: return "Queued"
    case .probing: return "Connecting…"
    case .downloading: return "\(Int(r.progress * 100))%"
    case .paused: return r.awaitingRefresh == true ? "Waiting for link" : "Stopped"
    case .finished: return r.checksumResult.map { $0.contains("not") ? "Checksum failed" : "Complete" } ?? "Complete"
    case .failed: return "Error"
    }
}

struct DetailPane: View {
    let item: DownloadRecord
    let bps: Double
    let eta: String
    let queue: String

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(item.fileName).font(.headline).lineLimit(1)
            Text(item.url.absoluteString).font(.caption).foregroundStyle(.secondary).lineLimit(1).textSelection(.enabled)
            if item.isStream == true || item.siteVideo == true {
                ProgressView(value: item.status == .finished ? 1 : item.progress).tint(brand)
            } else {
                SegmentBar(segments: item.segments, size: item.size).frame(height: 14)
            }
            HStack(spacing: 16) {
                Text(item.isStream == true && item.status != .finished ? "\(bytes(item.received)) so far (streamed video, best quality)"
                 : "\(bytes(item.received)) of \(item.size.map(bytes) ?? "unknown size")")
                if item.status.isActive {
                    Text(speed(bps))
                    if !eta.isEmpty { Text("\(eta) left") }
                    if item.siteVideo == true {
                    Text("Video site · " + SiteVideo.choiceText(audioOnly: item.audioOnly, maxHeight: item.maxHeight))
                } else if item.isStream == true {
                    Text("Video pieces: \(item.streamDone ?? 0) of \(item.streamTotal ?? 0)")
                } else {
                    Text("Connections: \(item.segments.filter { !$0.isComplete }.count)")
                }
                }
                Text(item.acceptsRanges || item.status == .queued ? "Resume: yes" : "Resume: no")
                Text("Queue: \(queue)")
                if let l = item.speedLimitKBps { Text("Limit: \(l) KB/s") }
            }
            .font(.caption)
            if let c = item.checksumResult { Text(c).font(.caption).foregroundStyle(c.contains("not") ? .red : .green) }
            if let e = item.error { Text(e).font(.caption).foregroundStyle(.red).lineLimit(2) }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// One bar for the whole file; each range shows how much of it has arrived.
struct SegmentBar: View {
    let segments: [Segment]
    let size: Int64?

    var body: some View {
        Canvas { ctx, sz in
            ctx.fill(Path(roundedRect: CGRect(origin: .zero, size: sz), cornerRadius: 3), with: .color(.secondary.opacity(0.2)))
            guard let size, size > 0 else { return }
            for s in segments {
                let x = sz.width * CGFloat(s.start) / CGFloat(size)
                let w = sz.width * CGFloat(s.written) / CGFloat(size)
                ctx.fill(Path(CGRect(x: x, y: 0, width: max(w, 0.5), height: sz.height)),
                         with: .color(s.isComplete ? .green : brand))
            }
        }
    }
}

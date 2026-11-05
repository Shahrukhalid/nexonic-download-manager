import NexonicKit
import SwiftUI
import UIKit
import UniformTypeIdentifiers

/// A magnet link or .torrent file to show in the Add Torrent window.
struct TorrentRequest: Identifiable { let id = UUID(); let source: String }

/// One torrent on iPhone or iPad. Torrents download while the app is open; when it goes to the background they pause
/// and save a checkpoint, and continue from there when the app is opened again.
struct TorrentItem: Codable, Identifiable, Equatable {
    enum State: String, Codable { case downloading, paused, complete, failed }
    var id = UUID()
    var name: String
    var task: TorrentTask
    var state: State = .paused
    var received: Int64 = 0
    var wanted: Int64 = 0
    var error: String?
    /// Where the files are in Files › Nexonic, relative to the app's Documents folder.
    var savedFolder: String?
    /// Paused because the app went to the background (continues when it is opened again).
    var autoPaused = false
    var progress: Double { wanted > 0 ? min(1, Double(received) / Double(wanted)) : 0 }
}

@Observable
final class TorrentStore {
    static let shared = TorrentStore()
    var items: [TorrentItem] = []
    var speeds: [UUID: Int64] = [:]
    var peers: [UUID: String] = [:]
    @ObservationIgnored private var workers: [UUID: TorrentWorker] = [:]
    @ObservationIgnored private var background: UIBackgroundTaskIdentifier = .invalid

    private static var root: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Torrents", isDirectory: true)
    }
    private static var listURL: URL { root.appendingPathComponent("torrents.json") }
    private func folder(_ id: UUID) -> URL { Self.root.appendingPathComponent(id.uuidString, isDirectory: true) }

    init() {
        if let data = try? Data(contentsOf: Self.listURL), let saved = try? JSONDecoder().decode([TorrentItem].self, from: data) {
            items = saved.map { var i = $0; if i.state == .downloading { i.state = .paused; i.autoPaused = true }; return i }
        }
    }

    var isEnabled: Bool {
        get { UserDefaults.standard.bool(forKey: "torrentsEnabled") }
        set { UserDefaults.standard.set(newValue, forKey: "torrentsEnabled") }
    }

    private func save() {
        try? FileManager.default.createDirectory(at: Self.root, withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(items) { try? data.write(to: Self.listURL, options: .atomic) }
    }

    private func update(_ id: UUID, _ change: (inout TorrentItem) -> Void) {
        guard let i = items.firstIndex(where: { $0.id == id }) else { return }
        change(&items[i]); save()
    }

    func add(_ task: TorrentTask, start: Bool) {
        let item = TorrentItem(name: task.metadata?.name ?? "Torrent", task: task, wanted: task.selectedSize)
        items.insert(item, at: 0); save()
        if start { resume(item.id) }
    }

    func resume(_ id: UUID) {
        guard workers[id] == nil, let item = items.first(where: { $0.id == id }), item.state != .complete else { return }
        update(id) { $0.state = .downloading; $0.error = nil; $0.autoPaused = false }
        let worker = TorrentWorker()
        workers[id] = worker
        let base = folder(id)
        worker.start(id: "payload", task: item.task, destination: base.appendingPathComponent("data"), stateDirectory: base.appendingPathComponent("state")) { [weak self] result in
            DispatchQueue.main.async { self?.changed(id, result) }
        }
    }

    private func changed(_ id: UUID, _ result: Result<TorrentSnapshot, Error>) {
        switch result {
        case .failure(let error):
            workers[id] = nil; speeds[id] = nil
            if error is CancellationError { update(id) { if $0.state == .downloading { $0.state = .paused } } }
            else { update(id) { $0.state = .failed; $0.error = error.localizedDescription } }
        case .success(let snapshot):
            speeds[id] = snapshot.downloadSpeed
            peers[id] = "\(snapshot.seedsText) seeds · \(snapshot.peersText) peers"
            guard let i = items.firstIndex(where: { $0.id == id }) else { return }
            items[i].received = snapshot.received
            items[i].wanted = snapshot.wantedSize
            if snapshot.payloadComplete && items[i].state != .complete {
                workers[id]?.pause(); workers[id] = nil; speeds[id] = nil
                finish(id, snapshot)
            } else if snapshot.state == "paused" {
                workers[id] = nil; speeds[id] = nil
                if items[i].state == .downloading { items[i].state = .paused }
                save()
            }
        }
    }

    /// Moves the downloaded files into Files › Nexonic › Torrents › <name>.
    private func finish(_ id: UUID, _ snapshot: TorrentSnapshot) {
        guard let item = items.first(where: { $0.id == id }) else { return }
        let fm = FileManager.default
        let source = folder(id).appendingPathComponent("data")
        let torrents = Library.documents.appendingPathComponent("Torrents", isDirectory: true)
        try? fm.createDirectory(at: torrents, withIntermediateDirectories: true)
        let target = FileNames.unique(FileNames.sanitize(item.name), in: torrents)
        // Give the engine a moment to close its files before they move.
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [self] in
            do {
                let top = (try? fm.contentsOfDirectory(at: source, includingPropertiesForKeys: nil)) ?? []
                if top.count == 1, let only = top.first { try fm.moveItem(at: only, to: target) }
                else { try fm.moveItem(at: source, to: target) }
                try? fm.removeItem(at: folder(id))
                update(id) { $0.state = .complete; $0.received = $0.wanted; $0.savedFolder = "Torrents/" + target.lastPathComponent }
            } catch {
                update(id) { $0.state = .failed; $0.error = "Could not save to Files: \(error.localizedDescription)" }
            }
        }
    }

    func pause(_ id: UUID) {
        workers[id]?.pause()
        update(id) { if $0.state == .downloading { $0.state = .paused } }
    }

    func remove(_ id: UUID) {
        workers[id]?.discard(); workers[id] = nil
        let path = folder(id)
        DispatchQueue.main.asyncAfter(deadline: .now() + 3) { try? FileManager.default.removeItem(at: path) }
        items.removeAll { $0.id == id }; save()
    }

    /// Going to the background: pause and save checkpoints (iOS allows a little time for it).
    func enterBackground() {
        let running = items.filter { $0.state == .downloading }
        guard !running.isEmpty else { return }
        background = UIApplication.shared.beginBackgroundTask(withName: "Save torrent checkpoints") { [weak self] in
            guard let self else { return }
            UIApplication.shared.endBackgroundTask(self.background); self.background = .invalid
        }
        for item in running { workers[item.id]?.pause(); update(item.id) { $0.state = .paused; $0.autoPaused = true } }
        DispatchQueue.main.asyncAfter(deadline: .now() + 8) { [weak self] in
            guard let self, self.background != .invalid else { return }
            UIApplication.shared.endBackgroundTask(self.background); self.background = .invalid
        }
    }

    /// Back in the app: continue the torrents that were paused for the background.
    func enterForeground() {
        for item in items where item.autoPaused && item.state == .paused { resume(item.id) }
    }
}

/// Add Torrent: a magnet link or a .torrent file. Its files are read first; nothing is downloaded until Download.
struct TorrentAddView: View {
    @Environment(\.dismiss) private var dismiss
    @State var source: String
    @State private var snapshot: TorrentSnapshot?
    @State private var chosen: [Bool] = []
    @State private var loading = false
    @State private var error: String?
    @State private var enabled = TorrentStore.shared.isEnabled
    @State private var importing = false
    var done: (String?) -> Void

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Toggle("Torrents on", isOn: $enabled)
                        .onChange(of: enabled) { _, on in TorrentStore.shared.isEnabled = on; if on { load() } }
                    Text("Peers and trackers can see your IP address. Torrents download while Nexonic is open and continue when you come back.")
                        .font(.footnote).foregroundStyle(.secondary)
                }
                Section("Torrent") {
                    TextField("Magnet link", text: $source, axis: .vertical).lineLimit(1...4)
                        .textInputAutocapitalization(.never).autocorrectionDisabled()
                    HStack {
                        Button("Choose .torrent File…") { importing = true }
                        Spacer()
                        Button("Load") { load() }.disabled(source.isEmpty || !enabled || loading)
                    }
                    if loading { HStack { ProgressView(); Text("Finding the torrent's files…").foregroundStyle(.secondary) } }
                    if let error { Text(error).foregroundStyle(.red).font(.footnote) }
                }
                if let snapshot, let files = snapshot.files {
                    Section("\(snapshot.name ?? "Files") · \(ByteCountFormatter.string(fromByteCount: selected(files), countStyle: .file))") {
                        ForEach(files.filter { !$0.pad }) { file in
                            Toggle(isOn: Binding(get: { chosen.indices.contains(file.index) && chosen[file.index] },
                                                 set: { if chosen.indices.contains(file.index) { chosen[file.index] = $0 } })) {
                                VStack(alignment: .leading) {
                                    Text(file.path).lineLimit(2)
                                    Text(ByteCountFormatter.string(fromByteCount: file.size, countStyle: .file)).font(.caption).foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                }
            }
            .navigationTitle("Add Torrent")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss(); done(nil) } }
                ToolbarItem(placement: .confirmationAction) { Button("Download") { add() }.disabled(snapshot == nil) }
            }
            .fileImporter(isPresented: $importing, allowedContentTypes: [UTType(filenameExtension: "torrent") ?? .data]) { result in
                if case .success(let url) = result, let local = TorrentAddView.copyIn(url) { source = local.path; load() }
            }
            .onAppear { if enabled && !source.isEmpty { load() } }
        }
    }

    private func selected(_ files: [TorrentFile]) -> Int64 {
        files.filter { !$0.pad && chosen.indices.contains($0.index) && chosen[$0.index] }.reduce(0) { $0 + $1.size }
    }

    private func load() {
        guard enabled, !source.isEmpty, !loading else { return }
        let text = source.trimmingCharacters(in: .whitespacesAndNewlines)
        guard TorrentSource.isTorrent(text) else { error = "Enter a magnet link or choose a .torrent file."; return }
        loading = true; error = nil; snapshot = nil
        let state = FileManager.default.temporaryDirectory.appendingPathComponent("torrent-preview")
        Task {
            do {
                let found = try await TorrentSource.preview(text, stateDirectory: state)
                await MainActor.run { snapshot = found; chosen = found.files?.map { !$0.pad } ?? []; loading = false }
            } catch {
                await MainActor.run { self.error = error.localizedDescription; loading = false }
            }
        }
    }

    private func add() {
        guard let snapshot else { return }
        var task = TorrentTask(source: source.trimmingCharacters(in: .whitespacesAndNewlines), metadata: snapshot)
        task.priorities = (snapshot.files ?? []).map { !$0.pad && chosen.indices.contains($0.index) && chosen[$0.index] ? 4 : 0 }
        task.options.seed = false
        do { _ = try task.selectedPriorities() } catch { self.error = error.localizedDescription; return }
        TorrentStore.shared.add(task, start: true)
        dismiss(); done("Torrent started")
    }

    /// A .torrent file opened from Files or another app, copied into the app so it stays readable.
    static func copyIn(_ url: URL) -> URL? {
        let access = url.startAccessingSecurityScopedResource()
        defer { if access { url.stopAccessingSecurityScopedResource() } }
        guard let data = try? Data(contentsOf: url), data.count < 8 * 1024 * 1024 else { return nil }
        let target = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".torrent")
        return (try? data.write(to: target)) != nil ? target : nil
    }
}

/// The torrents section at the top of the downloads list.
struct TorrentRows: View {
    @State private var store = TorrentStore.shared

    var body: some View {
        if !store.items.isEmpty {
            Section("Torrents") {
                ForEach(store.items) { item in
                    VStack(alignment: .leading, spacing: 4) {
                        Text(item.name).lineLimit(1)
                        if item.state != .complete { ProgressView(value: item.progress) }
                        Text(caption(item)).font(.caption).foregroundStyle(item.state == .failed ? .red : .secondary).lineLimit(2)
                    }
                    .swipeActions(edge: .trailing) {
                        Button(role: .destructive) { store.remove(item.id) } label: { Label("Remove", systemImage: "trash") }
                    }
                    .swipeActions(edge: .leading) {
                        if item.state == .downloading { Button { store.pause(item.id) } label: { Label("Pause", systemImage: "pause.fill") }.tint(.orange) }
                        else if item.state != .complete { Button { store.resume(item.id) } label: { Label("Resume", systemImage: "play.fill") }.tint(.green) }
                    }
                }
            }
        }
    }

    private func caption(_ item: TorrentItem) -> String {
        let size = ByteCountFormatter.string(fromByteCount: item.wanted, countStyle: .file)
        switch item.state {
        case .complete: return "Complete · \(size) · Files › Nexonic › \(item.savedFolder ?? "Torrents")"
        case .failed: return item.error ?? "Failed"
        case .paused: return "Paused · \(Int(item.progress * 100))% of \(size)" + (item.autoPaused ? " · continues when Nexonic is open" : "")
        case .downloading:
            let speed = ByteCountFormatter.string(fromByteCount: store.speeds[item.id] ?? 0, countStyle: .file)
            return "\(Int(item.progress * 100))% of \(size) · \(speed)/s · \(store.peers[item.id] ?? "finding peers…")"
        }
    }
}

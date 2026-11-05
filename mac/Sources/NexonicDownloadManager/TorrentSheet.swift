import AppKit
import NexonicCore
import NexonicTorrent
import SwiftUI
import UniformTypeIdentifiers

/// Add Torrent: a .torrent file, an HTTP(S) torrent link or a magnet. Metadata is previewed first (a preview never
/// downloads content), then the files, priorities, folder, queue, limits and seeding are chosen.
struct TorrentSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    let queueID: String
    var initialSource = ""

    @State private var source = ""
    @State private var snapshot: TorrentSnapshot?
    @State private var priorities: [Int] = []
    @State private var folder = URL(fileURLWithPath: NSHomeDirectory())
    @State private var queue = DownloadQueue.mainID
    @State private var options = TorrentOptions()
    @State private var downloadLimit = 0
    @State private var loading: Task<Void, Never>?
    @State private var message = ""

    private static let levels = [0, 1, 4, 7]
    private static func levelName(_ value: Int) -> String { ["Skip", "Low", "", "", "Normal", "", "", "High"][value] }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("Add Torrent").font(.title2.bold())
                Spacer()
                Toggle("Torrents on", isOn: $manager.settings.torrentsEnabled).toggleStyle(.switch)
                    .help("Turn torrents on or off. Peers and trackers can see your IP address.")
            }
            if let reason = TorrentPolicy.unavailable(manager.settings) {
                HStack {
                    Image(systemName: "exclamationmark.triangle").foregroundStyle(.orange)
                    Text(reason).fixedSize(horizontal: false, vertical: true)
                }
                .font(.callout)
            }
            HStack {
                TextField("Magnet link, torrent link or .torrent file", text: $source).textFieldStyle(.roundedBorder)
                    .onSubmit(load)
                Button("Choose File…", action: chooseFile)
                Button(loading == nil ? "Load" : "Cancel") { loading == nil ? load() : cancelLoad() }
                    .disabled(source.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            if loading != nil {
                HStack { ProgressView().controlSize(.small); Text("Finding the torrent's files…").foregroundStyle(.secondary) }
            }
            if let snapshot, let files = snapshot.files {
                VStack(alignment: .leading, spacing: 2) {
                    Text(snapshot.name ?? "Torrent").font(.headline).lineLimit(1).truncationMode(.middle)
                    Text("\(files.filter { !$0.pad }.count) files · selected \(format(selectedSize(files)))")
                        .font(.caption).foregroundStyle(.secondary)
                    if let hash = snapshot.infoHashV1, !hash.isEmpty {
                        Text(hash).font(.caption.monospaced()).foregroundStyle(.secondary).textSelection(.enabled)
                    }
                }
                List {
                    ForEach(files.filter { !$0.pad }) { file in
                        HStack {
                            Text(file.path).lineLimit(1).truncationMode(.middle)
                            Spacer()
                            Text(format(file.size)).foregroundStyle(.secondary).monospacedDigit()
                            Picker("", selection: priorityBinding(file.index)) {
                                ForEach(Self.levels, id: \.self) { Text(Self.levelName($0)).tag($0) }
                            }
                            .labelsHidden().frame(width: 100)
                        }
                    }
                }
                .frame(minHeight: 120, maxHeight: 200)
                Form {
                    SaveOptions(folder: $folder, queueID: $queue)
                    LabeledContent("Download limit") { limitField($downloadLimit) }
                    LabeledContent("Upload limit") { limitField($options.uploadLimitKBps) }
                    TorrentConnectionFields(options: $options)
                    Toggle("Find peers with DHT", isOn: $options.dht)
                    Toggle("Keep sharing (seeding) after the download", isOn: $options.seed)
                    if options.seed {
                        LabeledContent("Stop at ratio") {
                            TextField("", value: $options.ratioLimit, format: .number.precision(.fractionLength(0...2)))
                                .frame(width: 70).help("0 means no ratio limit")
                        }
                        LabeledContent("Stop after (minutes)") {
                            TextField("", value: minutesBinding, format: .number).frame(width: 70).help("0 means no time limit")
                        }
                    }
                }
                .formStyle(.grouped)
                Text("Peers and trackers can see your IP address. Files are checked piece by piece and get a SHA-256 when finished.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            HStack {
                Text(message).font(.caption).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true)
                Spacer()
                Button("Cancel") { cancelLoad(); dismiss() }.keyboardShortcut(.cancelAction)
                Button("Download Later") { add(false) }.disabled(snapshot == nil)
                Button("Start Download") { add(true) }.keyboardShortcut(.defaultAction).disabled(snapshot == nil)
            }
        }
        .padding(20)
        .frame(width: 640)
        .onAppear {
            folder = manager.settings.folder
            queue = queueID
            if source.isEmpty { source = initialSource }
            if !source.isEmpty, TorrentPolicy.unavailable(manager.settings) == nil { load() }
        }
        .onChange(of: manager.settings.torrentsEnabled) { on in
            if on, snapshot == nil, loading == nil, !source.isEmpty { load() }  // opened before torrents were on
        }
        .onDisappear { loading?.cancel() }
    }

    private var minutesBinding: Binding<Int> {
        Binding(get: { options.seedSeconds / 60 }, set: { options.seedSeconds = max(0, $0) * 60 })
    }

    private func priorityBinding(_ index: Int) -> Binding<Int> {
        Binding(get: { priorities.indices.contains(index) ? priorities[index] : 4 },
                set: { if priorities.indices.contains(index) { priorities[index] = $0 } })
    }

    private func limitField(_ value: Binding<Int>) -> some View {
        HStack {
            TextField("", value: value, format: .number).frame(width: 80)
            Text("KB/s (0 = no limit)").foregroundStyle(.secondary)
        }
    }

    private func selectedSize(_ files: [TorrentFile]) -> Int64 {
        zip(files, priorities).filter { $0.1 > 0 && !$0.0.pad }.reduce(0) { $0 + $1.0.size }
    }

    private func format(_ bytes: Int64) -> String { ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file) }

    private func chooseFile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "torrent") ?? .data]
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url {
            source = url.path
            load()
        }
    }

    private func load() {
        message = ""
        if let reason = TorrentPolicy.unavailable(manager.settings) { message = reason; return }
        let text = TorrentInbox.normalized(source)
        guard TorrentSource.isTorrent(text) else { message = "Enter a magnet link, a link to a .torrent file or choose a .torrent file."; return }
        loading?.cancel()
        snapshot = nil
        let state = AppPaths.parts.appendingPathComponent("torrent-preview")
        loading = Task {
            do {
                let found = try await TorrentSource.preview(text, stateDirectory: state)
                snapshot = found
                priorities = found.files?.map { $0.pad ? 0 : 4 } ?? []
            } catch is CancellationError {
            } catch {
                message = error.localizedDescription
            }
            loading = nil
        }
    }

    private func cancelLoad() {
        loading?.cancel()
        loading = nil
    }

    private func add(_ start: Bool) {
        guard let snapshot else { return }
        var task = TorrentTask(source: TorrentInbox.normalized(source), metadata: snapshot)
        task.priorities = priorities
        task.options = options
        do {
            try options.validate()
            guard (0...2_000_000).contains(downloadLimit) else { throw TorrentFailure("Enter a download limit between 0 and 2,000,000 KB/s.") }
            try manager.addTorrent(task, folder: folder, queueID: queue, limit: downloadLimit, start: start)
            dismiss()
        } catch {
            message = error.localizedDescription
        }
    }
}

/// Torrents handed to the app from outside the Torrents window: a .torrent file opened in Finder, a magnet link
/// clicked in a browser, or a torrent pasted in Add URL. The main window shows them in the Torrents window.
@MainActor
final class TorrentInbox: ObservableObject {
    static let shared = TorrentInbox()
    @Published var source: String?

    /// Takes a .torrent file or magnet link; false for anything else.
    @discardableResult
    func accept(_ url: URL) -> Bool {
        if url.isFileURL, url.pathExtension.lowercased() == "torrent" { source = url.path; return true }
        if url.scheme?.lowercased() == "magnet" { source = url.absoluteString; return true }
        return false
    }

    /// A pasted or dropped file:// link becomes a plain path; everything else is only trimmed.
    nonisolated static func normalized(_ text: String) -> String {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.lowercased().hasPrefix("file://"), let url = URL(string: t), url.isFileURL { return url.path }
        return t
    }
}

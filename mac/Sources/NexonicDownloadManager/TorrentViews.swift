import AppKit
import Combine
import NexonicCore
import NexonicTorrent
import SwiftUI

/// Connections per torrent, upload slots and download order, shared by Add Torrent and Torrent Settings.
struct TorrentConnectionFields: View {
    @Binding var options: TorrentOptions

    var body: some View {
        LabeledContent("Connections") {
            HStack {
                TextField("", value: $options.maxConnections, format: .number).frame(width: 80)
                Text("per torrent (0 = no limit)").foregroundStyle(.secondary)
            }
        }
        .help("How many peers this torrent may connect to at once.")
        LabeledContent("Upload slots") {
            HStack {
                TextField("", value: $options.maxUploads, format: .number).frame(width: 80)
                Text("peers you upload to at once (0 = no limit)").foregroundStyle(.secondary)
            }
        }
        Toggle("Download pieces in order (for watching while it downloads)", isOn: $options.sequential)
    }
}

/// The torrent's details under the list, with tabs like a BitTorrent client: General, Trackers, Peers and Files.
struct TorrentDetailPane: View {
    @EnvironmentObject var manager: DownloadManager
    let item: DownloadRecord
    @State private var tab = Tab.general
    @State private var peers: [TorrentPeer] = []
    private let timer = Timer.publish(every: 2, on: .main, in: .common).autoconnect()

    enum Tab: String, CaseIterable { case general = "General", trackers = "Trackers", peers = "Peers", files = "Files" }

    private var snapshot: TorrentSnapshot? { item.torrent?.metadata }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(item.fileName).font(.headline).lineLimit(1).truncationMode(.middle)
                Spacer()
                Picker("", selection: $tab) { ForEach(Tab.allCases, id: \.self) { Text($0.rawValue).tag($0) } }
                    .pickerStyle(.segmented).labelsHidden().frame(width: 320)
            }
            switch tab {
            case .general: general
            case .trackers: trackers
            case .peers: peerList
            case .files: files
            }
            if let e = item.error { Text(e).font(.caption).foregroundStyle(.red).lineLimit(2) }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .frame(height: 210)
        .onReceive(timer) { _ in if tab == .peers { peers = manager.torrentPeers(item.id) } }
        .onChange(of: tab) { value in if value == .peers { peers = manager.torrentPeers(item.id) } }
    }

    private var general: some View {
        let s = snapshot
        return VStack(alignment: .leading, spacing: 8) {
            PieceBar(map: s?.pieceMap ?? [], done: item.status == .finished || item.status == .seeding).frame(height: 12)
            Grid(alignment: .leading, horizontalSpacing: 18, verticalSpacing: 4) {
                GridRow {
                    field("Downloaded", bytes(s?.downloaded ?? item.received))
                    field("Download speed", rate(s?.downloadSpeed))
                    field("Seeds", s?.seedsText ?? "–")
                }
                GridRow {
                    field("Uploaded", bytes(s?.uploaded ?? 0))
                    field("Upload speed", rate(s?.uploadSpeed))
                    field("Peers", s?.peersText ?? "–")
                }
                GridRow {
                    field("Ratio", String(format: "%.3f", s?.ratio ?? 0))
                    field("Time left", eta(s))
                    field("Connections", connections(s))
                }
                GridRow {
                    field("Availability", s?.availability.map { $0 >= 0 ? String(format: "%.3f", $0) : "–" } ?? "–")
                    field("Pieces", pieces(s))
                    field("Time active", duration(s?.activeSeconds))
                }
                GridRow {
                    field("Download limit", limit(item.speedLimitKBps))
                    field("Upload limit", limit(item.torrent?.options.uploadLimitKBps))
                    field("Upload slots", slots(item.torrent?.options.maxUploads))
                }
            }
            .font(.caption)
            if let hash = s?.infoHashV1, !hash.isEmpty {
                Text("Hash: \(hash)").font(.caption.monospaced()).foregroundStyle(.secondary).textSelection(.enabled).lineLimit(1)
            }
        }
    }

    private var trackers: some View {
        Table(snapshot?.trackerStatus ?? []) {
            TableColumn("Tracker") { t in Text(t.url).lineLimit(1).truncationMode(.middle).textSelection(.enabled) }
            TableColumn("Status") { t in Text(t.statusText).foregroundStyle(t.working ? Color.primary : Color.secondary) }.width(170)
            TableColumn("Seeds") { t in Text(t.seeds >= 0 ? "\(t.seeds)" : "–") }.width(50)
            TableColumn("Peers") { t in Text(t.peers >= 0 ? "\(t.peers)" : "–") }.width(50)
        }
        .overlay(alignment: .bottomTrailing) {
            Button("Update Tracker") { manager.reannounce(item.id) }
                .controlSize(.small).padding(6).disabled(!item.status.isActive)
                .help("Ask the trackers and DHT for more peers now")
        }
    }

    private var peerList: some View {
        Group {
            if !item.status.isActive {
                Text("Start the torrent to see its peers.").foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if peers.isEmpty {
                Text("Looking for peers…").foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                Table(peers) {
                    TableColumn("IP") { p in Text(p.address).textSelection(.enabled) }
                    TableColumn("Client") { p in Text(p.client.isEmpty ? "Unknown" : p.client).lineLimit(1) }
                    TableColumn("Flags") { p in Text(p.flags).font(.caption.monospaced()) }.width(44)
                    TableColumn("%") { p in Text("\(Int(p.progress * 100))") }.width(36)
                    TableColumn("Down") { p in Text(speed(Double(p.downloadSpeed))) }.width(80)
                    TableColumn("Up") { p in Text(speed(Double(p.uploadSpeed))) }.width(80)
                    TableColumn("Downloaded") { p in Text(bytes(p.downloaded)) }.width(80)
                    TableColumn("Uploaded") { p in Text(bytes(p.uploaded)) }.width(80)
                }
                .help("Flags: E encrypted, I incoming, P µTP, S seed")
            }
        }
    }

    private var files: some View {
        let list = (snapshot?.files ?? []).filter { !$0.pad }
        let priorities = item.torrent?.priorities ?? []
        return Table(list) {
            TableColumn("File") { f in Text(f.path).lineLimit(1).truncationMode(.middle) }
            TableColumn("Size") { f in Text(bytes(f.size)) }.width(80)
            TableColumn("Done") { f in ProgressView(value: f.size > 0 ? min(1, Double(f.received) / Double(f.size)) : 1) }.width(90)
            TableColumn("Priority") { f in
                let value = priorities.indices.contains(f.index) ? priorities[f.index] : 4
                Text(value == 0 ? "Skip" : value == 1 ? "Low" : value == 7 ? "High" : "Normal")
            }.width(60)
        }
    }

    private func field(_ title: String, _ value: String) -> some View {
        HStack(spacing: 4) { Text(title + ":").foregroundStyle(.secondary); Text(value).monospacedDigit() }
    }
    private func rate(_ value: Int64?) -> String { item.status.isActive ? (speed(Double(value ?? 0)).isEmpty ? "0 B/s" : speed(Double(value ?? 0))) : "–" }
    private func limit(_ kbps: Int?) -> String { (kbps ?? 0) > 0 ? "\(kbps!) KB/s" : "No limit" }
    private func slots(_ value: Int?) -> String { (value ?? 0) > 0 ? "\(value!)" : "No limit" }
    private func connections(_ s: TorrentSnapshot?) -> String {
        guard let s else { return "–" }
        let max = item.torrent?.options.maxConnections ?? 0
        return "\(s.connections ?? s.peers)" + (max > 0 ? " of \(max)" : "")
    }
    private func pieces(_ s: TorrentSnapshot?) -> String {
        guard let s, let count = s.pieceCount, count > 0 else { return "–" }
        return "\(s.piecesDone ?? 0) of \(count) × \(bytes(s.pieceSize ?? 0))"
    }
    private func eta(_ s: TorrentSnapshot?) -> String {
        guard item.status == .downloading, let s, s.eta > 0 else { return item.status == .seeding ? "Done" : "–" }
        return duration(s.eta)
    }
    private func duration(_ seconds: Int64?) -> String {
        guard let seconds, seconds > 0 else { return "–" }
        let f = DateComponentsFormatter(); f.allowedUnits = [.day, .hour, .minute, .second]; f.unitsStyle = .abbreviated; f.maximumUnitCount = 2
        return f.string(from: TimeInterval(seconds)) ?? "–"
    }
}

/// Which parts of the torrent have arrived, from left to right.
struct PieceBar: View {
    let map: [Int]
    let done: Bool

    var body: some View {
        Canvas { ctx, size in
            ctx.fill(Path(roundedRect: CGRect(origin: .zero, size: size), cornerRadius: 3), with: .color(.secondary.opacity(0.2)))
            if done && map.isEmpty {
                ctx.fill(Path(roundedRect: CGRect(origin: .zero, size: size), cornerRadius: 3), with: .color(brand))
                return
            }
            guard !map.isEmpty else { return }
            let w = size.width / CGFloat(map.count)
            for (i, value) in map.enumerated() where value > 0 {
                ctx.fill(Path(CGRect(x: CGFloat(i) * w, y: 0, width: w + 0.5, height: size.height)),
                         with: .color(brand.opacity(0.35 + 0.65 * Double(value) / 100)))
            }
        }
    }
}

/// Settings of one torrent: speed limits, connections, seeding, trackers and file priorities. A running torrent
/// takes the changes right away.
struct TorrentSettingsSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    let id: UUID
    @State private var options = TorrentOptions()
    @State private var downloadLimit = 0
    @State private var priorities: [Int] = []
    @State private var trackers = ""
    @State private var message = ""

    private static let levels = [0, 1, 4, 7]
    private static func levelName(_ value: Int) -> String { ["Skip", "Low", "", "", "Normal", "", "", "High"][value] }

    var body: some View {
        let item = manager.items.first { $0.id == id }
        VStack(alignment: .leading, spacing: 12) {
            Text("Torrent Settings").font(.title2.bold())
            if let item {
                Text(item.fileName).font(.headline).lineLimit(1).truncationMode(.middle)
                Form {
                    Section("Bandwidth") {
                        LabeledContent("Download limit") { limitField($downloadLimit) }
                        LabeledContent("Upload limit") { limitField($options.uploadLimitKBps) }
                        TorrentConnectionFields(options: $options)
                    }
                    Section("Seeding") {
                        Toggle("Keep sharing (seeding) after the download", isOn: $options.seed)
                        if options.seed {
                            LabeledContent("Stop at ratio") {
                                TextField("", value: $options.ratioLimit, format: .number.precision(.fractionLength(0...2))).frame(width: 70)
                                    .help("0 means no ratio limit")
                            }
                            LabeledContent("Stop after (minutes)") {
                                TextField("", value: minutesBinding, format: .number).frame(width: 70).help("0 means no time limit")
                            }
                        }
                    }
                    Section("Trackers (one per line)") {
                        TextEditor(text: $trackers).font(.caption.monospaced()).frame(height: 70)
                    }
                    if let files = item.torrent?.metadata?.files {
                        Section("Files") {
                            ForEach(files.filter { !$0.pad }) { file in
                                HStack {
                                    Text(file.path).lineLimit(1).truncationMode(.middle)
                                    Spacer()
                                    Text(bytes(file.size)).foregroundStyle(.secondary).monospacedDigit()
                                    Picker("", selection: priorityBinding(file.index)) {
                                        ForEach(Self.levels, id: \.self) { Text(Self.levelName($0)).tag($0) }
                                    }
                                    .labelsHidden().frame(width: 100)
                                }
                            }
                        }
                    }
                }
                .formStyle(.grouped)
                .frame(minHeight: 360)
            }
            HStack {
                Text(message).font(.caption).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true)
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Save") { save() }.keyboardShortcut(.defaultAction).disabled(item?.torrent == nil)
            }
        }
        .padding(20)
        .frame(width: 600)
        .onAppear {
            guard let item, let task = item.torrent else { return }
            options = task.options
            downloadLimit = item.speedLimitKBps ?? 0
            priorities = task.priorities ?? task.metadata?.files?.map { $0.pad ? 0 : 4 } ?? []
            trackers = (task.metadata?.trackers ?? []).joined(separator: "\n")
        }
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

    private func save() {
        guard let item = manager.items.first(where: { $0.id == id }), var task = item.torrent else { return }
        do {
            guard (0...2_000_000).contains(downloadLimit) else { throw TorrentFailure("Enter a download limit between 0 and 2,000,000 KB/s.") }
            let list = trackers.split(whereSeparator: \.isNewline).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
            for url in list where !(url.hasPrefix("http://") || url.hasPrefix("https://") || url.hasPrefix("udp://")) {
                throw TorrentFailure("Trackers must start with http://, https:// or udp://.")
            }
            task.options = options
            task.priorities = priorities
            let trackersChanged = list != (task.metadata?.trackers ?? [])
            if trackersChanged { task.metadata?.trackers = list }
            try manager.updateTorrent(id, task: task, limit: downloadLimit, trackers: trackersChanged ? list : nil)
            dismiss()
        } catch {
            message = error.localizedDescription
        }
    }
}

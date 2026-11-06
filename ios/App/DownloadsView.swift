import NexonicKit
import QuickLook
import SwiftUI
import UIKit

enum ListView: String, CaseIterable, Identifiable {
    case all = "All", unfinished = "Unfinished", finished = "Finished"
    var id: String { rawValue }
    func includes(_ j: DownloadJob) -> Bool {
        switch self {
        case .all: return true
        case .unfinished: return j.status != .complete
        case .finished: return j.status == .complete
        }
    }
}

extension FileCategory {
    var label: String { self == .other ? "General" : rawValue }
    var color: Color {
        switch self {
        case .video: return .purple
        case .music: return .pink
        case .compressed: return .orange
        case .documents: return .blue
        case .programs: return .green
        case .images: return .teal
        default: return .gray
        }
    }
}

/// The download list: All / Unfinished / Finished, by type, search. On iPad the details open beside the list.
struct DownloadsView: View {
    @Environment(AppModel.self) private var model
    @State private var view: ListView = .all
    @State private var category: FileCategory?
    @State private var query = ""
    @State private var showLibrary = false

    private var shown: [DownloadJob] {
        model.jobs.filter { view.includes($0) && (category == nil || $0.category == category)
            && (query.isEmpty || $0.fileName.localizedCaseInsensitiveContains(query) || $0.url.absoluteString.localizedCaseInsensitiveContains(query)) }
    }

    @Environment(\.horizontalSizeClass) private var sizeClass

    var body: some View {
        @Bindable var model = model
        Group {
            if sizeClass == .compact {
                // iPhone: a plain stack, so a download opened from code (a notification, the screenshots) shows its details.
                NavigationStack(path: Binding(get: { model.selection.map { [$0] } ?? [] }, set: { model.selection = $0.last })) {
                    list(selection: nil)
                        .navigationDestination(for: UUID.self) { DetailView(id: $0) }
                }
            } else {
                NavigationSplitView {
                    list(selection: $model.selection)
                } detail: {
                    if let selection = model.selection { DetailView(id: selection) }
                    else { ContentUnavailableView("Select a download", systemImage: "arrow.down.circle") }
                }
            }
        }
        .sheet(isPresented: Binding(get: { model.showPaste }, set: { model.showPaste = $0 })) { PasteView() }
        .sheet(isPresented: $showLibrary) { MediaLibraryView() }
    }

    private func list(selection: Binding<UUID?>?) -> some View {
        List(selection: selection) {
            Section {
                Picker("Show", selection: $view) {
                    ForEach(ListView.allCases) { v in Text("\(v.rawValue) \(model.jobs.filter(v.includes).count)").tag(v) }
                }
                .pickerStyle(.segmented)
                .listRowInsets(EdgeInsets(top: 8, leading: 0, bottom: 8, trailing: 0))
                .listRowBackground(Color.clear)
            }
            TorrentRows()
            if shown.isEmpty {
                ContentUnavailableView {
                    Label(model.jobs.isEmpty ? "No downloads yet" : "Nothing here", systemImage: "arrow.down.circle")
                } description: {
                    Text(model.jobs.isEmpty ? "In Safari or Chrome, tap Share → Download with Nexonic. Or tap + to paste a link." : "No downloads match.")
                } actions: {
                    if model.jobs.isEmpty {
                        Button("Set Up Download Shortcut") { model.showSetup = true }
                    }
                }
                .listRowBackground(Color.clear)
            } else {
                ForEach(shown) { job in
                    NavigationLink(value: job.id) { DownloadRow(job: job, speed: model.speeds[job.id] ?? 0) }
                        .swipeActions(edge: .trailing) {
                            Button(role: .destructive) { model.remove(job, deleteFile: false) } label: { Label("Remove", systemImage: "trash") }
                        }
                        .swipeActions(edge: .leading) {
                            if job.isActive { Button { model.pause(job) } label: { Label("Pause", systemImage: "pause.fill") }.tint(.orange) }
                            else if job.status != .complete { Button { model.resume(job) } label: { Label("Resume", systemImage: "play.fill") }.tint(.green) }
                        }
                }
            }
        }
        .navigationTitle("Downloads")
        .searchable(text: $query, prompt: "Search downloads")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { model.showPaste = true } label: { Label("Add", systemImage: "plus") }
                    .keyboardShortcut("n", modifiers: .command)  // iPad with a keyboard
            }
            ToolbarItem(placement: .topBarLeading) {
                Menu {
                    Picker("Type", selection: $category) {
                        Text("All types").tag(FileCategory?.none)
                        ForEach(FileCategory.allCases, id: \.self) { c in Label(c.label, systemImage: c.symbol).tag(Optional(c)) }
                    }
                    Divider()
                    Button("Add Torrent…", systemImage: "arrow.down.circle.dotted") { model.torrentRequest = TorrentRequest(source: "") }
                    Button("Media Library", systemImage: "play.rectangle.on.rectangle") { showLibrary = true }
                    if model.activeCount > 0 { Button("Pause All", systemImage: "pause") { model.pauseAll() } }
                    Button("Start All", systemImage: "play") { model.startLater() }
                    Button("Clear Finished", systemImage: "checkmark.circle") { model.clearFinished() }
                } label: { Label("More", systemImage: category == nil ? "line.3.horizontal.decrease.circle" : "line.3.horizontal.decrease.circle.fill") }
            }
        }
    }
}

struct DownloadRow: View {
    let job: DownloadJob
    let speed: Int64

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: job.category.symbol)
                .font(.title3).foregroundStyle(job.category.color)
                .frame(width: 42, height: 42)
                .background(job.category.color.opacity(0.14), in: RoundedRectangle(cornerRadius: 11))
            VStack(alignment: .leading, spacing: 4) {
                Text(job.fileName).font(.body.weight(.medium)).lineLimit(1)
                if job.status == .downloading || job.status == .paused {
                    if job.size != nil { ProgressView(value: job.progress) } else { ProgressView().progressViewStyle(.linear) }
                }
                Text(StatusText.of(job, speed: speed)).font(.caption)
                    .foregroundStyle(job.status == .failed ? .red : .secondary).lineLimit(1)
            }
        }
        .padding(.vertical, 2)
    }
}

enum StatusText {
    static func of(_ j: DownloadJob, speed: Int64) -> String {
        switch j.status {
        case .downloading:
            var parts = [j.size.map { "\(Format.size(j.written)) of \(Format.size($0))" } ?? Format.size(j.written)]
            if speed > 0 { parts.append(Format.speed(speed)) }
            if speed > 0, let size = j.size { parts.append(Format.eta((size - j.written) / max(speed, 1)) + " left") }
            return parts.joined(separator: " · ")
        case .queued: return "Waiting"
        case .paused: return "Paused" + (j.size.map { " · \(Format.size(j.written)) of \(Format.size($0))" } ?? "")
        case .later: return "Download later" + (j.size.map { " · \(Format.size($0))" } ?? "")
        case .complete: return Format.size(j.size ?? j.written) + " · " + (j.finishedAt ?? j.addedAt).formatted(date: .abbreviated, time: .omitted)
        case .failed: return j.error ?? "Failed"
        }
    }
}

/// Details of one download, with all actions.
struct DetailView: View {
    @Environment(AppModel.self) private var model
    let id: UUID
    @State private var preview: URL?
    @State private var confirmDelete = false
    @State private var showLogin = false
    @State private var user = ""
    @State private var password = ""

    var body: some View {
        if let job = model.jobs.first(where: { $0.id == id }) {
            List {
                Section {
                    HStack(spacing: 14) {
                        Image(systemName: job.category.symbol).font(.title2).foregroundStyle(job.category.color)
                            .frame(width: 52, height: 52).background(job.category.color.opacity(0.14), in: RoundedRectangle(cornerRadius: 13))
                        VStack(alignment: .leading) {
                            Text(job.fileName).font(.headline).lineLimit(2)
                            Text(job.url.absoluteString).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                        }
                    }
                    if job.status != .complete, job.size != nil { ProgressView(value: job.progress) }
                }
                Section {
                    LabeledContent("Status", value: StatusText.of(job, speed: model.speeds[job.id] ?? 0))
                    LabeledContent("Size", value: Format.size(job.size))
                    if let q = job.quality { LabeledContent("Quality", value: q) }
                    LabeledContent("Type", value: job.category.label)
                    if let path = job.savedPath { LabeledContent("Saved to", value: "Files › Nexonic › " + path) }
                    LabeledContent("Added", value: job.addedAt.formatted(date: .abbreviated, time: .shortened))
                }
                Section {
                    switch job.status {
                    case .downloading, .queued: Button("Pause", systemImage: "pause") { model.pause(job) }
                    case .paused, .later: Button("Resume", systemImage: "play") { model.resume(job) }
                    case .failed: Button("Try Again", systemImage: "arrow.clockwise") { model.resume(job) }
                    case .complete:
                        if let file = Library.fileURL(job) {
                            Button("Open", systemImage: "eye") { preview = file }
                            ShareLink(item: file) { Label("Share", systemImage: "square.and.arrow.up") }
                            Button("Show in Files", systemImage: "folder") { FilesApp.show(file) }
                        }
                    }
                    if job.status != .complete { Button("Login…", systemImage: "key") { showLogin = true } }
                    Button("Copy Link", systemImage: "link") { UIPasteboard.general.url = job.url; model.toast = "Link copied" }
                    Button("Remove", systemImage: "trash", role: .destructive) { confirmDelete = true }
                }
            }
            .navigationTitle("Details")
            .navigationBarTitleDisplayMode(.inline)
            .quickLookPreview($preview)
            .confirmationDialog("Remove \(job.fileName)?", isPresented: $confirmDelete, titleVisibility: .visible) {
                Button("Remove from List") { model.remove(job, deleteFile: false) }
                if job.savedPath != nil { Button("Remove and Delete File", role: .destructive) { model.remove(job, deleteFile: true) } }
            }
            .alert("Login for this download", isPresented: $showLogin) {
                TextField("User name", text: $user).textInputAutocapitalization(.never)
                SecureField("Password", text: $password)
                Button("Save") { model.setLogin(job, user: user, password: password); model.resume(job) }
                Button("Cancel", role: .cancel) {}
            } message: { Text("Sent only to this site, and kept in the Keychain on this device.") }
        } else {
            ContentUnavailableView("Download removed", systemImage: "trash")
        }
    }
}

enum FilesApp {
    /// Opens the Files app at the file's folder.
    static func show(_ file: URL) {
        let folder = file.deletingLastPathComponent().path
        if let url = URL(string: "shareddocuments://" + (folder.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? folder)) {
            UIApplication.shared.open(url)
        }
    }
}

/// Paste one or more links, or a batch pattern.
struct PasteView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var text = ""
    @State private var batch = false

    private var links: [URL] {
        let raw = Links.split(text)
        let expanded = batch ? raw.flatMap { (try? BatchPattern.expand($0)) ?? [$0] } : raw
        return expanded.compactMap(Net.webURL)
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("https://…", text: $text, axis: .vertical).lineLimit(3...8)
                        .keyboardType(.URL).textInputAutocapitalization(.never).autocorrectionDisabled()
                    PasteButton(payloadType: String.self) { strings in text = strings.joined(separator: "\n") }
                    Toggle("Batch pattern (file[01-50].jpg)", isOn: $batch)
                } footer: { Text(links.isEmpty ? "Links start with http:// or https://" : "\(links.count) link\(links.count == 1 ? "" : "s")") }
            }
            .navigationTitle("Add Download")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Next") { let l = links; dismiss(); DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { model.addInput = DownloadInput(urls: l) } }.disabled(links.isEmpty)
                }
            }
        }
    }
}

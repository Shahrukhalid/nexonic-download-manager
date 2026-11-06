import AppKit
import NexonicCore
import QuickLookThumbnailing
import SwiftUI

/// Playlists and channels, and the Media Library (Milestone 8).
enum MediaWindows {
    @MainActor private static var playlist: NSWindow?
    @MainActor private static var library: NSWindow?

    /// Lists a playlist's or channel's videos; the chosen ones open in the Add window.
    @MainActor static func showPlaylist(_ link: String = "") {
        if let w = playlist { w.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true); return }
        let start = link.isEmpty ? (NSPasteboard.general.string(forType: .string).flatMap { Playlists.isPlaylist($0) ? $0 : nil } ?? "") : link
        let w = NSWindow(contentViewController: NSHostingController(rootView: PlaylistView(link: start) { links in
            playlist?.close()
            DownloadManager.showMainWindow?()
            DownloadManager.shared.addRequest = AddRequest(links: links)
        }))
        w.title = "Add Playlist or Channel"
        w.styleMask = [.titled, .closable, .resizable]
        w.isReleasedWhenClosed = false
        w.setContentSize(NSSize(width: 620, height: 520))
        w.center()
        playlist = w
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { _ in
            Task { @MainActor in playlist = nil }
        }
        w.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    /// Finished videos and music with preview pictures.
    @MainActor static func showLibrary() {
        if let w = library { w.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true); return }
        let w = NSWindow(contentViewController: NSHostingController(rootView: LibraryView(manager: DownloadManager.shared)))
        w.title = "Media Library"
        w.styleMask = [.titled, .closable, .resizable, .miniaturizable]
        w.isReleasedWhenClosed = false
        w.setContentSize(NSSize(width: 900, height: 620))
        w.center()
        library = w
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { _ in
            Task { @MainActor in library = nil }
        }
        w.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    /// Runs the video engine to list a playlist's videos (up to two minutes).
    static func read(_ link: String, proxy: ProxySettings) throws -> Playlists.Result {
        SiteVideoTools.prepare()
        guard FileManager.default.isExecutableFile(atPath: SiteVideoTools.ytdlp.path) else { throw MediaError("The video engine is missing. Reinstall the app") }
        let prepared = try SiteVideo.prepare(Playlists.arguments(link, quickJS: SiteVideoTools.quickJS), proxy: proxy)
        try FileManager.default.createDirectory(at: AppPaths.parts, withIntermediateDirectories: true)
        let errorFile = AppPaths.parts.appendingPathComponent("playlist-\(UUID().uuidString).log")
        FileManager.default.createFile(atPath: errorFile.path, contents: nil)
        defer {
            if let f = prepared.configFile { try? FileManager.default.removeItem(at: f) }
            try? FileManager.default.removeItem(at: errorFile)
        }
        let p = Process()
        p.executableURL = SiteVideoTools.ytdlp
        p.arguments = prepared.args
        let out = Pipe()
        p.standardOutput = out
        p.standardError = try FileHandle(forWritingTo: errorFile)
        try p.run()
        let timer = DispatchWorkItem { if p.isRunning { p.terminate() } }
        DispatchQueue.global().asyncAfter(deadline: .now() + 120, execute: timer)
        let data = out.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        timer.cancel()
        guard p.terminationStatus == 0, let r = Playlists.parse(data) else {
            let errors = (try? String(contentsOf: errorFile, encoding: .utf8)) ?? ""
            throw MediaError(SiteVideo.error(errors))
        }
        if r.entries.isEmpty { throw MediaError("No videos were found at this link") }
        return r
    }
}

struct MediaError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
    init(_ message: String) { self.message = message }
}

private struct PlaylistView: View {
    @State var link: String
    let add: (String) -> Void
    @State private var status = ""
    @State private var busy = false
    @State private var title = ""
    @State private var entries: [Playlists.Entry] = []
    @State private var chosen: Set<String> = []

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Paste a playlist or channel link (YouTube, Vimeo, SoundCloud, Bandcamp and other video sites). Nexonic lists its videos (up to \(Playlists.maxEntries)); choose the ones you want and they open in the Add window.")
                .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            HStack {
                TextField("Playlist or channel link", text: $link).textFieldStyle(.roundedBorder).onSubmit(read)
                Button("Read Videos", action: read).disabled(busy)
            }
            if busy { ProgressView().controlSize(.small) }
            if !status.isEmpty { Text(status).font(.callout).foregroundStyle(.secondary) }
            if !entries.isEmpty {
                Toggle("All videos", isOn: Binding(get: { chosen.count == entries.count },
                                                   set: { on in chosen = on ? Set(entries.map(\.url)) : [] }))
                List(entries, id: \.url) { e in
                    Toggle(isOn: Binding(get: { chosen.contains(e.url) },
                                         set: { on in if on { chosen.insert(e.url) } else { chosen.remove(e.url) } })) {
                        HStack {
                            Text(e.title).lineLimit(1)
                            Spacer()
                            if let d = e.duration, d > 0 { Text(duration(d)).foregroundStyle(.secondary).monospacedDigit() }
                        }
                    }
                    .help(e.url)
                }
            } else {
                Spacer()
            }
            HStack {
                Spacer()
                Button(chosen.count == 1 ? "Add 1 Video" : "Add \(chosen.count) Videos") {
                    add(entries.filter { chosen.contains($0.url) }.map(\.url).joined(separator: "\n"))
                }
                .keyboardShortcut(.defaultAction)
                .disabled(chosen.isEmpty)
            }
        }
        .padding(16)
        .frame(minWidth: 520, minHeight: 420)
        .onAppear { if !link.isEmpty { read() } }
    }

    private func duration(_ s: Double) -> String {
        let t = Int(s)
        return t >= 3600 ? String(format: "%d:%02d:%02d", t / 3600, t / 60 % 60, t % 60) : String(format: "%d:%02d", t / 60, t % 60)
    }

    private func read() {
        let url = link.trimmingCharacters(in: .whitespacesAndNewlines)
        guard DownloadJob(url: url).validURL != nil else { status = "Paste a web link (http or https)."; return }
        busy = true
        status = "Reading the list of videos… (large channels can take a minute)"
        let proxy = DownloadManager.shared.settings.proxy
        DispatchQueue.global().async {
            let result = Result { try MediaWindows.read(url, proxy: proxy) }
            Task { @MainActor in
                busy = false
                switch result {
                case .success(let r):
                    entries = r.entries
                    chosen = Set(r.entries.map(\.url))
                    let count = r.entries.count == 1 ? "1 video" : "\(r.entries.count) videos"
                    status = (r.title.isEmpty ? "" : r.title + ": ") + count + (r.entries.count >= Playlists.maxEntries ? " (the first \(Playlists.maxEntries))" : "")
                case .failure(let e):
                    status = (e as? MediaError)?.message ?? e.localizedDescription
                }
            }
        }
    }
}

private struct LibraryView: View {
    @ObservedObject var manager: DownloadManager
    @State private var search = ""
    @State private var kind = 0
    @State private var selection: String?

    private struct Item: Identifiable {
        let id: String  // the file's path
        let record: DownloadRecord
        let isVideo: Bool
    }

    private var items: [Item] {
        manager.items
            .filter { $0.status == .finished }
            .compactMap { r -> Item? in
                guard let p = r.savedPath, FileManager.default.fileExists(atPath: p) else { return nil }
                let c = FileCategory(fileName: (p as NSString).lastPathComponent)
                guard c == .video || c == .music else { return nil }
                if kind == 1 && c != .video || kind == 2 && c != .music { return nil }
                if !search.isEmpty && !r.fileName.localizedCaseInsensitiveContains(search) { return nil }
                return Item(id: p, record: r, isVideo: c == .video)
            }
            .sorted { ($0.record.finished ?? $0.record.added) > ($1.record.finished ?? $1.record.added) }
    }

    var body: some View {
        let list = items
        VStack(spacing: 0) {
            HStack {
                TextField("Search by name", text: $search).textFieldStyle(.roundedBorder).frame(width: 240)
                Picker("", selection: $kind) {
                    Text("Videos and music").tag(0)
                    Text("Videos").tag(1)
                    Text("Music").tag(2)
                }
                .labelsHidden()
                .frame(width: 170)
                Text(list.count == 1 ? "1 file" : "\(list.count) files").foregroundStyle(.secondary)
                Spacer()
            }
            .padding(12)
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 200, maximum: 220), spacing: 12)], spacing: 14) {
                    ForEach(list) { item in
                        VStack(alignment: .leading, spacing: 4) {
                            Thumbnail(path: item.id, isVideo: item.isVideo)
                                .frame(height: 112)
                                .clipShape(RoundedRectangle(cornerRadius: 6))
                            Text(item.record.fileName).fontWeight(.semibold).lineLimit(1)
                            Text("\(item.record.size.map { ByteCountFormatter.string(fromByteCount: $0, countStyle: .file) } ?? "")   \((item.record.finished ?? item.record.added).formatted(date: .abbreviated, time: .omitted))")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                        .padding(6)
                        .background(RoundedRectangle(cornerRadius: 8).fill(selection == item.id ? Color.accentColor.opacity(0.15) : .clear))
                        .contentShape(Rectangle())
                        .onTapGesture(count: 2) { NSWorkspace.shared.open(URL(fileURLWithPath: item.id)) }
                        .onTapGesture { selection = item.id }
                        .help(item.id)
                    }
                }
                .padding(.horizontal, 12)
            }
            Divider()
            HStack {
                Spacer()
                Button("Play") { if let s = selection { NSWorkspace.shared.open(URL(fileURLWithPath: s)) } }.disabled(selection == nil)
                Button("Show in Finder") {
                    if let s = selection { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: s)]) }
                }
                .disabled(selection == nil)
            }
            .padding(12)
        }
        .frame(minWidth: 520, minHeight: 360)
    }
}

/// A file's preview picture from Quick Look (videos), or a symbol (music and files without one).
private struct Thumbnail: View {
    let path: String
    let isVideo: Bool
    @State private var image: NSImage?

    var body: some View {
        ZStack {
            Color.secondary.opacity(0.12)
            if let image {
                Image(nsImage: image).resizable().scaledToFill()
            } else {
                Image(systemName: isVideo ? "film" : "music.note").font(.system(size: 34)).foregroundStyle(.secondary)
            }
        }
        .task(id: path) {
            guard isVideo else { return }
            let request = QLThumbnailGenerator.Request(fileAt: URL(fileURLWithPath: path), size: CGSize(width: 400, height: 224),
                                                       scale: 2, representationTypes: .thumbnail)
            if let rep = try? await QLThumbnailGenerator.shared.generateBestRepresentation(for: request) { image = rep.nsImage }
        }
    }
}

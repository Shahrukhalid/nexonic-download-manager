import AppKit
import Combine
import NexonicCore
import SwiftUI
import UniformTypeIdentifiers

/// The system icon for a file name.
@MainActor func fileIcon(_ name: String) -> NSImage {
    let ext = (name as NSString).pathExtension
    return NSWorkspace.shared.icon(for: UTType(filenameExtension: ext) ?? .data)
}

// MARK: Download window (shown when a link is clicked in the browser)

/// The compact window that asks where and how to save a file the browser handed over.
@MainActor
enum DownloadInfoWindow {
    private static var panels: [String: NSPanel] = [:]  // by link, so one link never gets two windows

    static func isOpen(for url: String) -> Bool { panels[url] != nil }

    static func show(_ job: DownloadJob, manager: DownloadManager) {
        if let p = panels[job.url] {
            p.makeKeyAndOrderFront(nil)
            return
        }
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 540, height: 300), styleMask: [.titled, .closable],
                            backing: .buffered, defer: false)
        panel.title = "Download File Info"
        panel.isReleasedWhenClosed = false
        panel.level = .floating
        panel.hidesOnDeactivate = false
        let key = job.url
        let close = { [weak panel] in
            panel?.close()
            panels[key] = nil
        }
        panel.contentView = NSHostingView(rootView: DownloadInfoView(job: job, close: close).environmentObject(manager))
        panel.center()
        if let last = panels.values.first { panel.setFrameTopLeftPoint(NSPoint(x: last.frame.minX + 28, y: last.frame.maxY - 28)) }
        panels[key] = panel
        _ = NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: panel, queue: .main) { _ in
            Task { @MainActor in panels[key] = nil }
        }
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
    }
}

struct DownloadInfoView: View {
    @EnvironmentObject var manager: DownloadManager
    let job: DownloadJob
    let close: () -> Void
    @State private var name = ""
    @State private var folder = URL(fileURLWithPath: NSHomeDirectory())
    @State private var queueID = DownloadQueue.mainID
    @State private var size: Int64?
    @State private var checking = false
    @State private var quality = 0  // 0 = best, -1 = sound only, otherwise the highest picture height
    @State private var choices: SiteVideo.Choices?
    @State private var bestSize: Int64?
    @State private var infoPath: String?

    /// Qualities below the best one (the best is its own row), plus one chosen in the browser before the lookup finished.
    private var lowerQualities: [Int] {
        var h = (choices?.video.dropFirst().map(\.height)) ?? []
        if quality > 0, !h.contains(quality) { h.append(quality) }
        return h.sorted(by: >)
    }

    private var url: URL? { job.validURL }
    private var isSiteVideo: Bool { job.site == true || (url.map(SiteVideo.isSupported) ?? false) }
    private var category: FileCategory { FileCategory(fileName: name) }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .top, spacing: 14) {
                Image(nsImage: fileIcon(name))
                    .resizable().frame(width: 48, height: 48)
                VStack(alignment: .leading, spacing: 4) {
                    TextField("File name", text: $name).textFieldStyle(.roundedBorder).font(.headline)
                    Text(url?.absoluteString ?? job.url).font(.caption).foregroundStyle(.secondary)
                        .lineLimit(1).truncationMode(.middle).textSelection(.enabled)
                }
            }
            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 8) {
                GridRow {
                    Text("Size").foregroundStyle(.secondary)
                    if checking { HStack(spacing: 6) { ProgressView().controlSize(.small); Text(isSiteVideo ? "Finding the qualities on offer…" : "Checking…") } }
                    else { Text(size.map(bytes) ?? (isSiteVideo ? "Size known when the download starts" : "Unknown")) }
                }
                GridRow {
                    Text("Category").foregroundStyle(.secondary)
                    Label(category.rawValue, systemImage: category.symbol)
                }
                GridRow {
                    Text("Save to").foregroundStyle(.secondary)
                    HStack {
                        Text(folder.path).lineLimit(1).truncationMode(.middle)
                        Spacer()
                        Button("Choose…") { if let u = chooseFolder(folder) { folder = u } }
                    }
                }
                GridRow {
                    Text("Queue").foregroundStyle(.secondary)
                    Picker("", selection: $queueID) {
                        ForEach(manager.queues) { q in Text(q.name).tag(q.id) }
                    }
                    .labelsHidden().frame(maxWidth: 220, alignment: .leading)
                }
                if isSiteVideo {
                    GridRow {
                        Text("Quality").foregroundStyle(.secondary)
                        Picker("", selection: $quality) {
                            Text(choices?.video.first.map { "Best (\(SiteVideo.qualityName($0.height)))" } ?? "Best available").tag(0)
                            ForEach(lowerQualities, id: \.self) { h in Text(SiteVideo.qualityName(h)).tag(h) }
                            Text("Sound only").tag(-1)
                        }
                        .labelsHidden().frame(maxWidth: 220, alignment: .leading)
                        .onChange(of: quality) { _ in showSize() }
                    }
                }
            }
            .font(.callout)
            Toggle("Don't show this window; start browser downloads right away",
                   isOn: Binding(get: { !manager.settings.showDownloadDialog }, set: { manager.settings.showDownloadDialog = !$0 }))
                .font(.caption)
            HStack {
                Spacer()
                Button("Cancel") { close() }.keyboardShortcut(.cancelAction)
                Button("Download Later") { add(start: false) }
                Button("Start Download") { add(start: true) }.keyboardShortcut(.defaultAction).disabled(url == nil)
            }
        }
        .padding(18)
        .frame(width: 540)
        .tint(brand)
        .onAppear(perform: setUp)
    }

    private func setUp() {
        folder = manager.settings.folder
        quality = job.audioOnly == true ? -1 : (job.maxHeight ?? 0)
        if let n = job.fileName, !n.isEmpty { name = FileNames.sanitize(n) }
        else if let url { name = isSiteVideo ? "Video from \(url.host ?? "site")" : FileNames.fromURL(url) }
        size = job.size
        if isSiteVideo, let url { lookUpVideo(url) }
        else if size == nil, job.stream != true, let url { probe(url) }
    }

    /// Looks the video up while the window is open (title, size, best quality), like a file's size is checked.
    /// The details are saved and reused, so the download starts at once instead of looking the video up again.
    private func lookUpVideo(_ url: URL) {
        checking = true
        let path = AppPaths.parts.appendingPathComponent("info-\(UUID().uuidString).json").path
        let args = SiteVideo.infoArguments(url: url, quickJS: SiteVideoTools.quickJS, audioOnly: false, referrer: job.referrer,
                                           cookies: job.cookies, userAgent: job.userAgent, username: nil, password: nil)
        let fromJob = job.fileName != nil
        let proxy = manager.settings.proxy
        DispatchQueue.global(qos: .userInitiated).async {
            SiteVideoTools.prepare()
            try? FileManager.default.createDirectory(at: AppPaths.parts, withIntermediateDirectories: true)
            let prepared = try? SiteVideo.prepare(args, proxy: proxy)  // nil: no lookup (the download itself reports it)
            defer { if let f = prepared?.configFile { try? FileManager.default.removeItem(at: f) } }
            let p = Process()
            p.executableURL = SiteVideoTools.ytdlp
            p.arguments = prepared?.args ?? []
            let out = Pipe()
            p.standardOutput = out
            p.standardError = FileHandle.nullDevice
            var info: SiteVideo.Info?
            var offered: SiteVideo.Choices?
            if prepared != nil, (try? p.run()) != nil {
                let data = out.fileHandleForReading.readDataToEndOfFile()
                p.waitUntilExit()
                if p.terminationStatus == 0, let i = SiteVideo.info(data), (try? data.write(to: URL(fileURLWithPath: path))) != nil {
                    info = i
                    offered = SiteVideo.choices(data)
                }
            }
            let found = info, foundChoices = offered
            Task { @MainActor in
                checking = false
                guard let found else { return }
                infoPath = path
                bestSize = found.size ?? foundChoices?.video.first?.size  // estimated from the bitrate when not given
                choices = foundChoices
                if quality > 0, quality == foundChoices?.video.first?.height { quality = 0 }  // that is the best on offer
                showSize()
                if !fromJob { name = FileNames.sanitize("\(found.title).\(found.ext)") }
            }
        }
    }

    /// The size of the chosen quality, when the video's details tell it.
    private func showSize() {
        guard choices != nil || bestSize != nil else { return }
        switch quality {
        case 0: size = bestSize
        case -1: size = choices?.soundSize
        default: size = choices?.video.first { $0.height == quality }?.size
        }
    }

    /// Asks the server for the size (and the real file name) without downloading the file.
    private func probe(_ url: URL) {
        checking = true
        var request = URLRequest(url: url, timeoutInterval: 10)
        request.httpMethod = "HEAD"
        if let c = job.cookies, !c.isEmpty { request.setValue(c, forHTTPHeaderField: "Cookie") }
        if let ua = job.userAgent, !ua.isEmpty { request.setValue(ua, forHTTPHeaderField: "User-Agent") }
        if let r = job.referrer, !r.isEmpty { request.setValue(r, forHTTPHeaderField: "Referer") }
        let session = URLSession(configuration: Network.configuration(proxy: manager.settings.proxy))
        let fromJob = job.fileName != nil
        session.dataTask(with: request) { _, response, _ in
            let length = response?.expectedContentLength ?? -1
            let suggested = response?.suggestedFilename
            Task { @MainActor in
                checking = false
                if length > 0 { size = length }
                if !fromJob, let s = suggested, !s.isEmpty, s != "Unknown", (s as NSString).pathExtension.count > 0 {
                    name = FileNames.sanitize(s)
                }
            }
        }.resume()
        session.finishTasksAndInvalidate()
    }

    private func add(start: Bool) {
        var job = job
        job.fileName = name.trimmingCharacters(in: .whitespaces).isEmpty ? nil : name
        job.start = start
        job.audioOnly = quality == -1 ? true : nil
        job.maxHeight = quality > 0 ? quality : nil
        job.infoJSON = infoPath
        var o = DownloadManager.AddOptions()
        o.folder = folder
        o.queueID = queueID
        o.start = start
        o.audioOnly = quality == -1
        let id = manager.add(job, options: o)
        if start, let id, manager.settings.showProgressWindow { ProgressWindow.show(id, manager: manager) }
        close()
    }
}

// MARK: Progress window (one per download)

@MainActor
enum ProgressWindow {
    private static var windows: [UUID: NSWindow] = [:]

    static func show(_ id: UUID, manager: DownloadManager) {
        if let w = windows[id] {
            w.makeKeyAndOrderFront(nil)
            return
        }
        guard let rec = manager.items.first(where: { $0.id == id }) else { return }
        let compact = rec.siteVideo == true || rec.isStream == true  // a short list of pieces instead of connections
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 580, height: compact ? 470 : 600),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.contentMinSize = NSSize(width: 520, height: 250)
        window.isReleasedWhenClosed = false
        window.title = "Download"
        let close = { [weak window] in
            window?.close()
            windows[id] = nil
        }
        let setTitle: (String) -> Void = { [weak window] t in window?.title = t }
        // Hide Details shrinks the window to the summary; Show Details brings back the height it had.
        var tallHeight: CGFloat?
        let fit: (Bool) -> Void = { [weak window] showDetails in
            DispatchQueue.main.async {
                guard let window, let view = window.contentView else { return }
                var frame = window.frame
                if !showDetails { tallHeight = frame.height }
                let fitting = window.frameRect(forContentRect: NSRect(origin: .zero, size: view.fittingSize)).height
                let height = showDetails ? max(tallHeight ?? fitting, fitting) : fitting
                frame.origin.y += frame.height - height
                frame.size.height = height
                window.setFrame(frame, display: true, animate: true)
            }
        }
        window.contentView = NSHostingView(rootView: DownloadProgressView(id: id, close: close, setTitle: setTitle, fit: fit)
            .environmentObject(manager))
        window.center()
        if let last = windows.values.first(where: \.isVisible) {
            window.setFrameTopLeftPoint(NSPoint(x: last.frame.minX + 28, y: last.frame.maxY - 28))
        }
        windows[id] = window
        _ = NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: .main) { _ in
            Task { @MainActor in windows[id] = nil }
        }
        window.orderFront(nil)
    }
}

struct DownloadProgressView: View {
    @EnvironmentObject var manager: DownloadManager
    let id: UUID
    let close: () -> Void
    let setTitle: (String) -> Void
    let fit: (Bool) -> Void
    @State private var history: [Double] = []
    @State private var details = true

    private var item: DownloadRecord? { manager.items.first { $0.id == id } }

    var body: some View {
        Group {
            if let item { content(item) } else { Color.clear.onAppear(perform: close) }
        }
        .frame(minWidth: 520, maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .tint(brand)
        .onReceive(manager.$speeds) { s in
            history.append(s[id] ?? 0)
            if history.count > 120 { history.removeFirst(history.count - 120) }
            if let item {
                setTitle("\(Int(item.progress * 100))% \(item.fileName)")
                if item.status == .finished { close() }
            }
        }
    }

    @ViewBuilder private func content(_ item: DownloadRecord) -> some View {
        let bps = manager.speeds[id] ?? 0
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 12) {
                Image(nsImage: fileIcon(item.fileName))
                    .resizable().frame(width: 40, height: 40)
                VStack(alignment: .leading, spacing: 2) {
                    Text(item.fileName).font(.headline).lineLimit(1).truncationMode(.middle)
                    Text(item.url.absoluteString).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
                        .textSelection(.enabled)
                }
            }
            Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 5) {
                row("Status", statusText(item))
                row("File size", item.size.map(bytes) ?? "Unknown")
                row("Downloaded", "\(bytes(item.received))" + (item.size != nil ? String(format: " (%.1f %%)", item.progress * 100) : ""))
                row("Transfer rate", item.status.isActive ? speed(bps) : "–")
                row("Time left", item.status.isActive ? (manager.eta(item).isEmpty ? "–" : manager.eta(item)) : "–")
                row("Resume capability", item.acceptsRanges || item.siteVideo == true || item.isStream == true ? "Yes" : "No")
            }
            .font(.callout)
            ProgressView(value: item.status == .finished ? 1 : item.progress)
            SpeedGraph(samples: history).frame(height: 44)
            if details {
                if item.isStream != true && item.siteVideo != true && !item.segments.isEmpty {
                    Text("Start positions and progress by connection").font(.caption).foregroundStyle(.secondary)
                    SegmentBar(segments: item.segments, size: item.size).frame(height: 12)
                    connections(item)
                } else if item.isStream == true {
                    Text("Streamed video · pieces: \(item.streamDone ?? 0) of \(item.streamTotal ?? 0), joined into one .mp4")
                        .font(.caption).foregroundStyle(.secondary)
                } else if item.siteVideo == true {
                    VStack(alignment: .leading, spacing: 3) {
                        Text("Video site · " + SiteVideo.choiceText(audioOnly: item.audioOnly, maxHeight: item.maxHeight) + ", picture and sound joined")
                        Text("Save to: \(item.folder.path)").lineLimit(1).truncationMode(.middle)
                        if let path = item.savedPath { Text("Saved as: \((path as NSString).lastPathComponent)").lineLimit(1) }
                    }
                    .font(.caption).foregroundStyle(.secondary)
                    videoParts(item)
                }
            }
            if let e = item.error { Text(e).font(.caption).foregroundStyle(.red).lineLimit(2) }
            HStack {
                Button(details ? "Hide Details" : "Show Details") {
                    details.toggle()
                    fit(details)
                }
                Spacer()
                if item.status.isActive || item.status == .queued {
                    Button("Pause") { manager.pause(id) }
                } else if [.paused, .failed].contains(item.status) {
                    Button("Resume") { manager.resume(id) }.keyboardShortcut(.defaultAction)
                }
                Button("Cancel") { cancel(item) }
                Button("Close") { close() }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(16)
    }

    private func row(_ label: String, _ value: String) -> some View {
        GridRow {
            Text(label).foregroundStyle(.secondary)
            Text(value).monospacedDigit()
        }
    }

    private func statusText(_ item: DownloadRecord) -> String {
        if isPreparingVideo(item) { return "Preparing video… (finding the best quality)" }
        if item.status.isActive, let step = item.videoStep { return step }
        switch item.status {
        case .seeding: return "Seeding verified files"
        case .queued: return "Waiting in queue"
        case .probing: return "Connecting…"
        case .downloading: return "Receiving data…"
        case .paused: return "Paused"
        case .finished: return "Complete"
        case .failed: return "Failed"
        }
    }

    /// The picture and sound pieces of a video, like the connection list of a normal download.
    private func videoParts(_ item: DownloadRecord) -> some View {
        let parts = item.videoParts ?? []
        return Grid(alignment: .leading, horizontalSpacing: 18, verticalSpacing: 3) {
            GridRow {
                Text("N.").bold()
                Text("Piece").bold()
                Text("Downloaded").bold()
                Text("Info").bold()
            }
            if parts.isEmpty {
                GridRow {
                    Text("1")
                    Text("Video")
                    Text(bytes(item.received)).monospacedDigit()
                    Text(item.status.isActive ? "Connecting…" : "Waiting").foregroundStyle(.secondary)
                }
            }
            ForEach(Array(parts.enumerated()), id: \.offset) { i, p in
                let complete = p.total.map { p.done >= $0 } ?? false
                GridRow {
                    Text("\(i + 1)")
                    Text(p.name)
                    Text(bytes(p.done) + (p.total.map { " of " + bytes($0) } ?? "")).monospacedDigit()
                    Text(complete ? "Complete" : item.status == .downloading ? (item.videoStep ?? "Receiving data…")
                         : item.status == .probing ? "Connecting…" : "Waiting")
                        .foregroundStyle(.secondary)
                }
            }
        }
        .font(.caption)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func connections(_ item: DownloadRecord) -> some View {
        ScrollView {
            Grid(alignment: .leading, horizontalSpacing: 18, verticalSpacing: 3) {
                GridRow {
                    Text("N.").bold()
                    Text("Downloaded").bold()
                    Text("Info").bold()
                }
                ForEach(item.segments, id: \.id) { s in
                    GridRow {
                        Text("\(s.id + 1)")
                        Text(bytes(s.written)).monospacedDigit()
                        Text(s.isComplete ? "Complete" : item.status == .downloading ? "Receiving data…"
                             : item.status == .probing ? "Connecting…" : "Waiting")
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .font(.caption)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(minHeight: 110, maxHeight: .infinity)
    }

    private func cancel(_ item: DownloadRecord) {
        let alert = NSAlert()
        alert.messageText = "Cancel this download?"
        alert.informativeText = "\(item.fileName) is removed from the list and the part already downloaded is deleted."
        alert.addButton(withTitle: "Cancel Download")
        alert.addButton(withTitle: "Keep Downloading")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        close()
        manager.remove(id)
    }
}

/// A small filled line chart of the recent transfer rate.
struct SpeedGraph: View {
    let samples: [Double]

    var body: some View {
        Canvas { ctx, size in
            ctx.fill(Path(roundedRect: CGRect(origin: .zero, size: size), cornerRadius: 4), with: .color(.secondary.opacity(0.08)))
            guard samples.count > 1, let top = samples.max(), top > 0 else { return }
            let step = size.width / CGFloat(max(samples.count - 1, 1))
            var line = Path()
            for (i, v) in samples.enumerated() {
                let p = CGPoint(x: CGFloat(i) * step, y: size.height - CGFloat(v / top) * (size.height - 4))
                if i == 0 { line.move(to: p) } else { line.addLine(to: p) }
            }
            var area = line
            area.addLine(to: CGPoint(x: CGFloat(samples.count - 1) * step, y: size.height))
            area.addLine(to: CGPoint(x: 0, y: size.height))
            area.closeSubpath()
            ctx.fill(area, with: .color(brand.opacity(0.18)))
            ctx.stroke(line, with: .color(brand), lineWidth: 1.5)
        }
    }
}

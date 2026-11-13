import AppKit
import NexonicCore
import QuickLookThumbnailing
import SwiftUI

/// Playlists and channels, and the Media Library (Milestone 8).
enum MediaWindows {
    /// Profile Batch windows, one per profile (several searches can run at once), by profile address ("" = no link yet).
    @MainActor private static var playlists: [String: NSWindow] = [:]
    @MainActor private static var library: NSWindow?

    @MainActor private static var lastHidden: String?

    /// Close: hides the Profile Batch window of this link; whatever it was doing keeps going (a signed-in search in the
    /// downloads list, or what visitors see). Profile Batch in the toolbar, or double-clicking the search's row, shows it again.
    @MainActor static func closePlaylist(_ link: String) {
        let key = link.isEmpty ? "" : ProfileBatch.key(link)
        let k = playlists[key] != nil ? key : playlists.first(where: { $0.value.isKeyWindow })?.key
        guard let k, let w = playlists[k] else { return }
        w.orderOut(nil)
        lastHidden = k
    }

    /// Profile Batch is open with this link.
    @MainActor static func isShowing(_ link: String) -> Bool { playlists[ProfileBatch.key(link)] != nil }

    /// Lists a profile's, playlist's or channel's videos (and photos); the chosen ones go into a queue of their own. One
    /// window per profile: opening one that is open brings it to the front.
    @MainActor static func showPlaylist(_ link: String = "") {
        // Profile Batch from the toolbar: the window hidden last (Close), still working, comes back first.
        if link.isEmpty, let k = lastHidden, let w = playlists[k], !w.isVisible {
            lastHidden = nil
            NSApp.activate(ignoringOtherApps: true); w.makeKeyAndOrderFront(nil); w.orderFrontRegardless()
            return
        }
        let start = link.isEmpty ? (NSPasteboard.general.string(forType: .string).flatMap { DownloadJob(url: $0.trimmingCharacters(in: .whitespacesAndNewlines)).validURL != nil ? $0 : nil } ?? "") : link
        let key = start.isEmpty ? "" : ProfileBatch.key(start)
        if let w = playlists[key] {
            NSApp.activate(ignoringOtherApps: true); w.makeKeyAndOrderFront(nil); w.orderFrontRegardless()
            return
        }
        let w = NSWindow(contentViewController: NSHostingController(rootView: ProfileBatchView(link: start).environmentObject(DownloadManager.shared)))
        w.title = String(localized: "Profile Batch")
        w.styleMask = [.titled, .closable, .resizable, .miniaturizable]
        w.isReleasedWhenClosed = false
        w.setContentSize(NSSize(width: 680, height: 640))
        w.center()
        if !playlists.isEmpty, let last = playlists.values.first { w.setFrameOrigin(NSPoint(x: last.frame.minX + 28, y: last.frame.minY - 28)) }
        playlists[key] = w
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { _ in
            // Only this window's entry: another window may have taken the same profile meanwhile.
            Task { @MainActor in if playlists[key] === w { playlists[key] = nil } }
        }
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
        w.orderFrontRegardless()  // in front of the downloads window, also when the browser handed the profile over
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
        guard SiteVideoTools.ensureInstalled() else { throw MediaError(SiteVideoTools.missing) }
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

/// Profile Batch: one link → every video open to everyone there (no login), filtered, downloaded in its own queue.
struct ProfileBatchView: View {
    @EnvironmentObject var manager: DownloadManager
    @State var link: String

    init(link: String) { _link = State(initialValue: link); windowLink = link }

    @State private var lastLink = ""
    /// The signed-in search whose result this window shows (its profile address), so it is shown once.
    @State private var shownSearch = ""
    /// The link this window was opened with (its entry among the Profile Batch windows).
    private let windowLink: String
    @State private var quality = "best"
    @State private var mode = 0  // 0 all, 1 new only, 2 preview
    // Found videos wait here until the user picks Download Now or Add to Queue: nothing starts by itself.
    @State private var pending: [BatchEntry] = []
    @State private var pendingCounts: BatchCounts?
    @State private var ownFolder = true
    /// Where this batch is saved (the downloads folder until another is chosen).
    @State private var saveFolder: URL?
    @State private var parallel = 2
    @State private var useFrom = false
    @State private var useTo = false
    @State private var from = Date().addingTimeInterval(-30 * 86400)
    @State private var to = Date()
    @State private var keywords = ""
    @State private var minMinutes = ""
    @State private var maxMinutes = ""
    @State private var kind = "all"
    @State private var maxCount = ProfileBatch.defaultMax
    @State private var checkHours = 0
    @State private var status = ""
    @State private var busy = false
    @State private var stopRequested = false
    @State private var scan: ProfileBatch.Scan?
    @State private var preview: [BatchEntry] = []
    @State private var chosen: Set<String> = []

    var filter: BatchFilter {
        var f = BatchFilter()
        f.from = useFrom ? from : nil; f.to = useTo ? to : nil; f.keywords = keywords; f.kind = kind
        f.minSeconds = Int(minMinutes).map { $0 * 60 }; f.maxSeconds = Int(maxMinutes).map { $0 * 60 }
        f.max = max(1, min(maxCount, ProfileBatch.limit))
        return f
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Paste a link to a profile, channel, playlist, gallery or page. Nexonic finds every video (and, on Instagram, TikTok, X and similar sites, every photo) using your browser's sign-in, skips the ones you already have and downloads the rest in their own queue. Facebook pages: what visitors see.")
                .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            TextField("Link", text: $link).textFieldStyle(.roundedBorder).onSubmit(find)
                // Pasting a whole link starts Find Videos straight away (typing one doesn't, until Return).
                .onChange(of: link) { new in
                    defer { lastLink = new }
                    if !busy, new.count - lastLink.count > 8, DownloadJob(url: new.trimmingCharacters(in: .whitespacesAndNewlines)).validURL != nil { find() }
                }
            Form {
                Picker("Quality", selection: $quality) {
                    ForEach(ProfileBatch.qualities.map { $0.key }, id: \.self) { k in
                        Text(LocalizedStringKey(ProfileBatch.qualities.first { $0.key == k }?.title ?? k)).tag(k)
                    }
                }
                Picker("Download", selection: $mode) {
                    Text("All videos I don't have yet").tag(0)
                    Text("Only new videos (not seen on this profile before)").tag(1)
                    Text("Let me preview and choose").tag(2)
                }
                LabeledContent("Save to") {
                    HStack {
                        Text((saveFolder ?? manager.settings.startFolder).path).lineLimit(1).truncationMode(.middle)
                        Button("Choose…") {
                            let panel = NSOpenPanel()
                            panel.canChooseDirectories = true; panel.canChooseFiles = false; panel.canCreateDirectories = true
                            panel.directoryURL = saveFolder ?? manager.settings.startFolder
                            if panel.runModal() == .OK, let u = panel.url { saveFolder = u }
                        }
                        Toggle("A folder for each profile", isOn: $ownFolder)
                    }
                }
                Stepper("Downloads at the same time: \(parallel)", value: $parallel, in: 1...6)
                DisclosureGroup("Filters") {
                    HStack { Toggle("Published on or after", isOn: $useFrom); DatePicker("", selection: $from, displayedComponents: .date).labelsHidden().disabled(!useFrom) }
                    HStack { Toggle("Published on or before", isOn: $useTo); DatePicker("", selection: $to, displayedComponents: .date).labelsHidden().disabled(!useTo) }
                    TextField("Title contains (words; !word leaves it out)", text: $keywords)
                    HStack { TextField("Shortest (minutes)", text: $minMinutes); TextField("Longest (minutes)", text: $maxMinutes) }
                    Picker("Type", selection: $kind) {
                        Text("Everything").tag("all"); Text("Videos (no shorts or live streams)").tag("videos")
                        Text("Shorts and reels").tag("shorts"); Text("Live streams").tag("live")
                    }
                    Stepper("At most \(maxCount) videos", value: $maxCount, in: 10...ProfileBatch.limit, step: 50)
                    Text("A site that doesn't say when a video was published or how long it is: those videos are kept.").font(.caption).foregroundStyle(.secondary)
                }
                Picker("Look for new videos", selection: $checkHours) {
                    Text("Never (only now)").tag(0); Text("Every hour").tag(1); Text("Every 6 hours").tag(6); Text("Every day").tag(24); Text("Every week").tag(168)
                }
            }
            .formStyle(.grouped)
            if busy { ProgressView().controlSize(.small) }
            if !status.isEmpty { Text(status).font(.callout).foregroundStyle(.secondary).textSelection(.enabled) }
            if !preview.isEmpty {
                List(preview) { e in
                    Toggle(isOn: Binding(get: { chosen.contains(e.url) }, set: { if $0 { chosen.insert(e.url) } else { chosen.remove(e.url) } })) {
                        HStack {
                            Text(e.title).lineLimit(1)
                            Spacer()
                            if let d = e.date { Text(d, style: .date).foregroundStyle(.secondary) }
                        }
                    }.help(e.url)
                }.frame(minHeight: 160)
            }
            HStack {
                Menu("Export") {
                    Button("Text (one link per line)") { export("txt") }
                    Button("CSV") { export("csv") }
                    Button("JSON") { export("json") }
                }.disabled(scan == nil).fixedSize()
                Spacer()
                // While a profile is listed in the background, Close leaves it running (Nexonic reopens Profile Batch).
                Button("Close") { MediaWindows.closePlaylist(windowLink) }
                Button("Stop Search") {
                    stopRequested = true
                    let key = ProfileBatch.key(link.trimmingCharacters(in: .whitespacesAndNewlines))
                    if let l = manager.listings[key], l.running { l.stop.set(); busy = false; status = String(localized: "Stopped.") }
                }.disabled(!busy)
                if !preview.isEmpty || !pending.isEmpty {
                    Button("Add to Queue") { hand(start: false) }
                    Button("Download Now") { hand(start: true) }.keyboardShortcut(.defaultAction)
                    Button("Find Videos") { find(again: true) }.disabled(busy)
                } else {
                    Button("Find Videos") { find(again: true) }.keyboardShortcut(.defaultAction).disabled(busy)
                }
            }
        }
        .padding(16)
        .frame(minWidth: 600, minHeight: 560)
        .onAppear { lastLink = link; if !link.isEmpty { find() } }
        // A background listing finished while this window is open: show it (or what visitors see).
        // The signed-in search of this window's profile is the truth: its count while it runs, its result when it's done
        // (never what visitors see while it exists).
        .onReceive(manager.$listings) { all in
            let key = ProfileBatch.key(link.trimmingCharacters(in: .whitespacesAndNewlines))
            guard let l = all[key], !l.stopped else { return }
            if l.follow != nil {
                // Chosen already: what the search finds is added that way; this window only counts.
                busy = l.running
                status = l.running
                    ? String(format: String(localized: "%d photos and videos found so far; new ones are added as you chose. You can close this window: the search keeps going in the downloads list."), l.found)
                    : String(format: String(localized: "%d photos and videos found; all added as you chose."), l.found)
                if let w = l.waiting, l.running { status += "\n" + String(format: String(localized: "The site stopped answering for a while; the search continues from where it got to in %d min."), w) }
                if !l.running { shownSearch = key }
            } else if l.running {
                busy = true
                // The page's posts and what the search found so far, ready to download now; more join as they are found.
                if !l.entries.isEmpty, mode != 2 || preview.isEmpty {
                    show(SocialPages.scan(url: l.url, owner: l.owner, entries: l.entries, note: ""), url: l.url, filter: filter)
                    status += "\n" + String(localized: "Still searching with your browser's sign-in; what it finds later follows your choice. You can close this window: the search keeps going in the downloads list.")
                    if let w = l.waiting { status += "\n" + String(format: String(localized: "The site stopped answering for a while; the search continues from where it got to in %d min."), w) }
                }
            } else if l.scan != nil, shownSearch != key, !l.pageOnly {
                DispatchQueue.main.async { find() }  // after the change is stored
            } else if l.visitorsOnly, busy {
                DispatchQueue.main.async { find() }  // the signed-in search didn't work: what visitors see, and why
            }
        }
    }

    private func target(_ s: ProfileBatch.Scan) -> URL {
        let base = saveFolder ?? manager.settings.startFolder
        return ownFolder ? ProfileBatch.folder(base: base, owner: s.owner, url: s.url) : base
    }

    /// Download Now or Add to Queue: the chosen videos of the preview, or everything that was found.
    private func hand(start: Bool) {
        if !preview.isEmpty { download(preview.filter { chosen.contains($0.url) }, nil, start: start) }
        else { download(pending, pendingCounts, start: start) }
        pending = []; pendingCounts = nil
        // A search still running: what it finds later goes the same way (folder, quality, now or later).
        if let s = scan, manager.listings[ProfileBatch.key(s.url)]?.running == true {
            manager.listings[ProfileBatch.key(s.url)]?.follow = ProfileListing.Follow(folder: target(s), quality: quality, parallel: parallel, start: start)
        }
    }

    private func download(_ take: [BatchEntry], _ counts: BatchCounts?, start: Bool) {
        guard let s = scan else { return }
        var c = counts ?? ProfileBatch.choose(take, BatchFilter(), alreadyDownloaded: manager.alreadyDownloaded).counts
        c.queued = manager.queueBatch(owner: s.owner, take, folder: target(s), quality: quality, maxActive: parallel, start: start)
        status = (s.owner.isEmpty ? "" : s.owner + ": ") + c.text
        preview = []
        let key = ProfileBatch.key(s.url)
        var saved = manager.profiles.first { ProfileBatch.key($0.url) == key }
        if saved == nil && (checkHours > 0 || mode == 1) { saved = SavedProfile(url: s.url) }
        guard var p = saved else { return }
        p.title = s.owner; p.folder = target(s).path; p.quality = quality; p.filter = filter; p.checkHours = checkHours
        p.lastChecked = Date(); p.lastResult = status
        var seen = Set(p.seen)
        for e in s.entries where seen.insert(ProfileBatch.key(e.url)).inserted { p.seen.append(ProfileBatch.key(e.url)) }
        if let i = manager.profiles.firstIndex(where: { $0.id == p.id }) { manager.profiles[i] = p } else { manager.profiles.append(p) }
        manager.saveProfiles()
    }

    /// Shows what was found for the link, or looks for it. `again` (the Find Videos button): searches the site again even
    /// when a search was done, and adds what is new.
    private func find(again: Bool = false) {
        let url = link.trimmingCharacters(in: .whitespacesAndNewlines)
        guard DownloadJob(url: url).validURL != nil else { status = String(localized: "Paste a web link (http or https)."); return }
        busy = true; stopRequested = false; preview = []; pending = []; pendingCounts = nil
        let earlier = ProfileBatch.loadScan(url).map { $0.complete ? 0 : $0.entries.count } ?? 0
        status = earlier > 0 ? String(format: String(localized: "Continuing an interrupted scan (%d found before)…"), earlier)
            : String(localized: "Looking for videos… (large profiles can take a few minutes)")
        let proxy = manager.settings.proxy, f = filter, stopFlag = { [self] in stopRequested }
        let key = ProfileBatch.key(url)
        if let l = manager.listings[key], l.pageOnly, let social = SocialPages.find(url) {
            // Facebook: the page is read in a hidden view with the browser's sign-in the extension handed over, scrolled
            // down until it shows no more; each Find Videos reads it again and adds what is new.
            let cookies = manager.profileCookies[key]?.cookies
            shownSearch = key
            status = String(format: String(localized: "Reading the %@ page in the background, scrolling until it shows no more…"), social.site.name)
            Task { @MainActor in
                do {
                    let s = try await SocialWebView.scan(url, site: social.site, owner: social.owner, pages: social.pages, max: ProfileBatch.limit,
                                                         cookies: cookies, seed: l.entries,
                                                         found: { n in status = String(format: String(localized: "%d found so far…"), n) },
                                                         stop: { stopRequested })
                    manager.listings[key]?.entries = s.entries
                    manager.listings[key]?.found = s.entries.count
                    manager.listings[key]?.scan = s
                    if let row = manager.items.first(where: { $0.listing == true && ProfileBatch.key($0.url.absoluteString) == key })?.id {
                        manager.update(row) { $0.streamDone = s.entries.count }
                    }
                    show(s, url: url, filter: f)
                } catch is CancellationError {
                    status = String(localized: "Stopped.")
                } catch {
                    status = error.localizedDescription
                }
                busy = false
            }
            return
        }
        if again, let l = manager.listings[key], !l.running, !l.pageOnly, manager.canSearchSignedIn(url) {
            status = String(localized: "Searching again with your browser's sign-in; what is new is added to the list.")
            manager.startListing(url, max: ProfileBatch.limit, seed: l.entries)
            return
        }
        if let listing = manager.listings[key], !listing.running, let found = listing.scan {
            // Listed in the background (the extension's Download all): show it.
            busy = false
            shownSearch = key
            show(found, url: url, filter: f)
            return
        }
        if let l = manager.listings[key], l.running {
            status = String(localized: "Searching every photo and video with your browser's sign-in…")
            if l.follow == nil, !l.entries.isEmpty { show(SocialPages.scan(url: l.url, owner: l.owner, entries: l.entries, note: ""), url: url, filter: f) }
            return
        }
        if manager.canSearchSignedIn(url) {
            // Opened by the extension's Download all: the gallery engine lists every photo and video with the browser's
            // sign-in, a request every 1-2 seconds, in the background; this window may close meanwhile.
            status = String(localized: "Searching every photo and video with your browser's sign-in, a request every 1–2 seconds (the first time, macOS asks to let Nexonic use that sign-in). You can close this window: the search keeps going in the downloads list (\"Photos and videos of …\"); double-click it there to come back.")
            manager.startListing(url, max: f.max)
            return
        }
        // The signed-in search didn't work: what visitors see, below, and why.
        let signedInError = manager.listings[key]?.visitorsOnly == true ? manager.listings[key]?.error : nil
        if manager.listings[key]?.visitorsOnly == true { manager.listings[key] = nil }
        if let social = SocialPages.find(url) {
            // Instagram and Facebook show visitors only their latest posts: Nexonic takes those straight away and says how to
            // get the rest (the extension's Download all in the user's signed-in browser; Nexonic never signs in).
            Task { @MainActor in
                do {
                    status = String(format: String(localized: "Opening the %@ profile like a visitor's browser…"), social.site.name)
                    var s = try await SocialWebView.scan(url, site: social.site, owner: social.owner, pages: social.pages, max: f.max,
                                                         found: { n in status = String(format: String(localized: "%d found so far…"), n) },
                                                         stop: { stopRequested })
                    // A signed-in search of this profile started meanwhile: its result wins.
                    if let l = manager.listings[key], l.running || l.scan != nil { return }
                    let why = signedInError.map { String(format: String(localized: "The search with your browser's sign-in didn't work (%@), so these are what visitors see."), $0) }
                    s.note = [s.note ?? "", why ?? String(localized: String.LocalizationValue(SocialPages.more(social.site)))].filter { !$0.isEmpty }.joined(separator: "\n")
                    show(s, url: url, filter: f)
                } catch is CancellationError {
                    status = String(localized: "Stopped.")
                } catch {
                    status = error.localizedDescription
                }
                busy = false
            }
            return
        }
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result {
                try ProfileBatch.discover(url, proxy: proxy, max: f.max, stop: { DispatchQueue.main.sync { stopFlag() } }) { _, n in
                    if n % 10 == 0 { DispatchQueue.main.async { status = String(format: String(localized: "%d found so far…"), n) } }
                }
            }
            DispatchQueue.main.async {
                busy = false
                switch result {
                case .failure(let e) where e is CancellationError:
                    status = String(localized: "Stopped. The videos found so far are kept; Find Videos continues from there.")
                case .failure(let e):
                    status = e.localizedDescription
                case .success(let s):
                    show(s, url: url, filter: f)
                }
            }
        }
    }

    /// Shows a finished scan: the preview list, or the counts, or the queued downloads.
    private func show(_ s: ProfileBatch.Scan, url: String, filter: BatchFilter) {
        scan = s
        // A signed-in search lists files, not videos: all of them are offered (a carousel post alone can hold 20).
        var f = filter
        if manager.listings[ProfileBatch.key(url)] != nil { f.max = ProfileBatch.limit }
        var seen: Set<String>? = nil
        if mode == 1, let p = manager.profiles.first(where: { ProfileBatch.key($0.url) == ProfileBatch.key(url) }) { seen = Set(p.seen) }
        let (take, counts) = ProfileBatch.choose(s.entries, f, alreadyDownloaded: manager.alreadyDownloaded, seen: seen)
        if mode == 2 {
            preview = take; chosen = Set(take.map(\.url))
            var c = counts; c.queued = 0
            status = (s.owner.isEmpty ? "" : s.owner + ": ") + c.text
        } else if take.isEmpty {
            var c = counts; c.queued = 0
            status = (s.owner.isEmpty ? "" : s.owner + ": ") + c.text
        } else {
            pending = take; pendingCounts = counts
            var c = counts; c.queued = 0
            status = (s.owner.isEmpty ? "" : s.owner + ": ") + c.text + "\n" + String(localized: "Choose Download Now, or Add to Queue to start them later.")
        }
        if let note = s.note, !note.isEmpty { status += "\n" + note }  // what the site doesn't show visitors
    }

    private func export(_ format: String) {
        guard let s = scan else { return }
        let panel = NSSavePanel()
        panel.nameFieldStringValue = FileNames.sanitize(s.owner.isEmpty ? "videos" : s.owner) + "." + format
        guard panel.runModal() == .OK, let url = panel.url else { return }
        try? ProfileBatch.export(s.entries, format: format).write(to: url, atomically: true, encoding: .utf8)
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

import SwiftUI

/// What to download: from the Share sheet, the built-in browser, a Shortcut or the Add button.
public struct DownloadInput: Identifiable, Sendable {
    public let id = UUID()
    public var urls: [URL]
    public var referer: URL?
    public var userAgent: String?
    public var suggestedName: String?

    public init(urls: [URL], referer: URL? = nil, userAgent: String? = nil, suggestedName: String? = nil) {
        self.urls = urls; self.referer = referer; self.userAgent = userAgent; self.suggestedName = suggestedName
    }
}

/// The Nexonic download window: file name, size, type, quality (when the page offers several),
/// and Download Now / Download Later / Cancel. Used by the Share extension and the app.
public struct DownloadWindow: View {
    let input: DownloadInput
    let sessionID: String
    let done: (String?) -> Void

    @State private var probing = true
    @State private var probe: ProbeResult?
    @State private var choices: [MediaChoice] = []
    @State private var choice: MediaChoice?
    @State private var name = ""
    @State private var nameEdited = false
    @State private var problem: String?
    @State private var nothingFound = false

    public init(input: DownloadInput, sessionID: String, done: @escaping (String?) -> Void) {
        self.input = input; self.sessionID = sessionID; self.done = done
    }

    private var single: URL? { input.urls.count == 1 ? input.urls[0] : nil }
    private var target: URL? { choice?.url ?? probe?.finalURL ?? single }
    private var canDownload: Bool { input.urls.count > 1 || (target != nil && !nothingFound && !probing) }

    public var body: some View {
        NavigationStack {
            Form {
                if let single {
                    Section {
                        if !nothingFound {
                            TextField("File name", text: Binding(get: { name }, set: { name = $0; nameEdited = true }))
                                .textInputAutocapitalization(.never).autocorrectionDisabled()
                        }
                        Text(single.absoluteString).font(.footnote).foregroundStyle(.secondary).lineLimit(2).textSelection(.enabled)
                    } header: { Text("Download") }

                    if choices.count > 1 {
                        Section {
                            Picker("Quality", selection: $choice) {
                                ForEach(choices) { c in Text("\(c.kind.rawValue) · \(c.label)").tag(Optional(c)) }
                            }
                            .pickerStyle(.inline).labelsHidden()
                        } header: { Text("Found on this page") }
                    }

                    if !nothingFound {
                        Section {
                            row("Size", probing ? "Checking…" : Format.size(probe?.size))
                            row("Type", FileCategory(fileName: name).rawValue == "Other" ? "General" : FileCategory(fileName: name).rawValue)
                            row("Saved to", savedTo)
                            row("Resume", probing ? "…" : (probe?.resumable == true ? "Yes" : "No (starts over if stopped)"))
                        }
                    }
                    if probing { HStack { ProgressView(); Text("Asking the server…").foregroundStyle(.secondary) } }
                    if let problem { Text(problem).foregroundStyle(.red).font(.footnote) }
                    if nothingFound {
                        Text("No file or video to download was found on this page. Streaming and copy-protected video can't be saved.")
                            .font(.footnote).foregroundStyle(.secondary)
                    }
                } else {
                    Section {
                        ForEach(input.urls, id: \.self) { u in
                            Text(FileNames.fromURL(u)).lineLimit(1)
                        }
                    } header: { Text("\(input.urls.count) links") }
                }
            }
            .navigationTitle("Download with Nexonic")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { done(nil) } }
            }
            .safeAreaInset(edge: .bottom) {
                HStack(spacing: 12) {
                    Button { add(startNow: false) } label: { Text("Download Later").frame(maxWidth: .infinity) }
                        .buttonStyle(.bordered).controlSize(.large)
                    Button { add(startNow: true) } label: { Text("Download Now").bold().frame(maxWidth: .infinity) }
                        .buttonStyle(.borderedProminent).controlSize(.large)
                }
                .disabled(!canDownload)
                .padding()
                .background(.bar)
            }
            .task { await load() }
            .onChange(of: choice) { _, c in
                guard let c else { return }
                Task { await probeChoice(c.url) }
            }
        }
    }

    private var savedTo: String {
        let folder = SharedSettings.categoryFolders ? FileCategory(fileName: name).folder.map { " › \($0)" } ?? "" : ""
        return "Files › Nexonic" + folder
    }

    private func row(_ label: String, _ value: String) -> some View {
        LabeledContent(label) { Text(value).multilineTextAlignment(.trailing) }
    }

    private func load() async {
        guard let single else { probing = false; return }
        if let s = input.suggestedName, !s.isEmpty { name = FileNames.sanitize(s) }
        do {
            let p = try await Probe.run(single)
            if p.isPage {
                // A web page: offer the videos and files on it.
                let found = await Probe.scan(page: p.finalURL)
                choices = found
                if found.isEmpty { nothingFound = true; probing = false; return }
                choice = found[0] // probes it through onChange
                return
            }
            apply(p)
        } catch {
            problem = (error as? LocalizedError)?.errorDescription ?? "Could not reach the server. You can still save it for later."
            if name.isEmpty { name = FileNames.fromURL(single) }
            probing = false
        }
    }

    private func probeChoice(_ url: URL) async {
        probing = true
        nameEdited = false
        if let p = try? await Probe.run(url) { apply(p) } else { name = FileNames.fromURL(url); probing = false }
    }

    private func apply(_ p: ProbeResult) {
        probe = p
        if !nameEdited { name = p.fileName }
        probing = false
    }

    private func add(startNow: Bool) {
        if let target, single != nil {
            let job = DownloadJob(url: target, fileName: name.isEmpty ? FileNames.fromURL(target) : name, size: probe?.size,
                                  referer: input.referer ?? (choice != nil ? single : nil), userAgent: input.userAgent,
                                  quality: choice.map { $0.label })
            Library.add(job, startNow: startNow, sessionID: sessionID)
        } else {
            for u in input.urls {
                Library.add(DownloadJob(url: u, fileName: FileNames.fromURL(u), referer: input.referer, userAgent: input.userAgent),
                            startNow: startNow, sessionID: sessionID)
            }
        }
        if startNow { Notify.requestPermission() }
        let what = input.urls.count > 1 ? "\(input.urls.count) downloads" : "Download"
        done(startNow ? "\(what) started. Follow it in Nexonic." : "\(what) saved for later.")
    }
}

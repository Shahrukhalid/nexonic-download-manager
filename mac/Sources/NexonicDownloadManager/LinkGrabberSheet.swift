import AppKit
import NexonicCore
import Security
import SwiftUI

// MARK: Link Grabber

/// File → Link Grabber: links from pasted text or a web page, grouped into packages (split archives together). Each
/// package can get its own folder and is unpacked once every part of it is done.
struct LinkGrabberSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    let queueID: String
    @State private var text = ""
    @State private var packages: [LinkGrabber.Package] = []
    @State private var checked = Set<String>()
    @State private var status = ""
    @State private var task: Task<Void, Never>?
    @State private var folder = URL(fileURLWithPath: NSHomeDirectory())
    @State private var queue = DownloadQueue.mainID
    @State private var ownFolders = true
    @State private var unpack = true

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Link Grabber").font(.title2.bold())
            Text("Paste text with links, or a web page address and press Find Links. Parts of a split archive are grouped into one package.")
                .foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            TextEditor(text: $text).font(.body.monospaced()).frame(minHeight: 70, maxHeight: 90)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(.quaternary))
            HStack {
                Button(task == nil ? "Find Links" : "Stop") { if task == nil { find() } else { stop() } }
                    .disabled(text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                Text(status).font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("All") { checked = Set(packages.flatMap(\.links)) }.disabled(packages.isEmpty)
                Button("None") { checked = [] }.disabled(packages.isEmpty)
            }
            List {
                ForEach(packages, id: \.name) { p in
                    Section {
                        ForEach(p.links, id: \.self) { link in
                            Toggle(isOn: Binding(get: { checked.contains(link) }, set: { on in if on { checked.insert(link) } else { checked.remove(link) } })) {
                                Text(link).lineLimit(1).truncationMode(.middle)
                            }
                        }
                    } header: {
                        Toggle(isOn: Binding(get: { p.links.allSatisfy(checked.contains) },
                                             set: { on in p.links.forEach { if on { checked.insert($0) } else { checked.remove($0) } } })) {
                            Text("\(p.name)  (\(p.links.count))").bold()
                        }
                    }
                }
            }
            .frame(height: 200)
            Form {
                SaveOptions(folder: $folder, queueID: $queue)
                Toggle("Put each package in its own folder", isOn: $ownFolders)
                Toggle("Unpack a package's archives when all of it is done", isOn: $unpack)
            }
            .formStyle(.grouped)
            HStack {
                Spacer()
                Button("Close") { stop(); dismiss() }.keyboardShortcut(.cancelAction)
                Button("Download Later") { add(false) }.disabled(checked.isEmpty)
                Button("Download Checked") { add(true) }.keyboardShortcut(.defaultAction).disabled(checked.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 660)
        .onAppear {
            folder = manager.settings.startFolder
            queue = queueID
            let clip = NSPasteboard.general.string(forType: .string) ?? ""
            text = clip.count > 20000 ? "" : clip
            if LinkGrabber.pageAddress(text) == nil, !text.isEmpty { show(LinkGrabber.find(text)) }
        }
    }

    private func show(_ links: [String]) {
        packages = LinkGrabber.group(links)
        checked = Set(links)
        status = links.isEmpty ? "No links found." : "\(links.count) links in \(packages.count) packages"
    }

    @MainActor private func find() {
        guard let page = LinkGrabber.pageAddress(text) else { return show(LinkGrabber.find(text)) }
        status = "Reading the page…"
        let session = URLSession(configuration: Network.configuration(proxy: manager.settings.proxy))
        task = Task {
            do {
                var request = URLRequest(url: page, timeoutInterval: 30)
                request.setValue("text/html,application/xhtml+xml", forHTTPHeaderField: "Accept")
                let (data, _) = try await session.data(for: request)
                show(LinkGrabber.find(String(decoding: data.prefix(8 * 1024 * 1024), as: UTF8.self), page: page))
            } catch is CancellationError {
                status = "Stopped."
            } catch {
                status = "Could not read the page: \(error.localizedDescription)"
            }
            task = nil
        }
    }

    private func stop() {
        task?.cancel()
        task = nil
    }

    private func add(_ start: Bool) {
        manager.settings.usedFolder(folder)
        for p in packages {
            let links = p.links.filter(checked.contains)
            guard !links.isEmpty else { continue }
            let isPackage = p.name != LinkGrabber.otherFiles
            var o = DownloadManager.AddOptions()
            o.folder = isPackage && ownFolders ? folder.appendingPathComponent(p.name, isDirectory: true) : folder
            o.queueID = queue
            o.start = start
            o.package = isPackage ? p.name : nil
            o.unpack = isPackage && unpack
            for link in links { manager.add(DownloadJob(url: link), options: o) }
        }
        stop()
        dismiss()
    }
}

// MARK: Check Server (network diagnostics)

/// Downloads › Check Server…: one request for the first byte of a link, with the times macOS measured for each step
/// (name lookup, connection, secure connection) and the server's answer. Cookies and passwords are never sent.
struct DiagnosticsSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    @State var link: String
    @State private var report = ""
    @State private var task: Task<Void, Never>?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Check Server").font(.title2.bold())
            Text("Checks a link with your connection settings: name lookup, connection, secure connection and the server's answer. Cookies and passwords are never sent.")
                .foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            TextField("Link", text: $link, prompt: Text("https://example.com/file.zip"))
            ScrollView {
                Text(report.isEmpty ? " " : report).font(.body.monospaced()).textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .frame(height: 200)
            .padding(6)
            .overlay(RoundedRectangle(cornerRadius: 6).stroke(.quaternary))
            HStack {
                Button("Copy") { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(report, forType: .string) }
                    .disabled(report.isEmpty)
                Spacer()
                Button("Close") { task?.cancel(); dismiss() }.keyboardShortcut(.cancelAction)
                Button(task == nil ? "Run" : "Stop") { if task == nil { run() } else { task?.cancel(); task = nil } }
                    .keyboardShortcut(.defaultAction)
                    .disabled(DownloadJob(url: link).validURL == nil)
            }
        }
        .padding(20)
        .frame(width: 600)
        .onAppear { if link.isEmpty { link = clipboardLink() } }
    }

    @MainActor private func run() {
        guard let url = DownloadJob(url: link).validURL else { return }
        report = "Checking…"
        let proxy = manager.settings.proxy
        task = Task {
            report = await ServerCheck.run(url, proxy: proxy)
            task = nil
        }
    }
}

/// Collects the timings macOS measures for one request.
final class MetricsCatcher: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    private let lock = NSLock()
    private var collected: URLSessionTaskMetrics?
    var metrics: URLSessionTaskMetrics? { lock.lock(); defer { lock.unlock() }; return collected }

    func urlSession(_ session: URLSession, task: URLSessionTask, didFinishCollecting metrics: URLSessionTaskMetrics) {
        lock.lock(); collected = metrics; lock.unlock()
    }
}

enum ServerCheck {
    static func ms(_ a: Date?, _ b: Date?) -> String {
        guard let a, let b else { return "" }
        return " (\(Int(b.timeIntervalSince(a) * 1000)) ms)"
    }

    static func tls(_ v: tls_protocol_version_t?) -> String {
        switch v {
        case .some(.TLSv13): return "TLS 1.3"
        case .some(.TLSv12): return "TLS 1.2"
        case .none: return "TLS"
        default: return "TLS (older version)"
        }
    }

    static func run(_ url: URL, proxy: ProxySettings) async -> String {
        var lines = ["OK    Settings: \(proxy.mode.title)\(proxy.host.isEmpty || proxy.mode == .system || proxy.mode == .none ? "" : " \(proxy.host)")"]
        let config = Network.configuration(proxy: proxy)
        config.httpShouldSetCookies = false
        config.httpCookieStorage = nil
        config.urlCredentialStorage = nil
        config.requestCachePolicy = .reloadIgnoringLocalCacheData
        let session = URLSession(configuration: config)
        var request = URLRequest(url: url, timeoutInterval: 30)
        request.setValue("bytes=0-0", forHTTPHeaderField: "Range")
        request.setValue("Mozilla/5.0 (compatible; NexonicDownloadManager)", forHTTPHeaderField: "User-Agent")
        let catcher = MetricsCatcher()
        do {
            let (_, response) = try await session.data(for: request, delegate: catcher)
            let t = catcher.metrics?.transactionMetrics.last
            if let t {
                let host = url.host ?? ""
                lines.append("OK    Name lookup: \(host)\(t.remoteAddress.map { " → \($0)" } ?? "")\(ms(t.domainLookupStartDate, t.domainLookupEndDate))")
                lines.append("OK    Connection: \(t.isProxyConnection ? "through the proxy" : "direct")\(t.remotePort.map { ", port \($0)" } ?? "")\(ms(t.connectStartDate, t.connectEndDate))")
                if url.scheme == "https" { lines.append("OK    Secure connection: \(tls(t.negotiatedTLSProtocolVersion))\(ms(t.secureConnectionStartDate, t.secureConnectionEndDate))") }
            }
            guard let http = response as? HTTPURLResponse else { return (lines + ["FAIL  Server answer: not a web server"]).joined(separator: "\n") }
            let code = http.statusCode
            var detail = "\(code) \(HTTPURLResponse.localizedString(forStatusCode: code))"
            if let final = http.url?.host, final != url.host { detail += ", redirected to \(final)" }
            if let type = http.mimeType { detail += ", \(type)" }
            if let range = http.value(forHTTPHeaderField: "Content-Range"), let total = range.split(separator: "/").last.flatMap({ Int64($0) }) {
                detail += ", \(bytes(total))"
            } else if code == 200, http.expectedContentLength > 0 {
                detail += ", \(bytes(http.expectedContentLength))"
            } else {
                detail += ", size not given"
            }
            if let server = http.value(forHTTPHeaderField: "Server") { detail += ", server \(server)" }
            let ok = (200..<300).contains(code)
            lines.append("\(ok ? "OK  " : "FAIL")  Server answer: \(detail)\(ms(t?.requestStartDate, t?.responseStartDate))")
            if ok {
                lines.append(code == 206 ? "OK    Resuming and parts: the server sends parts of the file, so downloads can pause, resume and use several connections."
                                         : "FAIL  Resuming and parts: the server sends only the whole file, so downloads cannot resume and use one connection.")
            } else if code == 401 || code == 403 {
                lines.append("FAIL  Hint: the site needs a login or cookies; start the download from the browser so the extension can pass them on.")
            }
        } catch is CancellationError {
            lines.append("Stopped.")
        } catch {
            lines.append("FAIL  \(error.localizedDescription)")
        }
        session.invalidateAndCancel()
        return lines.joined(separator: "\n")
    }
}

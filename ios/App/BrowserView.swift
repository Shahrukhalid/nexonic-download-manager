import NexonicKit
import SwiftUI
import UIKit
import WebKit

/// The built-in browser: downloads open the Nexonic download window, long-press a link to download it, and
/// "Found on this page" lists the videos, music and files on the page. Only web pages load here.
@Observable
final class BrowserState: NSObject, WKNavigationDelegate, WKUIDelegate {
    var address = ""
    var title = ""
    var progress = 0.0
    var canBack = false
    var canForward = false
    var found: [MediaChoice] = []
    var showFound = false
    @ObservationIgnored var onDownload: ((DownloadInput) -> Void)?
    @ObservationIgnored private var observations: [NSKeyValueObservation] = []
    @ObservationIgnored private(set) var web: WKWebView!

    override init() {
        super.init()
        web = makeWebView()
    }

    private func makeWebView() -> WKWebView {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default()
        config.allowsInlineMediaPlayback = true
        config.mediaTypesRequiringUserActionForPlayback = .all
        let w = WKWebView(frame: .zero, configuration: config)
        w.navigationDelegate = self
        w.uiDelegate = self
        w.allowsBackForwardNavigationGestures = true
        observations = [
            w.observe(\.estimatedProgress) { [weak self] w, _ in self?.progress = w.estimatedProgress },
            w.observe(\.url) { [weak self] w, _ in self?.address = w.url?.absoluteString ?? "" },
            w.observe(\.title) { [weak self] w, _ in self?.title = w.title ?? "" },
            w.observe(\.canGoBack) { [weak self] w, _ in self?.canBack = w.canGoBack },
            w.observe(\.canGoForward) { [weak self] w, _ in self?.canForward = w.canGoForward },
        ]
        return w
    }

    func go(_ input: String) {
        let text = input.trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty else { return }
        let url: URL?
        if let u = Net.webURL(text) { url = u }
        else if !text.contains(" "), text.contains(".") { url = URL(string: "https://" + text) }
        else { url = URL(string: "https://duckduckgo.com/?q=" + (text.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? text)) }
        if let url { web.load(URLRequest(url: url)) }
    }

    func startIfNeeded() { if web.url == nil { go("https://duckduckgo.com/") } }

    // MARK: Navigation

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction) async -> WKNavigationActionPolicy {
        guard let s = action.request.url?.scheme?.lowercased() else { return .cancel }
        if ["http", "https", "about", "blob", "data"].contains(s) { return .allow }
        return .cancel // other apps' links are not opened from here
    }

    /// Files the page can't show (or that the server marks as attachments) go to the Nexonic download window.
    func webView(_ webView: WKWebView, decidePolicyFor response: WKNavigationResponse) async -> WKNavigationResponsePolicy {
        let http = response.response as? HTTPURLResponse
        let attachment = http?.value(forHTTPHeaderField: "Content-Disposition")?.lowercased().hasPrefix("attachment") == true
        guard response.isForMainFrame, let url = response.response.url, Net.webURL(url.absoluteString) != nil,
              attachment || !response.canShowMIMEType || Media.isMedia(url) else { return .allow }
        await handOver(url, suggestedName: response.response.suggestedFilename)
        return .cancel
    }

    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) { found = [] }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        webView.evaluateJavaScript("document.documentElement.outerHTML.slice(0, 3000000)") { [weak self] result, _ in
            guard let self, let html = result as? String, let base = webView.url else { return }
            self.found = Probe.choices(in: html, base: base)
        }
    }

    /// Long-press a link: Download with Nexonic.
    func webView(_ webView: WKWebView, contextMenuConfigurationFor elementInfo: WKContextMenuElementInfo) async -> UIContextMenuConfiguration? {
        guard let link = elementInfo.linkURL, Net.webURL(link.absoluteString) != nil else { return nil }
        return UIContextMenuConfiguration(identifier: nil, previewProvider: nil) { suggested in
            let download = UIAction(title: "Download with Nexonic", image: UIImage(systemName: "arrow.down.circle")) { [weak self] _ in
                Task { await self?.handOver(Links.unwrap(link), suggestedName: nil) }
            }
            return UIMenu(children: [download] + suggested)
        }
    }

    /// Hands a link to the download window with the page as referrer and this site's cookies (same domains only).
    @MainActor
    func handOver(_ url: URL, suggestedName: String?) async {
        let cookies = await web.configuration.websiteDataStore.httpCookieStore.allCookies()
        let host = url.host?.lowercased() ?? ""
        for c in cookies where BrowserState.cookie(c.domain, matches: host) {
            HTTPCookieStorage.shared.setCookie(c) // the download session sends it only to its own domain
        }
        let ua = (try? await web.evaluateJavaScript("navigator.userAgent")) as? String
        onDownload?(DownloadInput(urls: [url], referer: web.url, userAgent: ua, suggestedName: suggestedName))
    }

    static func cookie(_ domain: String, matches host: String) -> Bool {
        let d = domain.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "."))
        return host == d || host.hasSuffix("." + d)
    }

    /// Clears cookies, site data, cache and history.
    func clearData() async {
        let store = WKWebsiteDataStore.default()
        await store.removeData(ofTypes: WKWebsiteDataStore.allWebsiteDataTypes(), modifiedSince: .distantPast)
        HTTPCookieStorage.shared.removeCookies(since: .distantPast)
        found = []
    }
}

struct WebView: UIViewRepresentable {
    let web: WKWebView
    func makeUIView(context: Context) -> WKWebView { web }
    func updateUIView(_ uiView: WKWebView, context: Context) {}
}

struct BrowserView: View {
    @Environment(AppModel.self) private var model
    @Environment(BrowserState.self) private var browser
    @State private var editing = ""
    @FocusState private var focused: Bool

    var body: some View {
        @Bindable var browser = browser
        VStack(spacing: 0) {
            HStack(spacing: 8) {
                Button { browser.web.goBack() } label: { Image(systemName: "chevron.backward") }.disabled(!browser.canBack)
                Button { browser.web.goForward() } label: { Image(systemName: "chevron.forward") }.disabled(!browser.canForward)
                TextField("Search or type an address", text: $editing)
                    .keyboardType(.webSearch).textInputAutocapitalization(.never).autocorrectionDisabled()
                    .focused($focused)
                    .onSubmit { browser.go(editing); focused = false }
                    .padding(.horizontal, 12).padding(.vertical, 8)
                    .background(Color(.secondarySystemBackground), in: Capsule())
                if browser.progress < 1 && browser.progress > 0 {
                    Button { browser.web.stopLoading() } label: { Image(systemName: "xmark") }
                } else {
                    Button { browser.web.reload() } label: { Image(systemName: "arrow.clockwise") }
                }
            }
            .padding(.horizontal).padding(.vertical, 8)
            ProgressView(value: browser.progress).opacity(browser.progress > 0 && browser.progress < 1 ? 1 : 0).frame(height: 2)
            WebView(web: browser.web)
                .overlay(alignment: .bottomTrailing) {
                    if !browser.found.isEmpty {
                        Button { browser.showFound = true } label: {
                            Label("\(browser.found.count) found", systemImage: "arrow.down.circle.fill").font(.headline)
                                .padding(.horizontal, 16).padding(.vertical, 12)
                        }
                        .buttonStyle(.borderedProminent).clipShape(Capsule()).padding()
                    }
                }
        }
        .onChange(of: browser.address) { _, a in if !focused { editing = a.replacingOccurrences(of: "https://", with: "") } }
        .onAppear {
            browser.onDownload = { input in model.addInput = input }
            if let u = model.browserURL { browser.web.load(URLRequest(url: u)); model.browserURL = nil } else { browser.startIfNeeded() }
        }
        .onChange(of: model.browserURL) { _, u in if let u { browser.web.load(URLRequest(url: u)); model.browserURL = nil } }
        .onChange(of: model.demoShowFound) { _, show in if show { browser.showFound = true; model.demoShowFound = false } }
        .sheet(isPresented: $browser.showFound) {
            NavigationStack {
                List {
                    Section {
                        ForEach(browser.found) { c in
                            HStack {
                                let cat = FileCategory(fileName: c.url.lastPathComponent)
                                Image(systemName: cat.symbol).foregroundStyle(cat.color).frame(width: 30)
                                VStack(alignment: .leading) {
                                    Text(c.url.lastPathComponent.removingPercentEncoding ?? c.url.lastPathComponent).lineLimit(1)
                                    Text("\(c.kind.rawValue) · \(c.label)").font(.caption).foregroundStyle(.secondary).lineLimit(1)
                                }
                                Spacer()
                                Button("Download") {
                                    browser.showFound = false
                                    Task { await browser.handOver(c.url, suggestedName: nil) }
                                }
                                .buttonStyle(.bordered)
                            }
                        }
                    } footer: { Text("Streaming and copy-protected video can't be saved.") }
                }
                .navigationTitle("Found on this page")
                .navigationBarTitleDisplayMode(.inline)
            }
            .presentationDetents([.medium, .large])
        }
    }
}

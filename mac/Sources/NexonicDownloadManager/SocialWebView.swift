import AppKit
import NexonicCore
import WebKit

/// Profile Batch for Instagram and Facebook: the profile page opens in a hidden web view, as in a visitor's browser, and
/// the video links it shows are read with the site's script. Without signing in the view is private (nothing kept) and
/// the site shows only the latest posts. After the optional sign-in (on the site's own page, in Nexonic's own web data on
/// this Mac) the page is scrolled to the end and every video is found.
@MainActor
enum SocialWebView {
    private static func mark(_ site: SocialSite) -> URL {
        AppPaths.support.appendingPathComponent("WebSessions", isDirectory: true).appendingPathComponent(site.name.lowercased() + ".signed-in")
    }

    static func signedIn(_ site: SocialSite) -> Bool { FileManager.default.fileExists(atPath: mark(site).path) }

    private static func view(signedIn: Bool, frame: NSRect) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = signedIn ? .default() : .nonPersistent()
        return WKWebView(frame: frame, configuration: config)
    }

    private static func run(_ view: WKWebView, _ script: String) async -> String? {
        await withCheckedContinuation { (done: CheckedContinuation<String?, Never>) in
            view.evaluateJavaScript(script) { result, _ in done.resume(returning: result as? String) }
        }
    }

    static func scan(_ url: String, site: SocialSite, owner: String, pages: [String], max: Int, found: @escaping (Int) -> Void,
                     stop: @escaping () -> Bool) async throws -> ProfileBatch.Scan {
        let signedIn = signedIn(site)
        let view = view(signedIn: signedIn, frame: NSRect(x: 0, y: 0, width: 1100, height: 1600))
        // An off-screen window, so the page lays out and runs its scripts as it would for a visitor.
        let window = NSWindow(contentRect: NSRect(x: -20000, y: -20000, width: 1100, height: 1600), styleMask: .borderless,
                              backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.contentView = view
        window.orderBack(nil)
        defer { view.stopLoading(); window.orderOut(nil); window.contentView = nil }
        var all: [String: BatchEntry] = [:], order: [String] = [], posts = 0
        for page in pages {
            guard let link = URL(string: page) else { continue }
            view.load(URLRequest(url: link))
            // Signed in: scroll until nothing new comes for a while. Signed out: read until the page stops changing.
            var idle = 0
            for i in 0..<(signedIn ? 2400 : 25) where idle < (signedIn ? 5 : 2) && all.count < max {
                try await Task.sleep(nanoseconds: signedIn ? 1_500_000_000 : 1_000_000_000)
                if stop() { throw CancellationError() }
                let now = InstagramPage.read(await run(view, site.script))
                posts = Swift.max(posts, now.posts)
                let grew = SocialPages.merge(&all, order: &order, now.videos, max: max)
                idle = grew || (all.isEmpty && i < 10) ? 0 : idle + 1
                if grew { found(all.count) }
                if signedIn { _ = await run(view, SocialPages.scroll) }
            }
        }
        if all.isEmpty {
            throw ProfileBatchError(posts > 0 ? InstagramPage.noVideos(posts)
                : "\(site.name) did not show any videos of this profile" + (signedIn ? "." : " to a visitor who is not signed in (it may be private or not exist)."))
        }
        let note = signedIn ? SocialPages.signedInNote(site, all.count) : SocialPages.offerSignIn(site)
        return SocialPages.scan(url: url, owner: owner, entries: order.compactMap { all[$0] }, note: note)
    }

    /// The site's own sign-in page in a window of Nexonic's: the password goes to the site, never to Nexonic. Returns true
    /// once the site has signed the user in (its session cookie is there); the window then closes by itself.
    static func signIn(_ site: SocialSite) async -> Bool {
        let view = view(signedIn: true, frame: NSRect(x: 0, y: 0, width: 520, height: 760))
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 760), styleMask: [.titled, .closable, .resizable],
                              backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.title = "Sign in to \(site.name) (Nexonic never sees your password)"
        window.contentView = view
        window.center()
        window.makeKeyAndOrderFront(nil)
        if let link = URL(string: site.loginURL) { view.load(URLRequest(url: link)) }
        defer { view.stopLoading(); window.orderOut(nil); window.contentView = nil }
        while window.isVisible {
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            let cookies = await withCheckedContinuation { (done: CheckedContinuation<[HTTPCookie], Never>) in
                view.configuration.websiteDataStore.httpCookieStore.getAllCookies { done.resume(returning: $0) }
            }
            if cookies.contains(where: { $0.name == site.sessionCookie && $0.domain.hasSuffix(site.domain) && !$0.value.isEmpty }) {
                try? FileManager.default.createDirectory(at: mark(site).deletingLastPathComponent(), withIntermediateDirectories: true)
                try? Data().write(to: mark(site))
                window.close()
                return true
            }
        }
        return false
    }

    /// Removes Nexonic's session for the site (the site's own account is untouched).
    static func signOut(_ site: SocialSite) async {
        try? FileManager.default.removeItem(at: mark(site))
        let store = WKWebsiteDataStore.default()
        let types = WKWebsiteDataStore.allWebsiteDataTypes()
        let records = await store.dataRecords(ofTypes: types)
        await store.removeData(ofTypes: types, for: records.filter { $0.displayName.contains(site.domain) })
    }
}

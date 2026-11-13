import AppKit
import NexonicCore
import WebKit

/// Profile Batch for Instagram and Facebook pages: the profile page opens in a hidden, private web view (nothing is kept)
/// and the video links it shows are read with the site's script. With the cookies the extension handed over (the user's
/// own sign-in in their browser) the page is scrolled in that hidden view, a screen every 1.5 seconds, until it shows no
/// more; without them it is what any visitor sees (only the latest posts).
@MainActor
enum SocialWebView {
    private static func run(_ view: WKWebView, _ script: String) async -> String? {
        await withCheckedContinuation { (done: CheckedContinuation<String?, Never>) in
            view.evaluateJavaScript(script) { result, _ in done.resume(returning: result as? String) }
        }
    }

    static func scan(_ url: String, site: SocialSite, owner: String, pages: [String], max: Int, cookies: String? = nil,
                     userAgent: String? = nil, seed: [BatchEntry] = [], found: @escaping (Int) -> Void,
                     stop: @escaping () -> Bool) async throws -> ProfileBatch.Scan {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .nonPersistent()
        let signedIn = !(GalleryDl.cookieJar(cookies).isEmpty)
        for (name, value) in GalleryDl.cookieJar(cookies) {
            guard let c = HTTPCookie(properties: [.domain: "." + site.domain, .path: "/", .name: name, .value: value, .secure: "TRUE"]) else { continue }
            await withCheckedContinuation { (done: CheckedContinuation<Void, Never>) in
                config.websiteDataStore.httpCookieStore.setCookie(c) { done.resume() }
            }
        }
        // A hidden page keeps running as if it were in front (otherwise macOS pauses it and it never loads more), and it
        // is told it is being looked at: the sites load more posts only for a page in view.
        if #available(macOS 14.0, *) { config.preferences.inactiveSchedulingPolicy = .none }
        config.userContentController.addUserScript(WKUserScript(source: """
            Object.defineProperty(document, 'visibilityState', { get: () => 'visible' });
            Object.defineProperty(document, 'hidden', { get: () => false });
            document.hasFocus = () => true;
            """, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        let view = WKWebView(frame: NSRect(x: 0, y: 0, width: 1100, height: 1600), configuration: config)
        // The browser the sign-in came from, as the site knows it.
        if let userAgent, !userAgent.isEmpty { view.customUserAgent = userAgent }
        // A see-through window on the screen that clicks pass through: macOS treats it as in view, so the page lays out,
        // draws and loads more as it is scrolled, while the user sees nothing (an off-screen window is paused).
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1100, height: 900)
        let window = NSWindow(contentRect: NSRect(x: screen.minX, y: screen.minY, width: 1100, height: Swift.min(1600, screen.height)),
                              styleMask: .borderless, backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.alphaValue = 0
        window.ignoresMouseEvents = true
        window.hasShadow = false
        window.level = .floating
        window.collectionBehavior = [.canJoinAllSpaces, .transient, .ignoresCycle]
        window.contentView = view
        window.orderFrontRegardless()
        defer { view.stopLoading(); window.orderOut(nil); window.contentView = nil }
        var all: [String: BatchEntry] = [:], order: [String] = [], posts = 0
        _ = SocialPages.merge(&all, order: &order, seed, max: max)
        for page in pages {
            guard let link = URL(string: page) else { continue }
            view.load(URLRequest(url: link))
            // Read until the page stops changing: signed in, scrolling down a screen at a time (a person's pace) until
            // nothing new comes for a while; a visitor gets no more by scrolling.
            var idle = 0, rounds = 0, height = "", first = "", wall = ""
            for i in 0..<(signedIn ? 1200 : 25) where idle < (signedIn ? 10 : 2) && all.count < max {
                rounds = i + 1
                try await Task.sleep(nanoseconds: signedIn ? 1_500_000_000 : 1_000_000_000)
                if stop() { throw CancellationError() }
                let now = InstagramPage.read(await run(view, site.script))
                posts = Swift.max(posts, now.posts)
                let grew = SocialPages.merge(&all, order: &order, now.videos, max: max)
                idle = grew || i < 6 ? 0 : idle + 1  // the page's first seconds are for loading
                if grew { found(all.count) }
                if signedIn {
                    // Down to the last video shown and then to the end, the way a person scrolling makes the page load more.
                    height = await run(view, """
                        (() => { const v = [...document.querySelectorAll('a[href*="/reel/"], a[href*="/videos/"]')].pop();
                        if (v) v.scrollIntoView({block: 'end'}); window.scrollTo(0, document.documentElement.scrollHeight);
                        return String(document.documentElement.scrollHeight); })()
                        """) ?? height
                    if first.isEmpty { first = height }
                }
            }
            wall = await run(view, "String(!!document.querySelector('form[action*=\"login\"], input[name=\"pass\"]'))") ?? ""
            GalleryDl.note(page, cursor: nil, found: all.count,
                           "[page] signed in: \(signedIn), rounds: \(rounds), page height: \(first) → \(height), sign-in form shown: \(wall), at: \(view.url?.absoluteString ?? "")")
        }
        if all.isEmpty {
            throw ProfileBatchError(posts > 0 ? InstagramPage.noVideos(posts)
                : "\(site.name) did not show any videos of this profile to a visitor who is not signed in (it may be private or not exist).")
        }
        return SocialPages.scan(url: url, owner: owner, entries: order.compactMap { all[$0] },
                                note: signedIn ? "Read with your browser's sign-in, scrolling the page in the background until it showed no more."
                                    : "\(site.name) shows visitors who are not signed in only the latest posts; Nexonic took the videos among them.")
    }
}

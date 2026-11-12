import AppKit
import NexonicCore
import WebKit

/// Profile Batch for Instagram and Facebook without the browser extension: the profile page opens in a hidden, private
/// web view (nothing kept, no sign-in: what any visitor sees) and the video links it shows are read with the site's script.
/// The sites show visitors only their latest posts; the rest come through the user's own signed-in browser (SocialPages.More).
@MainActor
enum SocialWebView {
    private static func run(_ view: WKWebView, _ script: String) async -> String? {
        await withCheckedContinuation { (done: CheckedContinuation<String?, Never>) in
            view.evaluateJavaScript(script) { result, _ in done.resume(returning: result as? String) }
        }
    }

    static func scan(_ url: String, site: SocialSite, owner: String, pages: [String], max: Int, found: @escaping (Int) -> Void,
                     stop: @escaping () -> Bool) async throws -> ProfileBatch.Scan {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .nonPersistent()
        let view = WKWebView(frame: NSRect(x: 0, y: 0, width: 1100, height: 1600), configuration: config)
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
            // Read until the page stops changing (visitors get no more by scrolling).
            var idle = 0
            for i in 0..<25 where idle < 2 && all.count < max {
                try await Task.sleep(nanoseconds: 1_000_000_000)
                if stop() { throw CancellationError() }
                let now = InstagramPage.read(await run(view, site.script))
                posts = Swift.max(posts, now.posts)
                let grew = SocialPages.merge(&all, order: &order, now.videos, max: max)
                idle = grew || (all.isEmpty && i < 10) ? 0 : idle + 1
                if grew { found(all.count) }
            }
        }
        if all.isEmpty {
            throw ProfileBatchError(posts > 0 ? InstagramPage.noVideos(posts)
                : "\(site.name) did not show any videos of this profile to a visitor who is not signed in (it may be private or not exist).")
        }
        return SocialPages.scan(url: url, owner: owner, entries: order.compactMap { all[$0] },
                                note: "\(site.name) shows visitors who are not signed in only the latest posts; Nexonic took the videos among them.")
    }
}

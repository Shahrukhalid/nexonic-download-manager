import AppKit
import NexonicCore
import WebKit

/// Profile Batch for Instagram: the profile page opens in a hidden, private web view (no cookies, no sign-in: what any
/// visitor sees), and the video links the page shows are read with InstagramPage.script.
@MainActor
enum InstagramWebView {
    static func scan(_ url: String, user: String, stop: @escaping () -> Bool) async throws -> ProfileBatch.Scan {
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
        guard let page = URL(string: "https://www.instagram.com/\(user)/") else { throw ProfileBatchError(InstagramPage.nothing) }
        view.load(URLRequest(url: page))
        var best: (posts: Int, videos: [BatchEntry]) = (0, [])
        var same = 0
        for _ in 0..<25 where same < 2 {
            try await Task.sleep(nanoseconds: 1_000_000_000)
            if stop() { throw CancellationError() }
            let json = await withCheckedContinuation { (done: CheckedContinuation<String?, Never>) in
                view.evaluateJavaScript(InstagramPage.script) { result, _ in done.resume(returning: result as? String) }
            }
            let now = InstagramPage.read(json)
            same = now.posts > 0 && now.posts == best.posts && now.videos.count == best.videos.count ? same + 1 : 0
            if now.posts >= best.posts { best = now }
        }
        if best.posts == 0 { throw ProfileBatchError(InstagramPage.nothing) }
        if best.videos.isEmpty { throw ProfileBatchError(InstagramPage.noVideos(best.posts)) }
        return InstagramPage.scan(url: url, user: user, posts: best.posts, videos: best.videos)
    }
}

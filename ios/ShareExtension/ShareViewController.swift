import NexonicKit
import SwiftUI
import UIKit
import UniformTypeIdentifiers

/// Share → Download with Nexonic, from Safari, Chrome and any app that shares a link or text with links.
/// Works as soon as the app is installed (no Shortcut needed). Downloads started here keep running in an iOS
/// background session after this window closes; the app shows them and puts finished files into Files.
final class ShareViewController: UIViewController {
    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .systemBackground
        Task { @MainActor in
            let urls = await sharedLinks()
            guard !urls.isEmpty else { showNoLink(); return }
            let window = DownloadWindow(input: DownloadInput(urls: urls), sessionID: Downloader.shareSessionID) { [weak self] message in
                self?.finish(message)
            }
            let host = UIHostingController(rootView: window)
            addChild(host)
            host.view.frame = view.bounds
            host.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
            view.addSubview(host.view)
            host.didMove(toParent: self)
        }
    }

    /// Web links from the shared items: URLs first, then links inside shared text.
    private func sharedLinks() async -> [URL] {
        let items = (extensionContext?.inputItems as? [NSExtensionItem]) ?? []
        var urls: [URL] = []
        for provider in items.flatMap({ $0.attachments ?? [] }) {
            if provider.hasItemConformingToTypeIdentifier(UTType.url.identifier),
               let item = try? await provider.loadItem(forTypeIdentifier: UTType.url.identifier) {
                var found: URL?
                if let u = item as? URL { found = u }
                else if let s = item as? String { found = URL(string: s) }
                else if let d = item as? Data { found = URL(dataRepresentation: d, relativeTo: nil) }
                if let found, let web = Net.webURL(found.absoluteString) { urls.append(Links.unwrap(web)) }
            } else if provider.hasItemConformingToTypeIdentifier(UTType.plainText.identifier),
                      let text = try? await provider.loadItem(forTypeIdentifier: UTType.plainText.identifier) as? String {
                urls += Links.split(text).compactMap { Net.webURL($0) }.map { Links.unwrap($0) }
            }
        }
        var seen = Set<URL>()
        return urls.filter { seen.insert($0).inserted }
    }

    private func showNoLink() {
        let alert = UIAlertController(title: "No web link", message: "Share a web page or a link to a file to download it with Nexonic.",
                                      preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "OK", style: .default) { [weak self] _ in self?.finish(nil) })
        present(alert, animated: true)
    }

    private func finish(_ message: String?) {
        extensionContext?.completeRequest(returningItems: nil)
    }
}

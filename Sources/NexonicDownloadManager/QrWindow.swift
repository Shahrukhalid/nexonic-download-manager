import AppKit
import CoreImage
import NexonicCore
import SwiftUI

/// Shows the selected downloads as a QR code: the phone's camera opens them in Nexonic on Android or iPhone.
enum QrWindow {
    static func show(_ urls: [URL]) {
        let (link, count) = ShareLinks.link(urls)
        guard count > 0, let image = image(link) else { return }
        let view = QrView(image: image, count: count, total: urls.count)
        let w = NSWindow(contentViewController: NSHostingController(rootView: view))
        w.title = "Download on Your Phone"
        w.styleMask = [.titled, .closable]
        w.isReleasedWhenClosed = false
        w.center()
        w.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    static func image(_ text: String) -> NSImage? {
        guard let filter = CIFilter(name: "CIQRCodeGenerator") else { return nil }
        filter.setValue(Data(text.utf8), forKey: "inputMessage")
        filter.setValue("M", forKey: "inputCorrectionLevel")
        guard let output = filter.outputImage?.transformed(by: CGAffineTransform(scaleX: 8, y: 8)) else { return nil }
        let rep = NSCIImageRep(ciImage: output)
        let image = NSImage(size: rep.size)
        image.addRepresentation(rep)
        return image
    }
}

private struct QrView: View {
    let image: NSImage
    let count: Int
    let total: Int

    var body: some View {
        VStack(spacing: 12) {
            Image(nsImage: image)
                .interpolation(.none)
                .resizable()
                .frame(width: 300, height: 300)
                .padding(16)
                .background(Color.white)
            Text(count == 1 ? "Scan with your phone's camera to download this file with the Nexonic app on your phone."
                            : "Scan with your phone's camera to download these \(count) files with the Nexonic app on your phone.")
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
            if count < total {
                Text("Only the first \(count) of \(total) links fit in one code.").font(.caption).foregroundStyle(.secondary)
            }
            Text("Only the links are shared: no cookies or passwords. Downloads that need you to be signed in may not work on the phone.")
                .font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(20)
        .frame(width: 380)
    }
}

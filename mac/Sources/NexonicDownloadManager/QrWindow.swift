import AppKit
import CoreImage
import NexonicCore
import SwiftUI

/// Shows the selected downloads as a QR code: the phone's camera opens them in Nexonic on Android or iPhone.
enum QrWindow {
    static func show(_ urls: [URL]) {
        // As many codes as the links need: Previous and Next go through them.
        let codes = ShareLinks.codes(urls).compactMap { c in image(c.link).map { (image: $0, count: c.count) } }
        guard !codes.isEmpty else { return }
        let view = QrView(codes: codes, total: urls.count, isPrivate: urls.contains(where: ShareLinks.looksPrivate))
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
    let codes: [(image: NSImage, count: Int)]
    let total: Int
    let isPrivate: Bool
    @State private var index = 0

    var body: some View {
        let count = codes[index].count
        let shown = codes.reduce(0) { $0 + $1.count }
        VStack(spacing: 12) {
            Image(nsImage: codes[index].image)
                .interpolation(.none)
                .resizable()
                .frame(width: 300, height: 300)
                .padding(16)
                .background(Color.white)
            if codes.count > 1 {
                HStack {
                    Button("Previous") { index = (index + codes.count - 1) % codes.count }
                    Text("Code \(index + 1) of \(codes.count)").monospacedDigit()
                    Button("Next") { index = (index + 1) % codes.count }
                }
            }
            Text(count == 1 ? "Scan with your phone's camera to download this file with the Nexonic app on your phone."
                            : "Scan with your phone's camera to download these \(count) files with the Nexonic app on your phone.")
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
            if shown < total {
                Text("\(total - shown) links are too long for a code and were left out.").font(.caption).foregroundStyle(.secondary)
            }
            if isPrivate {
                Text("A link seems to include a private access key (for example a token or signature). Anyone who scans the code can use it until it expires, so share it only with your own phone.")
                    .font(.caption).foregroundStyle(.orange).multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text("Only the links are shared: no cookies or passwords. Downloads that need you to be signed in may not work on the phone.")
                .font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(20)
        .frame(width: 380)
    }
}

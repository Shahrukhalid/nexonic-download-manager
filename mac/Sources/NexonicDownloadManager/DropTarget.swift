import AppKit
import NexonicCore

/// Settings → Extras → Drop target: a small round window that stays above other windows. Drop a link, a selection of
/// text with links or a .torrent file on it and the Download File Info window opens, as for a browser download. Drag it
/// anywhere; double-click it to show the downloads.
@MainActor
enum DropTarget {
    private static var panel: NSPanel?
    private static let frameKey = "dropTargetFrame"

    static func update(_ on: Bool) {
        if !on { panel?.close(); panel = nil; return }
        guard panel == nil else { return }
        let p = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 64, height: 64), styleMask: [.borderless, .nonactivatingPanel],
                        backing: .buffered, defer: false)
        p.level = .floating
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = true
        p.isMovableByWindowBackground = true
        p.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary]
        p.hidesOnDeactivate = false
        p.contentView = DropView(frame: NSRect(x: 0, y: 0, width: 64, height: 64))
        if let saved = UserDefaults.standard.string(forKey: frameKey) { p.setFrameOrigin(NSPointFromString(saved)) }
        else if let s = NSScreen.main?.visibleFrame { p.setFrameOrigin(NSPoint(x: s.maxX - 96, y: s.maxY - 140)) }
        NotificationCenter.default.addObserver(forName: NSWindow.didMoveNotification, object: p, queue: .main) { n in
            if let w = n.object as? NSWindow { UserDefaults.standard.set(NSStringFromPoint(w.frame.origin), forKey: frameKey) }
        }
        p.orderFrontRegardless()
        panel = p
    }

    /// Links in what was dropped: each opens the Download File Info window (a .torrent file opens Add Torrent).
    static func take(_ board: NSPasteboard) -> Bool {
        var links: [String] = []
        if let urls = board.readObjects(forClasses: [NSURL.self]) as? [URL] {
            for u in urls {
                if u.isFileURL { if TorrentInbox.shared.accept(u) { DownloadManager.showMainWindow?() } }
                else { links.append(u.absoluteString) }
            }
        }
        if links.isEmpty, let text = board.string(forType: .string) { links = LinkGrabber.find(text) }
        for l in links.prefix(50) {
            var job = DownloadJob(url: l)
            job.fromBrowser = true
            DownloadInfoWindow.show(job, manager: DownloadManager.shared)
        }
        return !links.isEmpty
    }

    private final class DropView: NSView {
        private var hot = false { didSet { needsDisplay = true } }

        override init(frame: NSRect) {
            super.init(frame: frame)
            registerForDraggedTypes([.URL, .fileURL, .string])
            toolTip = String(localized: "Drop links here to download them")
        }
        required init?(coder: NSCoder) { nil }

        override func draw(_ dirtyRect: NSRect) {
            let circle = NSBezierPath(ovalIn: bounds.insetBy(dx: 4, dy: 4))
            (hot ? NSColor.controlAccentColor : NSColor.windowBackgroundColor.withAlphaComponent(0.92)).setFill()
            circle.fill()
            NSColor.separatorColor.setStroke(); circle.lineWidth = 1; circle.stroke()
            let config = NSImage.SymbolConfiguration(pointSize: 26, weight: .regular)
                .applying(.init(paletteColors: [hot ? .white : .controlAccentColor]))
            if let icon = NSImage(systemSymbolName: "arrow.down", accessibilityDescription: nil)?.withSymbolConfiguration(config) {
                let size = icon.size
                icon.draw(in: NSRect(x: (bounds.width - size.width) / 2, y: (bounds.height - size.height) / 2, width: size.width, height: size.height))
            }
        }

        override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation { hot = true; return .copy }
        override func draggingExited(_ sender: NSDraggingInfo?) { hot = false }
        override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
            hot = false
            return DropTarget.take(sender.draggingPasteboard)
        }
        override func mouseDown(with event: NSEvent) {
            if event.clickCount == 2 { DownloadManager.showMainWindow?() } else { window?.performDrag(with: event) }
        }
        override func rightMouseDown(with event: NSEvent) {
            let menu = NSMenu()
            menu.addItem(withTitle: String(localized: "Hide Drop Target"), action: #selector(hide), keyEquivalent: "").target = self
            NSMenu.popUpContextMenu(menu, with: event, for: self)
        }
        @objc private func hide() { DownloadManager.shared.settings.finish.dropTarget = false }
    }
}

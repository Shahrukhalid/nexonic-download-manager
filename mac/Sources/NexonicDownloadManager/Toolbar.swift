import AppKit
import NexonicCore
import SwiftUI

/// Everything the toolbar and the menu bar can do in the main window. Menus post an action; the window performs it,
/// so a menu command and its toolbar button always behave the same.
enum MainAction: Equatable {
    case addURL, addTorrent, batch, grabber, scheduler
    case resume, stop, stopAll, resumeAll, delete, deleteCompleted, selectAll
    case startQueue, stopQueue, properties, showInFinder, open
}

@MainActor
final class MainActions: ObservableObject {
    static let shared = MainActions()
    @Published var pending: MainAction?
    func send(_ action: MainAction) {
        DownloadManager.showMainWindow?()
        pending = action
    }
}

/// One toolbar button in the style of IDM: a large coloured icon with its name underneath.
struct ToolbarTile: View {
    let title: String
    let symbol: String
    let color: Color
    var enabled = true
    var help = ""
    let action: () -> Void
    @AppStorage("toolbarText") private var showText = true
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            VStack(spacing: 3) {
                Image(systemName: symbol)
                    .font(.system(size: 22, weight: .regular))
                    .symbolRenderingMode(.hierarchical)
                    .foregroundStyle(enabled ? color : Color.secondary.opacity(0.5))
                    .frame(height: 26)
                if showText {
                    Text(title).font(.system(size: 11)).lineLimit(1).fixedSize()
                        .foregroundStyle(enabled ? Color.primary : Color.secondary.opacity(0.6))
                }
            }
            .padding(.horizontal, 6)
            .padding(.vertical, 5)
            .frame(minWidth: 58)
            .background(RoundedRectangle(cornerRadius: 7).fill(hovering && enabled ? Color.primary.opacity(0.08) : .clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
        .onHover { hovering = $0 }
        .help(help.isEmpty ? title : help)
        .accessibilityLabel(title)
    }
}

/// The main toolbar: the same commands as IDM's toolbar, in its order, with coloured icons and names.
struct MainToolbar: View {
    let hasSelection: Bool
    let hasItems: Bool
    let allSelected: Bool
    let queueName: String
    let perform: (MainAction) -> Void

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 2) {
                ToolbarTile(title: "Add URL", symbol: "plus.circle.fill", color: .green, help: "Add one or more links (⌘N)") { perform(.addURL) }
                ToolbarTile(title: "Torrent", symbol: "arrow.down.circle.dotted", color: .indigo, help: "Add a .torrent file, torrent link or magnet") { perform(.addTorrent) }
                separator
                ToolbarTile(title: "Resume", symbol: "play.circle.fill", color: .blue, enabled: hasSelection, help: "Resume the selected downloads") { perform(.resume) }
                ToolbarTile(title: "Stop", symbol: "pause.circle.fill", color: .orange, enabled: hasSelection, help: "Stop the selected downloads") { perform(.stop) }
                ToolbarTile(title: "Stop All", symbol: "stop.circle.fill", color: .red, help: "Stop every download") { perform(.stopAll) }
                separator
                ToolbarTile(title: allSelected ? "Select None" : "Select All", symbol: "checklist", color: .teal, enabled: hasItems) { perform(.selectAll) }
                ToolbarTile(title: "Delete", symbol: "trash.circle.fill", color: .red, enabled: hasSelection, help: "Remove the selected downloads from the list") { perform(.delete) }
                ToolbarTile(title: "Delete Completed", symbol: "checkmark.circle.fill", color: .green, help: "Remove finished downloads from the list") { perform(.deleteCompleted) }
                separator
                ToolbarTile(title: "Scheduler", symbol: "calendar.badge.clock", color: .purple, help: "Queues and timetables") { perform(.scheduler) }
                ToolbarTile(title: "Start Queue", symbol: "play.square.stack.fill", color: .mint, help: "Start \(queueName)") { perform(.startQueue) }
                ToolbarTile(title: "Stop Queue", symbol: "square.stack.fill", color: .pink, help: "Stop \(queueName)") { perform(.stopQueue) }
                separator
                ToolbarTile(title: "Grabber", symbol: "globe", color: .blue, help: "Download files from a website section") { perform(.grabber) }
                ToolbarTile(title: "Batch", symbol: "number.square.fill", color: .brown, help: "Download a numbered series of files") { perform(.batch) }
                if #available(macOS 14, *) {
                    SettingsLink {
                        OptionsTileLabel()
                    }
                    .buttonStyle(.plain)
                    .help("Options (⌘,)")
                } else {
                    ToolbarTile(title: "Options", symbol: "gearshape.fill", color: .gray, help: "Options (⌘,)") {
                        NSApp.sendAction(Selector(("showSettingsWindow:")), to: nil, from: nil)
                    }
                }
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
        }
        .background(.bar)
    }

    private var separator: some View { Divider().frame(height: 34).padding(.horizontal, 4) }
}

/// The Options tile drawn inside a SettingsLink (macOS 14+ opens Settings only through it).
private struct OptionsTileLabel: View {
    @AppStorage("toolbarText") private var showText = true
    var body: some View {
        VStack(spacing: 3) {
            Image(systemName: "gearshape.fill").font(.system(size: 22)).symbolRenderingMode(.hierarchical)
                .foregroundStyle(Color.gray).frame(height: 26)
            if showText { Text("Options").font(.system(size: 11)).fixedSize() }
        }
        .padding(.horizontal, 6).padding(.vertical, 5).frame(minWidth: 58).contentShape(Rectangle())
    }
}

/// Menus in the menu bar like IDM's Tasks, Downloads and View menus.
struct MainMenus: Commands {
    @AppStorage("toolbarText") private var showText = true
    @AppStorage("showToolbar") private var showToolbar = true

    var body: some Commands {
        CommandGroup(replacing: .newItem) {
            Button("Add URL…") { MainActions.shared.send(.addURL) }.keyboardShortcut("n")
            Button("Add Torrent…") { MainActions.shared.send(.addTorrent) }.keyboardShortcut("t", modifiers: [.command, .shift])
            Button("Add Batch Download…") { MainActions.shared.send(.batch) }.keyboardShortcut("b", modifiers: [.command, .shift])
            Button("Site Grabber…") { MainActions.shared.send(.grabber) }.keyboardShortcut("g", modifiers: [.command, .shift])
        }
        CommandMenu("Downloads") {
            Button("Resume") { MainActions.shared.send(.resume) }.keyboardShortcut("r")
            Button("Stop") { MainActions.shared.send(.stop) }.keyboardShortcut(".")
            Divider()
            Button("Resume All") { MainActions.shared.send(.resumeAll) }.keyboardShortcut("r", modifiers: [.command, .shift])
            Button("Stop All") { MainActions.shared.send(.stopAll) }.keyboardShortcut("p", modifiers: [.command, .shift])
            Divider()
            Button("Open") { MainActions.shared.send(.open) }.keyboardShortcut("o")
            Button("Show in Finder") { MainActions.shared.send(.showInFinder) }.keyboardShortcut("r", modifiers: [.command, .option])
            Button("Properties…") { MainActions.shared.send(.properties) }.keyboardShortcut("i")
            Divider()
            Button("Delete…") { MainActions.shared.send(.delete) }
            Button("Delete Completed…") { MainActions.shared.send(.deleteCompleted) }
            Divider()
            Button("Scheduler…") { MainActions.shared.send(.scheduler) }
            Button("Start Queue") { MainActions.shared.send(.startQueue) }
            Button("Stop Queue") { MainActions.shared.send(.stopQueue) }
        }
        CommandGroup(after: .toolbar) {
            Toggle("Show Toolbar Buttons", isOn: $showToolbar)
            Toggle("Show Button Names", isOn: $showText)
        }
    }
}

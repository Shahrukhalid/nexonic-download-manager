import NexonicCore
import SwiftUI

/// File → RSS Feeds: feeds whose new items are downloaded when their title matches the rules.
struct FeedsSheet: View {
    @EnvironmentObject var manager: DownloadManager
    @Environment(\.dismiss) private var dismiss
    @State private var selection: UUID?
    @State private var editing: FeedSubscription?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("RSS Feeds").font(.title2.bold())
            Text("New items of these feeds are downloaded when their title matches the rule: files and videos, and torrents when torrents are on. A rule lists words that must all be in the title, or a /regular expression/.")
                .foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            List(selection: $selection) {
                ForEach(manager.feeds) { f in
                    VStack(alignment: .leading, spacing: 2) {
                        Text((f.enabled ? "" : "(off) ") + (f.name.isEmpty ? f.url : f.name)).font(.body.weight(.medium))
                        Text(f.include.isEmpty ? String(localized: "every item") : f.include).font(.caption).foregroundStyle(.secondary)
                        Text(f.lastChecked.map { "\($0.formatted(date: .abbreviated, time: .shortened)): \(f.lastResult)" } ?? String(localized: "not checked yet"))
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    .tag(f.id)
                }
            }
            .frame(minHeight: 200)
            HStack {
                Button("Add Feed…") { editing = FeedSubscription() }
                Button("Edit…") { editing = manager.feeds.first { $0.id == selection } }.disabled(selection == nil)
                Button("Remove") {
                    manager.feeds.removeAll { $0.id == selection }
                    manager.saveFeeds()
                    selection = nil
                }.disabled(selection == nil)
                Button("Check Now") { manager.checkFeeds(all: true) }.disabled(manager.feeds.isEmpty)
                Spacer()
                Button("Close") { dismiss() }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 640)
        .sheet(item: $editing) { feed in
            FeedEditor(feed: feed) { saved in
                if let i = manager.feeds.firstIndex(where: { $0.id == saved.id }) { manager.feeds[i] = saved } else { manager.feeds.append(saved) }
                manager.saveFeeds()
            }
        }
    }
}

private struct FeedEditor: View {
    @Environment(\.dismiss) private var dismiss
    @State var feed: FeedSubscription
    let onSave: (FeedSubscription) -> Void
    @State private var problem = ""
    private let original: String

    init(feed: FeedSubscription, onSave: @escaping (FeedSubscription) -> Void) {
        _feed = State(initialValue: feed)
        self.onSave = onSave
        original = feed.url
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(original.isEmpty ? "Add Feed" : "Edit Feed").font(.title2.bold())
            Form {
                TextField("Feed address (RSS or Atom)", text: $feed.url)
                TextField("Name (optional)", text: $feed.name)
                TextField("Download items whose title has all of these words (empty = every item)", text: $feed.include)
                TextField("Skip items whose title has any of these words", text: $feed.exclude)
                TextField("Preferred qualities, best first (e.g. 1080p 720p)", text: $feed.quality)
                Stepper("Smallest file: \(feed.minSizeMB) MB", value: $feed.minSizeMB, in: 0...1_000_000, step: 50)
                Stepper("Largest file: \(feed.maxSizeMB) MB (0 = any)", value: $feed.maxSizeMB, in: 0...1_000_000, step: 50)
                Toggle("Only one download per episode", isOn: $feed.oneOfEachEpisode)
                TextField("Folder (empty = the usual folder)", text: $feed.folder)
                Stepper("Check every \(feed.intervalMinutes) minutes", value: $feed.intervalMinutes, in: 15...1440, step: 15)
                Toggle("Only download items that appear after the feed is added", isOn: $feed.skipExisting)
                Toggle("Check this feed", isOn: $feed.enabled)
            }
            .formStyle(.grouped)
            if !problem.isEmpty { Text(LocalizedStringKey(problem)).foregroundStyle(.red).font(.caption) }
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Save") { save() }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 560)
    }

    private func save() {
        let u = feed.url.trimmingCharacters(in: .whitespaces)
        guard let url = URL(string: u), ["http", "https"].contains(url.scheme?.lowercased() ?? "") else {
            problem = "The feed address must start with http:// or https://"; return
        }
        if let p = Feeds.ruleProblem(feed.include) ?? Feeds.ruleProblem(feed.exclude) { problem = p; return }
        if u != original { feed.seen = []; feed.lastChecked = nil }
        feed.url = u
        feed.folder = feed.folder.trimmingCharacters(in: .whitespaces)
        onSave(feed)
        dismiss()
    }
}

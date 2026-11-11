import NexonicCore
import SwiftUI

/// Settings → General: the user's own categories, checked in this order before the built-in types. Each has an icon,
/// a name, file types, optional sites and a folder; they can be added, moved and removed here, or edited as text.
struct CategoriesEditor: View {
    @EnvironmentObject var manager: DownloadManager
    @State private var rows: [CustomCategory] = []
    @State private var asText = false
    @State private var text = ""
    @State private var problem: String?
    @State private var saved = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("Your categories").font(.headline)
                Spacer()
                Picker("", selection: $asText) {
                    Text("List").tag(false)
                    Text("Text").tag(true)
                }
                .pickerStyle(.segmented).frame(width: 120).labelsHidden()
                .onChange(of: asText) { text in
                    if text { self.text = CustomCategory.toLines(rows) } else { fromText() }
                }
            }
            if asText {
                TextEditor(text: $text)
                    .font(.system(.body, design: .monospaced))
                    .frame(minHeight: 80)
                    .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.secondary.opacity(0.4)))
                    .accessibilityLabel("Your categories, one per line")
                Text("One per line: [icon] Name: types @ sites > folder. Example: 📚 Comics: cbz cbr > Comics")
                    .font(.caption).foregroundStyle(.secondary)
            } else {
                if rows.isEmpty {
                    Text("No categories yet. Add one to sort files of your own types into their own folder.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                ForEach(rows.indices, id: \.self) { i in row(i) }
                Button { rows.append(CustomCategory()); saved = false } label: { Label("Add Category", systemImage: "plus") }
                Text("Sites limit a category to files from those addresses (example.com includes its subdomains; * matches anything, as in github.com/*/releases). The folder can be a full path or a folder inside the download folder; empty = the name.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            if let problem { Text(problem).font(.caption).foregroundStyle(.red) }
            HStack {
                Button("Save Categories") { save() }
                if saved { Text("Saved").font(.caption).foregroundStyle(.secondary) }
            }
        }
        .onAppear { rows = manager.settings.customCategories }
    }

    @ViewBuilder private func row(_ i: Int) -> some View {
        HStack(spacing: 6) {
            TextField("Icon", text: binding(i, \.icon)).frame(width: 44).accessibilityLabel("Icon")
            TextField("Name", text: binding(i, \.name)).frame(minWidth: 90)
            TextField("Types (cbz cbr)", text: binding(i, \.extensions)).frame(minWidth: 110)
            TextField("Sites (optional)", text: binding(i, \.sites)).frame(minWidth: 100)
            TextField("Folder", text: binding(i, \.folder)).frame(minWidth: 90)
            Button { if let u = chooseFolder(manager.settings.folder) { rows[i].folder = u.path; saved = false } } label: { Image(systemName: "folder") }
                .help("Choose a folder").accessibilityLabel("Choose a folder")
            Button { move(i, -1) } label: { Image(systemName: "arrow.up") }.disabled(i == 0).help("Checked earlier").accessibilityLabel("Move up")
            Button { move(i, 1) } label: { Image(systemName: "arrow.down") }.disabled(i == rows.count - 1).help("Checked later").accessibilityLabel("Move down")
            Button(role: .destructive) { rows.remove(at: i); saved = false } label: { Image(systemName: "trash") }.accessibilityLabel("Remove")
        }
        .textFieldStyle(.roundedBorder)
    }

    private func binding(_ i: Int, _ key: WritableKeyPath<CustomCategory, String>) -> Binding<String> {
        Binding(get: { i < rows.count ? rows[i][keyPath: key] : "" }, set: { if i < rows.count { rows[i][keyPath: key] = $0; saved = false } })
    }

    private func move(_ i: Int, _ by: Int) {
        let j = i + by
        guard rows.indices.contains(j) else { return }
        rows.swapAt(i, j)
        saved = false
    }

    private func fromText() {
        let result = CustomCategory.parse(text)
        problem = result.problem
        if result.problem == nil { rows = result.list }
    }

    /// Checked the same way as the text form, so both always agree.
    private func save() {
        let result = CustomCategory.parse(asText ? text : CustomCategory.toLines(rows.filter { !$0.name.isEmpty || !$0.extensions.isEmpty }))
        problem = result.problem
        guard result.problem == nil else { return }
        manager.settings.customCategories = result.list
        rows = result.list
        text = CustomCategory.toLines(result.list)
        saved = true
    }
}

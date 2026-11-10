import NexonicCore
import SwiftUI

/// Settings → General: the user's own categories, one per line, in the order they are checked.
struct CategoriesEditor: View {
    @EnvironmentObject var manager: DownloadManager
    @State private var text = ""
    @State private var problem: String?
    @State private var saved = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Your categories").font(.headline)
            TextEditor(text: $text)
                .font(.system(.body, design: .monospaced))
                .frame(minHeight: 80)
                .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.secondary.opacity(0.4)))
                .accessibilityLabel("Your categories, one per line")
                .onChange(of: text) { _ in saved = false }
            if let problem {
                Text(problem).font(.caption).foregroundStyle(.red)
            } else {
                Text("One per line, in the order they are checked: [icon] Name: types @ sites > folder. The icon, the sites and the folder can be left out. Sites limit a category to files from those addresses (example.com includes its subdomains; * matches anything, as in github.com/*/releases). The folder can be a full path or a folder inside the download folder. Example: 📚 Comics: cbz cbr > Comics")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            HStack {
                Button("Save Categories") {
                    let result = CustomCategory.parse(text)
                    problem = result.problem
                    guard result.problem == nil else { return }
                    manager.settings.customCategories = result.list
                    text = CustomCategory.toLines(result.list)
                    saved = true
                }
                if saved { Text("Saved").font(.caption).foregroundStyle(.secondary) }
            }
        }
        .onAppear { text = CustomCategory.toLines(manager.settings.customCategories) }
    }
}

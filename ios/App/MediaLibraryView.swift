import NexonicKit
import QuickLook
import QuickLookThumbnailing
import SwiftUI

/// Finished videos and music with preview pictures: tap to play, share or show in Files.
struct MediaLibraryView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var kind: FileCategory?
    @State private var query = ""
    @State private var preview: URL?

    private var items: [(job: DownloadJob, file: URL)] {
        model.jobs
            .filter { $0.status == .complete && ($0.category == .video || $0.category == .music) }
            .filter { kind == nil || $0.category == kind }
            .filter { query.isEmpty || $0.fileName.localizedCaseInsensitiveContains(query) }
            .compactMap { job in Library.fileURL(job).flatMap { FileManager.default.fileExists(atPath: $0.path) ? (job: job, file: $0) : nil } }
            .sorted { ($0.job.finishedAt ?? $0.job.addedAt) > ($1.job.finishedAt ?? $1.job.addedAt) }
    }

    var body: some View {
        let list = items
        NavigationStack {
            ScrollView {
                Picker("Show", selection: $kind) {
                    Text("Videos and Music").tag(FileCategory?.none)
                    Text("Videos").tag(Optional(FileCategory.video))
                    Text("Music").tag(Optional(FileCategory.music))
                }
                .pickerStyle(.segmented)
                .padding(.horizontal)
                if list.isEmpty {
                    ContentUnavailableView("No videos or music yet", systemImage: "play.rectangle.on.rectangle",
                                           description: Text("Finished videos and music appear here."))
                        .padding(.top, 40)
                } else {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 160), spacing: 12)], spacing: 14) {
                        ForEach(list, id: \.job.id) { item in
                            VStack(alignment: .leading, spacing: 4) {
                                MediaThumbnail(file: item.file, isVideo: item.job.category == .video)
                                    .aspectRatio(16 / 9, contentMode: .fit)
                                    .clipShape(RoundedRectangle(cornerRadius: 8))
                                Text(item.job.fileName).font(.subheadline.weight(.semibold)).lineLimit(1)
                                Text("\(item.job.size.map { ByteCountFormatter.string(fromByteCount: $0, countStyle: .file) } ?? "")   \((item.job.finishedAt ?? item.job.addedAt).formatted(date: .abbreviated, time: .omitted))")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                            .contentShape(Rectangle())
                            .onTapGesture { preview = item.file }
                            .contextMenu {
                                Button("Play", systemImage: "play") { preview = item.file }
                                ShareLink(item: item.file) { Label("Share", systemImage: "square.and.arrow.up") }
                                Button("Show in Files", systemImage: "folder") { FilesApp.show(item.file) }
                            }
                        }
                    }
                    .padding()
                }
            }
            .navigationTitle("Media Library")
            .navigationBarTitleDisplayMode(.inline)
            .searchable(text: $query, prompt: "Search by name")
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } } }
            .quickLookPreview($preview, in: list.map(\.file))
        }
    }
}

/// A file's preview picture from Quick Look (videos), or a symbol (music and files without one).
private struct MediaThumbnail: View {
    let file: URL
    let isVideo: Bool
    @State private var image: UIImage?

    var body: some View {
        ZStack {
            Color.secondary.opacity(0.12)
            if let image {
                Image(uiImage: image).resizable().scaledToFill()
            } else {
                Image(systemName: isVideo ? "film" : "music.note").font(.system(size: 30)).foregroundStyle(.secondary)
            }
        }
        .task(id: file) {
            let request = QLThumbnailGenerator.Request(fileAt: file, size: CGSize(width: 320, height: 180),
                                                       scale: 2, representationTypes: .thumbnail)
            if let rep = try? await QLThumbnailGenerator.shared.generateBestRepresentation(for: request) { image = rep.uiImage }
        }
    }
}

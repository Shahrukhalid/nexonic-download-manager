import Foundation

public enum FileCategory: String, Codable, CaseIterable, Sendable {
    case compressed = "Compressed", documents = "Documents", music = "Music", video = "Video",
         programs = "Programs", images = "Images", other = "Other"

    private static let map: [FileCategory: Set<String>] = [
        .compressed: ["zip", "rar", "7z", "gz", "tgz", "bz2", "xz", "tar", "zst"],
        .documents: ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "epub", "csv", "odt", "pages", "key", "numbers"],
        .music: ["mp3", "m4a", "aac", "flac", "wav", "ogg", "opus", "aiff"],
        .video: ["mp4", "mkv", "mov", "avi", "webm", "m4v", "wmv", "flv", "ts"],
        .programs: ["dmg", "pkg", "app", "exe", "msi", "deb", "rpm", "apk", "iso", "img"],
        .images: ["jpg", "jpeg", "png", "gif", "webp", "heic", "svg", "tiff", "bmp", "raw"],
    ]

    public init(fileName: String) {
        let ext = (fileName as NSString).pathExtension.lowercased()
        self = FileCategory.map.first { $0.value.contains(ext) }?.key ?? .other
    }

    /// The subfolder of the download folder for this type, like IDM. Files of no particular type stay in the folder.
    public var folder: String? { self == .other ? nil : rawValue }

    public var symbol: String {
        switch self {
        case .compressed: return "doc.zipper"
        case .documents: return "doc.text"
        case .music: return "music.note"
        case .video: return "film"
        case .programs: return "app.badge"
        case .images: return "photo"
        case .other: return "doc"
        }
    }
}

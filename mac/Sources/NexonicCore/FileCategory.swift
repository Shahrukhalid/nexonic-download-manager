import Foundation

public enum FileCategory: String, Codable, CaseIterable, Sendable {
    case compressed = "Compressed", documents = "Documents", music = "Music", video = "Video",
         programs = "Programs", images = "Images", models = "Models", other = "Other"

    private static let map: [FileCategory: Set<String>] = [
        .compressed: ["zip", "rar", "7z", "gz", "tgz", "bz2", "xz", "tar", "zst", "zipx", "lz", "lzma", "lz4", "z", "cab", "arj", "tbz", "tbz2", "txz", "tzst", "br", "cpio", "sit", "sitx", "ace"],
        .documents: ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "epub", "csv", "odt", "ods", "odp", "md", "xps", "djvu", "mobi", "azw3", "cbz", "cbr", "pages", "key", "numbers", "dwg", "dxf", "dwf", "step", "stp", "iges", "igs", "stl", "3mf", "obj", "fbx", "blend", "skp", "f3d", "ifc", "sldprt", "sldasm", "ipt", "iam", "3ds", "dae", "glb", "gltf"],
        .music: ["mp3", "m4a", "aac", "flac", "wav", "ogg", "opus", "aiff", "alac", "wma", "mka", "ape", "dsf"],
        .video: ["mp4", "mkv", "mov", "avi", "webm", "m4v", "wmv", "flv", "ts", "3gp", "mts", "m2ts", "mpg", "mpeg", "vob", "ogv"],
        .programs: ["exe", "msi", "dmg", "pkg", "deb", "rpm", "apk", "iso", "img", "appx", "msix", "msixbundle", "appimage", "snap", "flatpak", "xapk", "apks", "app"],
        .images: ["jpg", "jpeg", "png", "gif", "webp", "heic", "heif", "avif", "jxl", "svg", "tiff", "tif", "bmp", "psd", "raw", "cr2", "cr3", "nef", "nrw", "arw", "srf", "sr2", "dng", "orf", "rw2", "raf", "pef", "srw", "x3f", "3fr", "erf", "kdc", "mrw", "iiq"],
        .models: ["safetensors", "gguf", "ggml", "onnx", "ckpt", "pt", "pth", "tflite", "mlmodel", "h5", "keras"],
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
        case .models: return "cpu"
        case .other: return "doc"
        }
    }
}

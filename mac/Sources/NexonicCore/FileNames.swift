import Foundation

public enum FileNames {
    /// File name from a Content-Disposition header (`filename*=UTF-8''…` wins over `filename="…"`).
    public static func fromContentDisposition(_ header: String?) -> String? {
        guard let header, !header.isEmpty else { return nil }
        for part in header.split(separator: ";").map({ $0.trimmingCharacters(in: .whitespaces) }) {
            let lower = part.lowercased()
            if lower.hasPrefix("filename*=") {
                var value = String(part.dropFirst("filename*=".count)).trimmingCharacters(in: CharacterSet(charactersIn: "\""))
                if let r = value.range(of: "''") { value = String(value[r.upperBound...]) }
                if let decoded = value.removingPercentEncoding, !decoded.isEmpty { return sanitize(decoded) }
            }
        }
        for part in header.split(separator: ";").map({ $0.trimmingCharacters(in: .whitespaces) }) where part.lowercased().hasPrefix("filename=") {
            let value = String(part.dropFirst("filename=".count)).trimmingCharacters(in: CharacterSet(charactersIn: "\" "))
            if !value.isEmpty { return sanitize(value) }
        }
        return nil
    }

    public static func fromURL(_ url: URL) -> String {
        let last = url.lastPathComponent.removingPercentEncoding ?? url.lastPathComponent
        return sanitize(last.isEmpty || last == "/" ? (url.host ?? "download") : last)
    }

    /// Removes path separators, control characters and leading dots; keeps names at most 200 characters.
    public static func sanitize(_ name: String) -> String {
        var cleaned = name.unicodeScalars.map { s -> Character in
            if s == "/" || s == ":" || s == "\\" || CharacterSet.controlCharacters.contains(s) { return "_" }
            return Character(s)
        }.reduce(into: "") { $0.append($1) }
        cleaned = cleaned.trimmingCharacters(in: .whitespacesAndNewlines)
        while cleaned.hasPrefix(".") { cleaned.removeFirst() }
        if cleaned.isEmpty { cleaned = "download" }
        if cleaned.count > 200 {
            let ext = (cleaned as NSString).pathExtension
            let stem = String((cleaned as NSString).deletingPathExtension.prefix(190))
            cleaned = ext.isEmpty ? stem : "\(stem).\(ext)"
        }
        return cleaned
    }

    /// "file.zip" → "file (1).zip" … until the name is free in `folder`.
    public static func unique(_ name: String, in folder: URL, fileManager: FileManager = .default) -> URL {
        var candidate = folder.appendingPathComponent(name)
        let ext = (name as NSString).pathExtension
        let stem = (name as NSString).deletingPathExtension
        var n = 1
        while fileManager.fileExists(atPath: candidate.path) {
            candidate = folder.appendingPathComponent(ext.isEmpty ? "\(stem) (\(n))" : "\(stem) (\(n)).\(ext)")
            n += 1
        }
        return candidate
    }
}

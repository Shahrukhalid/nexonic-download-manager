import Foundation

/// Download links for a QR code: a nexonic://add link that the phone app opens. Never holds cookies or passwords.
public enum ShareLinks {
    /// Longest link that still scans easily from a screen.
    public static let maxLength = 1200

    /// The link for as many of the downloads as fit, and how many that is.
    public static func link(_ urls: [URL]) -> (link: String, count: Int) {
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._~")
        var text = "nexonic://add"
        var n = 0
        var seen = Set<String>()
        for url in urls where ["http", "https"].contains(url.scheme?.lowercased() ?? "") && url.host != nil {
            let s = url.absoluteString
            guard seen.insert(s).inserted, let encoded = s.addingPercentEncoding(withAllowedCharacters: allowed) else { continue }
            let part = (n == 0 ? "?" : "&") + "url=" + encoded
            if text.count + part.count > maxLength { break }
            text += part
            n += 1
        }
        return (n == 0 ? "" : text, n)
    }
}

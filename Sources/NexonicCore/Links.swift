import Foundation

/// Pasted links: several at once, and the real file behind a search engine's or site's redirect link.
public enum Links {
    /// Links pasted together: one per line, or separated by spaces or commas.
    public static func split(_ text: String) -> [String] {
        let spaced = text.replacingOccurrences(of: #",\s*(?=https?://)"#, with: "\n", options: [.regularExpression, .caseInsensitive])
        return spaced.split(whereSeparator: \.isWhitespace)
            .map { $0.trimmingCharacters(in: CharacterSet(charactersIn: ",;")) }
            .filter { !$0.isEmpty }
    }

    /// The real address behind a redirect link: Google Images ("imgres?imgurl="), Google results ("url?q="),
    /// Facebook ("l.php?u=") and Reddit ("out.reddit.com?url="). Other links are returned unchanged.
    public static func unwrap(_ url: URL) -> URL {
        guard let c = URLComponents(url: url, resolvingAgainstBaseURL: false), let host = c.host?.lowercased() else { return url }
        func param(_ names: String...) -> URL? {
            for n in names {
                if let v = c.queryItems?.first(where: { $0.name == n })?.value, let u = URL(string: v),
                   ["http", "https"].contains(u.scheme?.lowercased() ?? ""), u.host != nil { return u }
            }
            return nil
        }
        let google = host.hasPrefix("google.") || host.hasPrefix("www.google.") || host.hasPrefix("images.google.")
        if google, c.path == "/imgres", let u = param("imgurl") { return u }
        if google, c.path == "/url", let u = param("q", "url") { return u }
        if ["l.facebook.com", "lm.facebook.com"].contains(host), c.path == "/l.php", let u = param("u") { return u }
        if host == "out.reddit.com", let u = param("url") { return u }
        return url
    }
}

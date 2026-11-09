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
        return direct(url)
    }

    /// The file itself behind a share page: Google Drive files (and Docs, Sheets and Slides as Office files), Dropbox
    /// share links and GitHub file pages. Other links are returned unchanged.
    public static func direct(_ url: URL) -> URL {
        guard var c = URLComponents(url: url, resolvingAgainstBaseURL: false), let host = c.host?.lowercased(),
              ["http", "https"].contains(c.scheme?.lowercased() ?? "") else { return url }
        let path = c.percentEncodedPath.split(separator: "/").map(String.init)
        func query(_ name: String) -> String? { c.queryItems?.first(where: { $0.name == name })?.value }
        func id(_ v: String?) -> String? {
            guard let v, v.count >= 10, v.allSatisfy({ $0.isASCII && ($0.isLetter || $0.isNumber || $0 == "_" || $0 == "-") }) else { return nil }
            return v
        }
        if ["drive.google.com", "docs.google.com", "drive.usercontent.google.com"].contains(host) {
            if host == "docs.google.com", path.count >= 3, path[1] == "d", let doc = id(path[2]) {
                let format = ["document": "docx", "spreadsheets": "xlsx", "presentation": "pptx"][path[0]]
                if let format { return URL(string: "https://docs.google.com/\(path[0])/d/\(doc)/export?format=\(format)") ?? url }
            }
            let file = path.count >= 3 && path[0] == "file" && path[1] == "d" ? id(path[2])
                : path.count >= 1 && ["open", "uc", "download"].contains(path[0]) ? id(query("id")) : nil
            guard let file else { return url }
            var out = URLComponents(string: "https://drive.usercontent.google.com/download")!
            out.queryItems = [URLQueryItem(name: "id", value: file), URLQueryItem(name: "export", value: "download"), URLQueryItem(name: "confirm", value: "t")]
            if let key = query("resourcekey"), !key.isEmpty { out.queryItems!.append(URLQueryItem(name: "resourcekey", value: key)) }
            return out.url ?? url
        }
        if ["dropbox.com", "www.dropbox.com"].contains(host), path.count >= 2, ["s", "scl", "sh"].contains(path[0]) {
            var items = (c.queryItems ?? []).filter { $0.name != "dl" }
            items.append(URLQueryItem(name: "dl", value: "1"))
            c.host = "www.dropbox.com"; c.queryItems = items
            return c.url ?? url
        }
        if host == "github.com", path.count >= 5, path[2] == "blob" {
            return URL(string: "https://raw.githubusercontent.com/" + (Array(path[0..<2]) + Array(path[3...])).joined(separator: "/")) ?? url
        }
        return url
    }
}

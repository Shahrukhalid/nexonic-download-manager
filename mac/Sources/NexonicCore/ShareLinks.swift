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
        for url in urls.map(clean) where ["http", "https"].contains(url.scheme?.lowercased() ?? "") && url.host != nil {
            let s = url.absoluteString
            guard seen.insert(s).inserted, let encoded = s.addingPercentEncoding(withAllowedCharacters: allowed) else { continue }
            let part = (n == 0 ? "?" : "&") + "url=" + encoded
            if text.count + part.count > maxLength { break }
            text += part
            n += 1
        }
        return (n == 0 ? "" : text, n)
    }

    /// The link without a user name or password (https://user:pass@host/… → https://host/…).
    public static func clean(_ url: URL) -> URL {
        guard url.user != nil || url.password != nil, var c = URLComponents(url: url, resolvingAgainstBaseURL: false) else { return url }
        c.user = nil
        c.password = nil
        return c.url ?? url
    }

    static let privateKeys: Set<String> = [
        "token", "access_token", "auth", "authorization", "key", "apikey", "api_key", "sig", "signature", "session",
        "sessionid", "sid", "password", "pass", "secret", "x-amz-signature", "x-amz-credential", "x-goog-signature",
        "googleaccessid", "se", "sp", "sv", "hdnts", "policy", "key-pair-id", "hmac", "expires",
    ]

    /// True when a link seems to carry a private access key (a token or signed-link parameter), so anyone who scans
    /// the code could use it. The app warns; the user decides.
    public static func looksPrivate(_ url: URL) -> Bool {
        if url.user != nil || url.password != nil { return true }
        let names = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.map { $0.name.lowercased() } ?? []
        return names.contains { privateKeys.contains($0) || $0.hasSuffix("token") || $0.hasSuffix("signature") }
    }
}

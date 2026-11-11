import Foundation

public enum DebridService: String, Codable, CaseIterable, Sendable {
    case none, realDebrid, allDebrid, premiumize
    public var title: String {
        switch self {
        case .none: return "Off"
        case .realDebrid: return "Real-Debrid"
        case .allDebrid: return "AllDebrid"
        case .premiumize: return "Premiumize"
        }
    }
}

/// An optional debrid account (your own API key; off by default). Links from the sites listed are turned into a direct
/// link by the service before downloading; nothing is sent to the service for other sites.
public struct DebridSettings: Codable, Equatable, Sendable {
    public var service: DebridService = .none
    /// Sites (comma or space separated) whose links go through the service; subdomains included.
    public var hosts: String = ""
    /// The API key: kept in a private file (see Debrid.key), never in the settings or a backup.
    public var key: String = ""
    public init() {}
    enum CodingKeys: String, CodingKey { case service, hosts }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        service = (try? c.decodeIfPresent(DebridService.self, forKey: .service)) ?? .none
        hosts = try c.decodeIfPresent(String.self, forKey: .hosts) ?? ""
    }
}

public enum Debrid {
    public struct Failure: LocalizedError {
        public let message: String
        public var errorDescription: String? { message }
    }

    /// The key file, readable only by this user.
    static var keyFile: URL { AppPaths.support.appendingPathComponent("debrid-key", isDirectory: false) }

    public static func loadKey() -> String {
        (try? String(contentsOf: keyFile, encoding: .utf8))?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    }

    public static func saveKey(_ key: String) {
        let k = key.trimmingCharacters(in: .whitespacesAndNewlines)
        if k.isEmpty { try? FileManager.default.removeItem(at: keyFile); return }
        try? FileManager.default.createDirectory(at: AppPaths.support, withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: keyFile.path, contents: Data(k.utf8), attributes: [.posixPermissions: 0o600])
    }

    private static let lock = NSLock()
    private static var current = DebridSettings()
    /// Set by the app when it starts and when Settings change (with the key from the key file).
    public static var settings: DebridSettings {
        get { lock.lock(); defer { lock.unlock() }; return current }
        set { lock.lock(); current = newValue; lock.unlock() }
    }

    public static func applies(_ url: URL, _ s: DebridSettings? = nil) -> Bool {
        let s = s ?? settings
        guard s.service != .none, !s.key.trimmingCharacters(in: .whitespaces).isEmpty,
              ["http", "https"].contains(url.scheme?.lowercased() ?? ""), let host = url.host?.lowercased() else { return false }
        return s.hosts.split(whereSeparator: { ", ;\n".contains($0) })
            .map { $0.trimmingCharacters(in: .whitespaces).lowercased().trimmingCharacters(in: CharacterSet(charactersIn: ".")) }
            .contains { !$0.isEmpty && (host == $0 || host.hasSuffix("." + $0)) }
    }

    static func form(_ fields: [String: String]) -> Data {
        var allowed = CharacterSet.alphanumerics; allowed.insert(charactersIn: "-._~")
        return Data(fields.map { "\($0.key.addingPercentEncoding(withAllowedCharacters: allowed)!)=\($0.value.addingPercentEncoding(withAllowedCharacters: allowed)!)" }
            .sorted().joined(separator: "&").utf8)
    }

    /// The service's request for one link.
    public static func request(_ s: DebridSettings, link: String) throws -> URLRequest {
        let key = s.key.trimmingCharacters(in: .whitespaces)
        var req: URLRequest
        switch s.service {
        case .realDebrid:
            req = URLRequest(url: URL(string: "https://api.real-debrid.com/rest/1.0/unrestrict/link")!)
            req.httpBody = form(["link": link]); req.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        case .allDebrid:
            req = URLRequest(url: URL(string: "https://api.alldebrid.com/v4/link/unlock?agent=nexonic")!)
            req.httpBody = form(["link": link]); req.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        case .premiumize:
            req = URLRequest(url: URL(string: "https://www.premiumize.me/api/transfer/directdl")!)
            req.httpBody = form(["apikey": key, "src": link])
        case .none:
            throw Failure(message: "No debrid service is chosen.")
        }
        req.httpMethod = "POST"
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        return req
    }

    /// The direct link in the service's answer, or a Failure with its message.
    public static func parseAnswer(_ service: DebridService, _ data: Data) throws -> URL {
        let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] ?? [:]
        var link: String?
        switch service {
        case .realDebrid: link = json["download"] as? String
        case .allDebrid: link = (json["data"] as? [String: Any])?["link"] as? String
        case .premiumize: link = ((json["content"] as? [[String: Any]])?.first?["link"] as? String) ?? json["location"] as? String
        case .none: break
        }
        if let link, let u = URL(string: link), ["http", "https"].contains(u.scheme?.lowercased() ?? "") { return u }
        let error = json["error"]
        let message = (error as? String) ?? ((error as? [String: Any])?["message"] as? String) ?? (json["message"] as? String)
        throw Failure(message: "\(service.title) could not unlock the link" + (message.map { ": \($0)" } ?? "."))
    }

    /// The direct link for `url` when the service applies to it, else `url` unchanged.
    public static func resolve(_ url: URL, proxy: ProxySettings) async throws -> URL {
        let s = settings
        guard applies(url, s) else { return url }
        let session = URLSession(configuration: Network.configuration(proxy: proxy))
        defer { session.finishTasksAndInvalidate() }
        let (data, _) = try await session.data(for: try request(s, link: url.absoluteString))
        return try parseAnswer(s.service, data)
    }
}

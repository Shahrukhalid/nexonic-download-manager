import Foundation
import Security

/// Where cookies and passwords are kept: a private file in the app, memory in tests.
public protocol SecretStore: AnyObject {
    /// All saved secrets, or nil when they can't be read (Keychain locked or access denied). Then nothing is written
    /// either, so a denied prompt never wipes them.
    func read() -> [String: String]?
    func write(_ values: [String: String]) -> Bool
}

/// One Keychain item ("Nexonic Download Manager") holding the cookies and passwords of the download list and the proxy.
public final class KeychainStore: SecretStore {
    private let service = "com.nexonic.downloadmanager"
    private let account = "secrets"

    public init() {}

    fileprivate var query: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }

    public func read() -> [String: String]? {
        var q = query
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var out: CFTypeRef?
        let status = SecItemCopyMatching(q as CFDictionary, &out)
        if status == errSecItemNotFound { return [:] }
        guard status == errSecSuccess, let data = out as? Data else { return nil }
        return try? JSONDecoder().decode([String: String].self, from: data)
    }

    public func write(_ values: [String: String]) -> Bool {
        guard let data = try? JSONEncoder().encode(values) else { return false }
        let status = SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        guard status == errSecItemNotFound else { return status == errSecSuccess }
        var add = query
        add[kSecValueData as String] = data
        add[kSecAttrLabel as String] = "Nexonic Download Manager"
        add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        return SecItemAdd(add as CFDictionary, nil) == errSecSuccess
    }
}

extension KeychainStore {
    /// Runs Keychain calls without ever showing a password prompt (they fail instead).
    static func silently<T>(_ body: () -> T) -> T {
        var was: DarwinBoolean = true
        SecKeychainGetUserInteractionAllowed(&was)
        SecKeychainSetUserInteractionAllowed(false)
        defer { SecKeychainSetUserInteractionAllowed(was.boolValue) }
        return body()
    }

    /// What 0.6.2 and 0.6.3 saved, if macOS hands it over without asking; nil otherwise.
    public func readWithoutPrompt() -> [String: String]? {
        Self.silently { read() }
    }

    /// Removes the item (without asking). Used once the secrets have moved to the private file.
    public func deleteWithoutPrompt() {
        _ = Self.silently { SecItemDelete(query as CFDictionary) }
    }
}

/// Cookies, download passwords and the proxy password in one private file (secrets.json, readable only by your user
/// account, inside your Library; protected on disk by FileVault). The Keychain is no longer used: the app isn't signed
/// with a developer certificate, so macOS asked for the login password after every update.
public final class FileStore: SecretStore {
    public let url: URL
    private let migrate: Bool

    /// `migrate`: move secrets saved in the Keychain by 0.6.2/0.6.3 into the file (the app does this; the browser
    /// helper only reads).
    public init(url: URL = AppPaths.support.appendingPathComponent("secrets.json"), migrate: Bool = true) {
        self.url = url
        self.migrate = migrate
    }

    public func read() -> [String: String]? {
        if let data = try? Data(contentsOf: url) {
            return try? JSONDecoder().decode([String: String].self, from: data)  // damaged: nil, so it's never overwritten
        }
        guard !FileManager.default.fileExists(atPath: url.path) else { return nil }  // there but unreadable
        guard migrate else { return [:] }
        let keychain = KeychainStore()
        guard let old = keychain.readWithoutPrompt(), !old.isEmpty else { return [:] }
        guard write(old) else { return nil }  // not moved yet: the Keychain item stays and the move is tried again
        keychain.deleteWithoutPrompt()
        return old
    }

    public func write(_ values: [String: String]) -> Bool {
        guard let data = try? JSONEncoder().encode(values) else { return false }
        let fm = FileManager.default
        try? fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let temp = url.deletingLastPathComponent().appendingPathComponent(".secrets-\(UUID().uuidString).json")
        // Created owner-only before anything is written, then moved over the old file in one step.
        guard fm.createFile(atPath: temp.path, contents: data, attributes: [.posixPermissions: 0o600]) else { return false }
        do {
            if fm.fileExists(atPath: url.path) { _ = try fm.replaceItemAt(url, withItemAt: temp) } else { try fm.moveItem(at: temp, to: url) }
            try? fm.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
            return true
        } catch {
            try? fm.removeItem(at: temp)
            return false
        }
    }
}

/// For tests.
public final class MemoryStore: SecretStore {
    public var values: [String: String]?
    public var failWrites = false
    public init(_ values: [String: String]? = [:]) { self.values = values }
    public func read() -> [String: String]? { values }
    public func write(_ v: [String: String]) -> Bool {
        if failWrites { return false }
        values = v
        return true
    }
}

/// Cookies, download passwords and the proxy password live in the Keychain; the JSON files on disk no longer hold them.
/// Lists saved by older versions (with the secrets in the file) are moved into the Keychain on the first save.
public enum Secrets {
    public static let proxyKey = "proxy.password"
    static func key(_ id: UUID, _ field: String) -> String { "download.\(id.uuidString).\(field)" }
    static func siteKey(_ id: UUID, _ field: String) -> String { "site.\(id.uuidString).\(field)" }

    /// Every secret of these downloads and settings.
    public static func collect(_ records: [DownloadRecord], _ settings: AppSettings) -> [String: String] {
        var out: [String: String] = [:]
        for r in records {
            if let c = r.cookies, !c.isEmpty { out[key(r.id, "cookies")] = c }
            if let p = r.password, !p.isEmpty { out[key(r.id, "password")] = p }
        }
        if !settings.proxy.password.isEmpty { out[proxyKey] = settings.proxy.password }
        for p in settings.hostProfiles {
            if let pw = p.password, !pw.isEmpty { out[siteKey(p.id, "password")] = pw }
            if let pw = p.proxy?.password, !pw.isEmpty { out[siteKey(p.id, "proxy")] = pw }
        }
        return out
    }

    /// Copies for the files on disk, without the secrets.
    public static func stripped(_ records: [DownloadRecord]) -> [DownloadRecord] {
        records.map { r in
            var r = r
            r.cookies = nil
            r.password = nil
            return r
        }
    }

    public static func stripped(_ settings: AppSettings) -> AppSettings {
        var s = settings
        s.proxy.password = ""
        for i in s.hostProfiles.indices {
            s.hostProfiles[i].password = nil
            s.hostProfiles[i].proxy?.password = ""
        }
        return s
    }

    /// Puts the saved secrets back. A value still in the file (saved by an older version) is kept as it is.
    public static func fill(_ records: inout [DownloadRecord], _ settings: inout AppSettings, from values: [String: String]) {
        for i in records.indices {
            if records[i].cookies?.isEmpty ?? true, let c = values[key(records[i].id, "cookies")] { records[i].cookies = c }
            if records[i].password?.isEmpty ?? true, let p = values[key(records[i].id, "password")] { records[i].password = p }
        }
        if settings.proxy.password.isEmpty, let p = values[proxyKey] { settings.proxy.password = p }
        for i in settings.hostProfiles.indices {
            let id = settings.hostProfiles[i].id
            if settings.hostProfiles[i].password?.isEmpty ?? true, let p = values[siteKey(id, "password")] { settings.hostProfiles[i].password = p }
            if settings.hostProfiles[i].proxy?.password.isEmpty == true, let p = values[siteKey(id, "proxy")] { settings.hostProfiles[i].proxy?.password = p }
        }
    }
}

/// Reading saved lists without losing them.
public enum SavedFile {
    /// Decodes a saved file. One that exists but can't be read is kept as "name.unreadable-DATE.json" (so the next save,
    /// which writes the empty list, can't destroy it) and nil is returned.
    public static func load<T: Decodable>(_ type: T.Type, from url: URL) -> T? { read(type, from: url).value }

    /// Like `load`, and tells whether a file was there but could not be read (`damaged`).
    public static func read<T: Decodable>(_ type: T.Type, from url: URL) -> (value: T?, damaged: Bool) {
        guard FileManager.default.fileExists(atPath: url.path) else { return (nil, false) }
        guard let data = try? Data(contentsOf: url) else { return (nil, true) }
        do {
            return (try JSONDecoder().decode(type, from: data), false)
        } catch {
            let f = DateFormatter()
            f.dateFormat = "yyyyMMdd-HHmmss"
            let copy = url.deletingPathExtension().appendingPathExtension("unreadable-\(f.string(from: Date())).json")
            try? FileManager.default.copyItem(at: url, to: copy)
            return (nil, true)
        }
    }
}

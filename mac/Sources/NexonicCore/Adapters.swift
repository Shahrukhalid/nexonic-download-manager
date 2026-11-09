import Foundation
import Darwin

/// Keeps downloads on one network adapter (a VPN, for example). Torrents and the video engine are bound to the
/// adapter's address; regular downloads check it before every connection and while data arrives, and stop when it
/// disconnects instead of using another adapter (kill switch).
public enum Adapters {
    public struct Adapter: Equatable, Sendable {
        public let name: String
        public let up: Bool
        public let addresses: [String]
    }

    public struct Down: LocalizedError, Equatable {
        public let adapter: String
        public var errorDescription: String? {
            "The network adapter \"\(adapter)\" is not connected. Downloads wait for it instead of using another connection."
        }
    }

    /// Adapters with at least one usable address (IPv4 first), loopback last.
    public static func list() -> [Adapter] {
        var head: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&head) == 0, let first = head else { return [] }
        defer { freeifaddrs(head) }
        var order: [String] = [], up: [String: Bool] = [:], v4: [String: [String]] = [:], v6: [String: [String]] = [:]
        for p in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let name = String(cString: p.pointee.ifa_name)
            let flags = Int32(bitPattern: p.pointee.ifa_flags)
            if up[name] == nil { order.append(name) }
            up[name] = (up[name] ?? false) || (flags & IFF_UP != 0 && flags & IFF_RUNNING != 0)
            guard let sa = p.pointee.ifa_addr else { continue }
            let family = Int32(sa.pointee.sa_family)
            guard family == AF_INET || family == AF_INET6 else { continue }
            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            let length = socklen_t(family == AF_INET ? MemoryLayout<sockaddr_in>.size : MemoryLayout<sockaddr_in6>.size)
            guard getnameinfo(sa, length, &host, socklen_t(host.count), nil, 0, NI_NUMERICHOST) == 0 else { continue }
            let address = String(cString: host)
            if family == AF_INET { v4[name, default: []].append(address) }
            else if !address.lowercased().hasPrefix("fe80") && !address.contains("%") { v6[name, default: []].append(address) }
        }
        return order.compactMap { name -> Adapter? in
            let all = (v4[name] ?? []) + (v6[name] ?? [])
            return all.isEmpty ? nil : Adapter(name: name, up: up[name] ?? false, addresses: all)
        }.sorted { a, b in
            let la = a.addresses.allSatisfy(isLoopback), lb = b.addresses.allSatisfy(isLoopback)
            return la != lb ? !la : a.name.localizedCaseInsensitiveCompare(b.name) == .orderedAscending
        }
    }

    static func isLoopback(_ a: String) -> Bool { a.hasPrefix("127.") || a == "::1" }

    /// The adapter's addresses (IPv4 first), or Down when it is missing, down or has none.
    public static func addresses(of adapter: String, in all: [Adapter]? = nil) throws -> [String] {
        let name = adapter.trimmingCharacters(in: .whitespaces)
        guard let found = (all ?? list()).first(where: { $0.name.caseInsensitiveCompare(name) == .orderedSame }),
              found.up, !found.addresses.isEmpty else { throw Down(adapter: name) }
        return found.addresses
    }

    /// The address torrents and the video engine use for this adapter.
    public static func sourceAddress(_ adapter: String) throws -> String { try addresses(of: adapter)[0] }

    // MARK: the adapter downloads must use

    private static let lock = NSLock()
    private static var chosen = ""
    private static var checked: (at: Date, down: Down?) = (.distantPast, nil)

    /// Set by the app when it starts and when Settings change.
    public static var required: String {
        get { lock.lock(); defer { lock.unlock() }; return chosen }
        set { lock.lock(); chosen = newValue.trimmingCharacters(in: .whitespaces); checked = (.distantPast, nil); lock.unlock() }
    }

    /// Why downloads can't run now (the chosen adapter is down), or nil. Checked at most once a second.
    public static func problem() -> Down? {
        lock.lock(); defer { lock.unlock() }
        guard !chosen.isEmpty else { return nil }
        if Date().timeIntervalSince(checked.at) < 1 { return checked.down }
        var down: Down?
        do { _ = try addresses(of: chosen) } catch let e as Down { down = e } catch { down = Down(adapter: chosen) }
        checked = (Date(), down)
        return down
    }

    /// Settings' adapter list: "" (any adapter) first, then every adapter, keeping a saved one that is offline.
    public static func choices(current: String) -> [String] {
        var names = [""] + list().map(\.name)
        let saved = current.trimmingCharacters(in: .whitespaces)
        if !saved.isEmpty, !names.contains(where: { $0.caseInsensitiveCompare(saved) == .orderedSame }) { names.append(saved) }
        return names
    }
}

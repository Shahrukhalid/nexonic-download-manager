import Foundation

/// The folder the app and the Share extension share (an App Group). Unsigned test builds have no App Group:
/// then the app's own folder is used, which is enough for the app and the tests.
public enum AppGroup {
    public static let id = "group.com.nexonic.downloadmanager"

    public static var folder: URL {
        if let url = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: id) { return url }
        let url = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Shared", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    /// True when this build has the App Group (signed builds); unsigned test builds don't.
    public static var available: Bool { FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: id) != nil }

    public static var defaults: UserDefaults { UserDefaults(suiteName: id) ?? .standard }
}

/// Tells the other process (app or Share extension) that the download list changed.
public enum Changes {
    static let name = "com.nexonic.downloadmanager.changed" as CFString

    public static func post() {
        CFNotificationCenterPostNotification(CFNotificationCenterGetDarwinNotifyCenter(), CFNotificationName(name), nil, nil, true)
    }
}

public final class ChangeObserver {
    private let block: () -> Void

    public init(_ block: @escaping () -> Void) {
        self.block = block
        let observer = Unmanaged.passUnretained(self).toOpaque()
        CFNotificationCenterAddObserver(CFNotificationCenterGetDarwinNotifyCenter(), observer, { _, observer, _, _, _ in
            guard let observer else { return }
            let me = Unmanaged<ChangeObserver>.fromOpaque(observer).takeUnretainedValue()
            DispatchQueue.main.async { me.block() }
        }, Changes.name, nil, .deliverImmediately)
    }

    deinit {
        CFNotificationCenterRemoveEveryObserver(CFNotificationCenterGetDarwinNotifyCenter(), Unmanaged.passUnretained(self).toOpaque())
    }
}

/// Settings shared by the app and the Share extension.
public enum SharedSettings {
    public static var wifiOnly: Bool {
        get { AppGroup.defaults.bool(forKey: "wifiOnly") }
        set { AppGroup.defaults.set(newValue, forKey: "wifiOnly") }
    }
    /// Save into Files › Nexonic › Video, Music, Documents… (on by default, like the desktop apps).
    public static var categoryFolders: Bool {
        get { AppGroup.defaults.object(forKey: "categoryFolders") as? Bool ?? true }
        set { AppGroup.defaults.set(newValue, forKey: "categoryFolders") }
    }
}

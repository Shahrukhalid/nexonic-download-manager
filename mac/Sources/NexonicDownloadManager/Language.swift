import Foundation
import ObjectiveC
import SwiftUI

/// The interface language picked in Settings ("" = same as the system). It switches while the app runs: every
/// window and menu redraws in the new language at once, and the choice is also kept as the app's AppleLanguages so
/// the next launch starts in it.
enum AppLanguage {
    static let all: [(code: String, name: String)] = [
        ("en", "English"), ("ar", "العربية"), ("de", "Deutsch"), ("es", "Español"), ("fr", "Français"),
        ("hi", "हिन्दी"), ("id", "Bahasa Indonesia"), ("ja", "日本語"), ("ko", "한국어"), ("pt", "Português"),
        ("ru", "Русский"), ("tr", "Türkçe"), ("ur", "اردو"), ("zh-Hans", "简体中文"), ("zh-Hant", "繁體中文"),
    ]

    static var chosen: String { UserDefaults.standard.string(forKey: "NexonicLanguage") ?? "" }

    /// The table texts are looked up in, or nil before start().
    fileprivate static var table: Bundle?

    /// Call once when the app starts, before any window is shown.
    static func start() {
        object_setClass(Bundle.main, LanguageBundle.self)
        load()
    }

    /// Switches to a language ("" = the system's) and redraws everything.
    static func use(_ code: String) {
        UserDefaults.standard.set(code, forKey: "NexonicLanguage")
        if code.isEmpty { UserDefaults.standard.removeObject(forKey: "AppleLanguages") }
        else { UserDefaults.standard.set([code], forKey: "AppleLanguages") }
        load()
        LanguageState.shared.revision += 1
    }

    /// The language in use: the chosen one, or the best match for the system's languages.
    static var current: String {
        if !chosen.isEmpty { return chosen }
        let system = UserDefaults.standard.stringArray(forKey: "AppleLanguages") ?? Locale.preferredLanguages
        return Bundle.preferredLocalizations(from: Bundle.main.localizations.filter { $0 != "Base" }, forPreferences: system).first ?? "en"
    }

    static var isRightToLeft: Bool { ["ar", "ur"].contains(String(current.prefix(2))) }

    private static func load() {
        table = Bundle.main.path(forResource: current, ofType: "lproj").flatMap(Bundle.init(path:))
    }
}

/// Bundle.main looks texts up in the chosen language's table instead of the one picked at launch.
private final class LanguageBundle: Bundle {
    override func localizedString(forKey key: String, value: String?, table tableName: String?) -> String {
        if let t = AppLanguage.table { return t.localizedString(forKey: key, value: value, table: tableName) }
        return super.localizedString(forKey: key, value: value, table: tableName)
    }
}

/// Changes when the language changes, so views and menus that watch it redraw.
final class LanguageState: ObservableObject {
    static let shared = LanguageState()
    @Published var revision = 0
}

private struct LiveLanguage: ViewModifier {
    @ObservedObject private var state = LanguageState.shared

    func body(content: Content) -> some View {
        content
            .id(state.revision)
            .environment(\.locale, Locale(identifier: AppLanguage.current))
            .environment(\.layoutDirection, AppLanguage.isRightToLeft ? .rightToLeft : .leftToRight)
    }
}

extension View {
    /// Redraws this view in the new language as soon as it changes.
    func liveLanguage() -> some View { modifier(LiveLanguage()) }
}

import Foundation
import ObjectiveC
import SwiftUI

/// The interface language picked in Settings ("" = same as the system). It switches while the app runs: every
/// window and menu redraws in the new language at once, and the choice is also kept as the app's AppleLanguages so
/// the next launch starts in it.
enum AppLanguage {
    // BEGIN LANGUAGES (written by scripts/translate/translate.py apply)
    static let all: [(code: String, name: String)] = [
        ("en", "English"),
        ("ar", "العربية"),
        ("de", "Deutsch"),
        ("es", "Español"),
        ("fr", "Français"),
        ("hi", "हिन्दी"),
        ("id", "Bahasa Indonesia"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("pt", "Português"),
        ("ru", "Русский"),
        ("tr", "Türkçe"),
        ("ur", "اردو"),
        ("zh-Hans", "简体中文"),
        ("zh-Hant", "繁體中文"),
        ("af", "Afrikaans"),
        ("am", "አማርኛ"),
        ("as", "অসমীয়া"),
        ("az", "Azərbaycan"),
        ("be", "Беларуская"),
        ("bg", "Български"),
        ("bn", "বাংলা"),
        ("bs", "Bosanski"),
        ("ca", "Català"),
        ("ceb", "Cebuano"),
        ("ckb", "کوردیی ناوەندی"),
        ("cs", "Čeština"),
        ("cy", "Cymraeg"),
        ("da", "Dansk"),
        ("el", "Ελληνικά"),
        ("eo", "Esperanto"),
        ("et", "Eesti"),
        ("eu", "Euskara"),
        ("fa", "فارسی"),
        ("fi", "Suomi"),
        ("fil", "Filipino"),
        ("fy", "Frysk"),
        ("ga", "Gaeilge"),
        ("gd", "Gàidhlig"),
        ("gl", "Galego"),
        ("gu", "ગુજરાતી"),
        ("ha", "Hausa"),
        ("haw", "ʻŌlelo Hawaiʻi"),
        ("he", "עברית"),
        ("hmn", "Hmoob"),
        ("hr", "Hrvatski"),
        ("ht", "Kreyòl ayisyen"),
        ("hu", "Magyar"),
        ("hy", "Հայերեն"),
        ("ig", "Igbo"),
        ("is", "Íslenska"),
        ("it", "Italiano"),
        ("jv", "Basa Jawa"),
        ("ka", "ქართული"),
        ("kk", "Қазақ тілі"),
        ("km", "ខ្មែរ"),
        ("kn", "ಕನ್ನಡ"),
        ("ku", "Kurdî"),
        ("ky", "Кыргызча"),
        ("la", "Latina"),
        ("lb", "Lëtzebuergesch"),
        ("lo", "ລາວ"),
        ("lt", "Lietuvių"),
        ("lv", "Latviešu"),
        ("mg", "Malagasy"),
        ("mi", "Te Reo Māori"),
        ("mk", "Македонски"),
        ("ml", "മലയാളം"),
        ("mn", "Монгол"),
        ("mr", "मराठी"),
        ("ms", "Bahasa Melayu"),
        ("mt", "Malti"),
        ("my", "မြန်မာ"),
        ("nb", "Norsk bokmål"),
        ("ne", "नेपाली"),
        ("nl", "Nederlands"),
        ("ny", "Chichewa"),
        ("or", "ଓଡ଼ିଆ"),
        ("pa", "ਪੰਜਾਬੀ"),
        ("pl", "Polski"),
        ("ps", "پښتو"),
        ("ro", "Română"),
        ("rw", "Kinyarwanda"),
        ("sd", "سنڌي"),
        ("si", "සිංහල"),
        ("sk", "Slovenčina"),
        ("sl", "Slovenščina"),
        ("sm", "Gagana Samoa"),
        ("sn", "chiShona"),
        ("so", "Soomaali"),
        ("sq", "Shqip"),
        ("sr", "Српски"),
        ("st", "Sesotho"),
        ("su", "Basa Sunda"),
        ("sv", "Svenska"),
        ("sw", "Kiswahili"),
        ("ta", "தமிழ்"),
        ("te", "తెలుగు"),
        ("tg", "Тоҷикӣ"),
        ("th", "ไทย"),
        ("tk", "Türkmen"),
        ("tt", "Татарча"),
        ("ug", "ئۇيغۇرچە"),
        ("uk", "Українська"),
        ("uz", "Oʻzbek"),
        ("vi", "Tiếng Việt"),
        ("xh", "isiXhosa"),
        ("yi", "ייִדיש"),
        ("yo", "Yorùbá"),
        ("zu", "isiZulu"),
    ]

    /// Languages written right to left.
    static let rightToLeft: Set<String> = ["ar", "ur", "ckb", "fa", "he", "ps", "sd", "ug", "yi"]
    // END LANGUAGES

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

    static var isRightToLeft: Bool { rightToLeft.contains(current) || rightToLeft.contains(String(current.split(separator: "-").first ?? "")) }

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

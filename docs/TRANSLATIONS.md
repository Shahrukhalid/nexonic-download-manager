# Translations

English is the default language. Every app (Windows, Linux, Mac, iPhone and iPad, Android) and the browser
extension can also be shown in:

| Code  | Language              | Status                                             |
|-------|-----------------------|----------------------------------------------------|
| ar    | Arabic                | Machine translation, needs review (right-to-left)  |
| de    | German                | Machine translation, needs review                  |
| es    | Spanish               | Machine translation, needs review                  |
| fr    | French                | Machine translation, needs review                  |
| hi    | Hindi                 | Machine translation, needs review                  |
| id    | Indonesian            | Machine translation, needs review                  |
| ja    | Japanese              | Machine translation, needs review                  |
| ko    | Korean                | Machine translation, needs review                  |
| pt    | Portuguese            | Machine translation, needs review                  |
| ru    | Russian               | Machine translation, needs review                  |
| tr    | Turkish               | Machine translation, needs review                  |
| ur    | Urdu                  | Machine translation, needs review (right-to-left)  |
| zh-cn | Chinese (Simplified)  | Machine translation, needs review                  |
| zh-tw | Chinese (Traditional) | Machine translation, needs review                  |

Any text without a translation stays in English. The language files are plain text and add about 30 KB per
language; no fonts are bundled (each system's own fonts are used).

## Choosing a language

| App              | Where                                                    | When it applies            |
|------------------|----------------------------------------------------------|----------------------------|
| Windows, Linux   | Options → General → Language                             | After restarting the app   |
| Mac              | Settings → General → Language, then **Restart Now**      | After the restart          |
| iPhone and iPad  | Settings → Language                                      | Next time the app opens    |
| Android          | Settings → Language (Android 13+: also the system's app language settings) | Right away |
| Browser extension| The extension's menu → Settings → Language               | Right away                 |

"Same as the system" follows `LANGUAGE`, `LC_ALL`, `LC_MESSAGES` or `LANG` on Linux and the display language
elsewhere. Chinese picks Traditional for Taiwan, Hong Kong and Macau, Simplified otherwise.

## Where the texts are

Keys are always the English text; values are the translation. Keep keyboard shortcuts (`(Ctrl+N)`, `(F1)`),
placeholders (`{0}`, `{site}`, `%@`), the `…` of menu items and the app name unchanged.

| App              | Files                                                        | Tests                        |
|------------------|--------------------------------------------------------------|------------------------------|
| Windows, Linux   | `windows/Core/Locales/<code>.json`                           | `dotnet test linux/Tests`    |
| Mac, iPhone, iPad| `packaging/Localizations/<code>.lproj/Localizable.strings` (`zh-Hans`, `zh-Hant` for Chinese) | `swift test --package-path mac` |
| Android          | `android/app/src/main/assets/locales/<code>.json` and `res/values-<code>/strings.xml` | `gradle testDebugUnitTest` |
| Extension        | `extension/locales/<code>.json` (store name and description: `extension/_locales`) | `npm test` in `extension` |

## Improving a translation

1. Edit the files above for your language.
2. When a native speaker has checked every string, change its `status` (or first-line comment) to `Reviewed`,
   set `Reviewed` to `true` in `L.Languages` (`windows/Core/Localization.cs`) and update the tests that expect
   "needs review".
3. Run the tests; they check that every language has exactly the English keys.

## Adding a language

Copy the English file on each platform, fill in the values, and add the code to `L.Languages`
(`windows/Core/Localization.cs`), `Translations.languages` (Android), `locales_config.xml`, `CFBundleLocalizations`
(`packaging/Info.plist`, `ios/App/Info.plist`), `AppLanguage.all` (Mac and iPhone settings) and `LANGUAGES`
(`extension/helpers.js`).

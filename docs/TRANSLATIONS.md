# Translations

English is the default language. Every app (Windows, Linux, Mac, iPhone and iPad, Android) and the browser
extension can also be shown in:

| Code  | Language              | Status                                             |
|-------|-----------------------|----------------------------------------------------|
| ar    | Arabic                | Reviewed (right-to-left)                           |
| de    | German                | Reviewed                                           |
| es    | Spanish               | Reviewed                                           |
| fr    | French                | Reviewed                                           |
| hi    | Hindi                 | Reviewed                                           |
| id    | Indonesian            | Reviewed                                           |
| ja    | Japanese              | Reviewed                                           |
| ko    | Korean                | Reviewed                                           |
| pt    | Portuguese            | Reviewed                                           |
| ru    | Russian               | Reviewed                                           |
| tr    | Turkish               | Reviewed                                           |
| ur    | Urdu                  | Reviewed (right-to-left)                           |
| zh-cn | Chinese (Simplified)  | Reviewed                                           |
| zh-tw | Chinese (Traditional) | Reviewed                                           |

Any text without a translation stays in English. The language files are plain text and add about 30 KB per
language; no fonts are bundled (each system's own fonts are used).

## Choosing a language

| App              | Where                                                    | When it applies            |
|------------------|----------------------------------------------------------|----------------------------|
| Windows, Linux   | Options → General → Language                             | Right away                 |
| Mac              | Settings → General → Language                            | Right away                 |
| iPhone and iPad  | Settings → Language                                      | Right away                 |
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
2. Run the tests; they check that every language has exactly the English keys and keeps names and shortcuts.

Corrections are welcome as pull requests.

## Adding a language

All languages are listed once in `scripts/translate/languages.json` (code, own name, right to left, the `.lproj` name and
the browser stores' locale). Add a line there, then run

    python3 scripts/translate/translate.py apply

It writes every app's language files (missing texts stay in English) and the language lists in the code
(`L.Languages`, `Translations.languages`, `locales_config.xml`, `CFBundleLocalizations`, `AppLanguage.all` and the
extension's `LANGUAGES`).

## Machine translations

The **Translations** workflow (Actions tab → Translations → Run workflow) translates every text a language is missing with
the open-source MADLAD-400 model (google/madlad400-3b-mt, Apache-2.0) on GitHub's runners and pushes the result to the
branch `i18n/machine-translations` for a pull request. Reviewed translations are never replaced. App names, links, file
types, shortcuts and placeholders are kept as they are; a text whose translation loses one of them stays in English.
Machine-translated languages have the status `Machine translation, not reviewed`; change it to `Reviewed` once a person
has checked the file.

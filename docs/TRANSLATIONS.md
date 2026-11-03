# Translations

English is the default language. The Windows and Linux apps can also show the
main window, tray menu, menus, download statuses and dialog titles in:

| Code | Language   | Status                         |
|------|------------|--------------------------------|
| ar   | Arabic     | Machine translation, needs review (right-to-left) |
| de   | German     | Machine translation, needs review |
| es   | Spanish    | Machine translation, needs review |
| fr   | French     | Machine translation, needs review |
| hi   | Hindi      | Machine translation, needs review |
| pt   | Portuguese | Machine translation, needs review |
| ur   | Urdu       | Machine translation, needs review (right-to-left) |

Choose a language in **Options → General → Language** and restart the app.
"Same as the system" follows `LANGUAGE`, `LC_ALL`, `LC_MESSAGES` or `LANG`
on Linux and the Windows display language on Windows. Any text without a
translation falls back to English.

## Improving a translation

1. Edit `windows/Core/Locales/<code>.json`. Keys are the English text; values
   are the translation. Keep keyboard shortcuts, `{0}`-style placeholders and
   the app name unchanged.
2. When a native speaker has checked every string, change `status` to
   `Reviewed` and set `Reviewed` to `true` for that language in
   `L.Languages` (`windows/Core/Localization.cs`).
3. Run the tests (`dotnet test linux/Tests`); they check that every language
   has exactly the same keys as `en.json`.

## Adding a language

Copy `en.json` to `<code>.json`, fill in the values, and add the language to
`L.Languages` in `windows/Core/Localization.cs`.

The Mac, Android and iPhone apps are English only for now.

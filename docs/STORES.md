# Browser extension stores

`python3 scripts/build-extensions.py` makes the uploads in `build/store/`:

| Store | File |
|---|---|
| Chrome Web Store | `NexonicExtension-chrome-web-store-<version>.zip` |
| Microsoft Edge Add-ons | `NexonicExtension-edge-add-ons-<version>.zip` |
| addons.mozilla.org | `NexonicFirefoxAddon-amo-<version>.zip` and, when asked for source, `NexonicFirefoxAddon-amo-source-<version>.zip` |

`npm test` in `extension/` checks the rules that can be checked before submitting: manifest version 3, name and
description length in every language, icons, no unused permissions, and no remote or generated code.

The store uploads have no `key` field: each store gives the extension its own ID. After the first upload, add that ID
to `NativeMessaging.AllowedExtensionIds` (`windows/Core/NativeMessaging.cs`) and `allowedExtensionIDs`
(`mac/Sources/NexonicCore/NativeMessaging.swift`) so the browser helper accepts the store version, and release the apps.
The Firefox ID stays the same (`browser_specific_settings` in the add-on).

## Listing texts

**Single purpose:** sends downloads, videos and links from the browser to the Nexonic Download Manager app on the same
computer.

**Permissions:**

| Permission | Why |
|---|---|
| `downloads` | to hand a download the user starts to the app instead of the browser (the browser keeps it if the app can't be reached) |
| `nativeMessaging` | to talk to the app on the same computer |
| `contextMenus` | "Download with Nexonic" in the right-click menu |
| `storage` | the extension's own settings (language, sites to skip) |
| `cookies` | the cookies of the download's own site, so downloads that need a login work in the app; whether you are signed in to Instagram or Facebook (for the popup's "Download all videos") |
| `scripting` | to collect the links of the current page (or of the selected text) when the user asks to download them all, and the post list of the Instagram or Facebook profile open in the tab for "Download all" |
| `webRequest` | to notice video streams the page plays, for "Download this video" |
| `tabs` | the current page's address and title for the context menu and the popup |
| host access (all sites) | downloads and videos can come from any site |

**Data:** the extension collects nothing and sends nothing anywhere except to the app on the same computer. When you
choose "Download all" on an Instagram or Facebook profile you have open, it reads that profile's post list in that tab
with your browser's sign-in (read-only, one page every few seconds, at most 60 pages an hour) and hands the video and
photo links to the app. No
analytics, no accounts. Privacy policy: the "Privacy and terms" section of the README and TERMS.md.

The owner submits the uploads; this repository never publishes to a store automatically.

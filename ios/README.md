# Nexonic Download Manager for iPhone and iPad

Native SwiftUI app for iOS and iPadOS 17 or later, with a Share extension and a Shortcuts action. It shares the Mac and
Windows apps' rules for file names, categories, links and batch patterns (`mac/Sources/NexonicCore`).

## Download with one tap

- **Share → Download with Nexonic**: from Safari, Chrome and any app that shares a link or text with links. It works as
  soon as the app is installed; no Shortcut needed. The Nexonic download window shows the file name, size, type, the
  quality choices found on the page (its videos, music and files), and **Download Now** / **Download Later**.
- **Shortcuts and Siri**: the **Download with Nexonic** action appears in the Shortcuts app on its own (App Intents).
  It takes one or more links and can be put in the Share sheet with *Show in Share Sheet*. Say *"Download with Nexonic"*.
- **Set Up Download Shortcut**: a guide in the app (shown at first launch and in Settings) with the steps above.

## In the app

- Downloads list: All / Unfinished / Finished, by type, search, swipe to pause, resume or remove. On iPad the details
  open beside the list.
- Details: progress, size, quality, where it's saved; Open (Quick Look), Share, Show in Files, Login, Copy Link, Remove.
- Built-in browser: files the page can't show open the download window, long-press a link → **Download with Nexonic**,
  and **Found on this page** lists videos, music and files.
- Paste several links at once, or batch patterns like `file[01-50].jpg`.
- Files go to **Files › On My iPhone/iPad › Nexonic**, by type (Video, Music, Documents…) unless you turn that off.
- Settings: Wi-Fi only, folders by type, clear finished, clear browser data.
- Private: no account, ads or tracking. Download logins are kept in the Keychain on this device and sent only to their
  own site (same host and port, never from https to http). Browser cookies go to a download only for its own domain.

## iOS limits

- Downloads run in an iOS **background session**, so they continue when you leave the app or the Share sheet closes, and
  iOS wakes Nexonic when they finish. iOS manages these transfers itself: each file uses one connection (the 32-connection
  engine of the desktop and Android apps can't run in the background on iOS), and iOS may delay downloads on a weak
  network or in Low Power Mode.
- Safari and Chrome on iPhone don't run desktop extensions; the Share extension and the built-in browser replace them.
- Streaming video (HLS `.m3u8`, DASH) and site video extraction (the desktop video engine) are not available on iOS.
  Copy-protected video is never supported.

## Build

Needs Xcode 16 and [XcodeGen](https://github.com/yonaskolb/XcodeGen) (`brew install xcodegen`). The Xcode project is
generated from `project.yml`:

```bash
cd ios && NDM_VERSION=$(cat ../VERSION) NDM_TEAM_ID=<your team ID> xcodegen generate && open Nexonic.xcodeproj
```

- **On your iPhone or iPad:** pick your team in Xcode (Signing & Capabilities) and press Run. Xcode creates the App Group
  `group.com.nexonic.downloadmanager` that the app and the Share extension share.
- **Signed .ipa:** `NDM_TEAM_ID=<team ID> bash ios/build-ipa.sh` (development, `ad-hoc` or `app-store-connect`).
- The *iOS* GitHub Actions workflow runs the tests, builds the app for the simulator and an unsigned device build
  (`NexonicDownloadManager-iOS-unsigned.ipa`, which needs signing before it installs), and takes iPhone and iPad screenshots.

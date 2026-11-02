<div align="center">

# Nexonic Download Manager

**Free, open-source download manager for Mac and Windows: an alternative to Internet Download Manager (IDM).**

Download faster, pause and resume anytime, and save videos from YouTube, Instagram, Facebook, X (Twitter), TikTok and
1,000+ other sites in the best quality.

[![Latest release](https://img.shields.io/github/v/release/Shahrukhalid/nexonic-download-manager?label=download)](https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest)
![macOS 13+](https://img.shields.io/badge/macOS-13%2B-black)
![Windows 10+](https://img.shields.io/badge/Windows-10%2B-0078D6)
[![MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Website](https://img.shields.io/badge/website-online-2563eb)](https://shahrukhalid.github.io/nexonic-download-manager/)

<img src="https://raw.githubusercontent.com/Shahrukhalid/nexonic-download-manager/screenshots/mac-main.png" alt="Nexonic Download Manager" width="880">

</div>

## Download

| System | File |
|---|---|
| **Mac** (macOS 13+, Apple silicon and Intel) | [NexonicDownloadManager-Mac.dmg](https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest/download/NexonicDownloadManager-Mac.dmg) |
| **Windows** 10 / 11 | [NexonicDownloadManager-Windows-Setup.exe](https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest/download/NexonicDownloadManager-Windows-Setup.exe) |
| Windows, no install | [NexonicDownloadManager-Windows-Portable.zip](https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest/download/NexonicDownloadManager-Windows-Portable.zip) |
| Chrome, Edge, Brave, Opera extension | [NexonicExtension-Chrome.zip](https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest/download/NexonicExtension-Chrome.zip) |
| Firefox add-on | [NexonicAddon-Firefox.xpi](https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest/download/NexonicAddon-Firefox.xpi) |

Everything is included. Nothing else to install.

> **Mac users:** the easiest way is one command in Terminal. It installs or updates the app and opens it without the
> "Apple could not verify" message:
>
> ```bash
> curl -fsSL https://shahrukhalid.github.io/nexonic-download-manager/install.sh | bash
> ```
>
> Prefer the `.dmg`? That works too: macOS asks once (System Settings → Privacy & Security → **Open Anyway**), and
> never again for that version.

## Install

**Mac, quickest:** paste this into Terminal. It installs (or updates) the app in Applications and opens it, with no
security warning:

```bash
curl -fsSL https://shahrukhalid.github.io/nexonic-download-manager/install.sh | bash
```

**Mac, from the disk image**

1. Open the `.dmg`, read the Terms of Use and click **Agree**.
2. Drag **Nexonic Download Manager** onto **Applications** (replace the old version if asked), then open it from
   Applications.
3. The first time, macOS may say it *could not verify* the app, because it is free and not sold through Apple.
   Click **Done**, open **System Settings → Privacy & Security**, scroll down and click **Open Anyway**.
   On macOS 13–14 you can instead right-click the app → **Open**.
4. If macOS says the app *is damaged and can't be opened*, open **Terminal** and run
   `xattr -cr "/Applications/Nexonic Download Manager.app"`, then open the app again.

**Windows**

1. Run the Setup file. If Windows says "Windows protected your PC", click **More info → Run anyway**.
2. Read the Terms of Use, choose **I accept the agreement** and follow the steps.

**Browser** (open the app once first):

- **Chrome, Edge, Brave, Opera:** unzip the extension, open the browser's extensions page, turn on **Developer mode**,
  click **Load unpacked** and pick the folder.
- **Firefox:** drag the `.xpi` onto Firefox. If Firefox says the add-on "has not been verified", that release's add-on
  isn't signed by Mozilla yet; for now open `about:debugging#/runtime/this-firefox` → **Load Temporary Add-on…** and
  pick the `.xpi` (it stays until Firefox restarts).

Updating? Install the new version over the old one; your downloads and settings are kept. If the Dock or Start menu
still shows the old icon, quit the app and open it again (on Mac, restarting the computer refreshes the icon cache).

## Features

- **Fast downloads** that split each file into parts and download them at the same time.
- **Pause and resume**, even after a restart. Expired links can be refreshed without starting over.
- **Videos from YouTube, Instagram, Facebook, X, TikTok and 1,000+ sites**, in the quality you pick (the
  qualities each video offers are listed, up to 4K/8K) or sound only.
- **Download this video** button on any web page, and streamed video saved as one `.mp4`.
- **Browser integration** for Chrome, Edge, Brave, Opera and Firefox: click a download link (or the video button on a
  page) and a compact download window
  shows the name, size, category and folder (Start, Download Later or Cancel). If the app can't be reached, the browser
  simply keeps the download.
- **A progress window for every download** with speed, time left, a live speed graph, each connection's progress, and
  Pause/Resume/Cancel. Several downloads run side by side, each in its own window.
- **Scheduler and queues:** start at night, stop in the morning, sleep or shut down when done.
- **Batch downloads** (`img[001-100].jpg`), a **site grabber**, and **clipboard** detection.
- **Speed limits**, proxy, password-protected downloads and checksum checks.
- **Updates inside the app:** a new version shows up when you open the app, with what's new and Update Now, Remind
  Me Later or Skip This Version.
- **Same app on Mac and Windows**, light and dark mode, no account, no tracking.

<p align="center">
  <img src="https://raw.githubusercontent.com/Shahrukhalid/nexonic-download-manager/screenshots/mac-info.png" alt="Download window that opens when a link is clicked in the browser" width="440">
  <img src="https://raw.githubusercontent.com/Shahrukhalid/nexonic-download-manager/screenshots/mac-progress.png" alt="Progress window with speed graph and connections" width="440">
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/Shahrukhalid/nexonic-download-manager/screenshots/mac-add.png" alt="Add URL" width="290">
  <img src="https://raw.githubusercontent.com/Shahrukhalid/nexonic-download-manager/screenshots/mac-scheduler.png" alt="Scheduler" width="290">
  <img src="https://raw.githubusercontent.com/Shahrukhalid/nexonic-download-manager/screenshots/windows-main.png" alt="Windows" width="290">
</p>

## How to use

| To | Do this |
|---|---|
| Download a link | **Add URL**, paste the link, **Start Download** |
| Save a YouTube / Instagram / Facebook / X / TikTok video | Point at the video → **Download this video**, or paste its link into **Add URL** |
| Download from a website | Click the download link in your browser; the app takes over |
| Download at night | **Scheduler** → **New Queue** → set the time → add downloads with **Download Later** |
| Continue an expired download | Right-click → **Refresh Link…** |

Command line: `ndm <link>` (Mac: **Settings → Browsers & Command Line → Install ndm Command**; Windows: added by the
installer). Run `ndm help` for options.

Copy-protected services (Netflix, Disney+, Prime Video, Spotify) are not supported. Only download videos you have the
right to save.

## Help and support

- **User guide:** [docs/HELP.md](docs/HELP.md), also built into the app: **Help → Nexonic Download Manager Help** (⌘?)
  on Mac, the **Help** button or **F1** on Windows.
- **About:** the About window shows your version, links and a **Check for Updates** button.
- **Report a problem:** [open an issue](https://github.com/Shahrukhalid/nexonic-download-manager/issues/new) with your
  system, the app version (from About) and the link if it is public.
- **What's new:** [CHANGELOG.md](CHANGELOG.md) and the [releases page](https://github.com/Shahrukhalid/nexonic-download-manager/releases).

## Privacy and terms

No account, no ads, no tracking: your downloads and settings stay on your computer. The app only connects to the
addresses you download from, and to GitHub to keep its video engine up to date and when you check for updates.
By installing you agree to the **[Terms of Use and License Agreement](TERMS.md)**: download only what you have the
right to, and respect copyright and each site's terms.

## Build from source

```bash
git clone https://github.com/Shahrukhalid/nexonic-download-manager.git && cd nexonic-download-manager
UNIVERSAL=0 bash scripts/build-app.sh     # Mac
pwsh windows/build.ps1                    # Windows (.NET 8 SDK, Inno Setup 6)
```

## License

[MIT](LICENSE) © 2026 Shahrukh Khalid. Not affiliated with Internet Download Manager or any of the sites mentioned;
their names belong to their owners.

Made by **[Shahrukh Khalid](https://www.shahrukhalid.com)** · [GitHub](https://github.com/Shahrukhalid). If this app
saves you time, please ⭐ star the repo so others can find it.

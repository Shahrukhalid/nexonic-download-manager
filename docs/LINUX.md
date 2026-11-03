# Nexonic Download Manager on Linux

The Linux app is the same download manager as the Windows app: the same download engine, queues, scheduler, batch
download, site grabber, video downloads, browser extension and `ndm` command, with a window built for Linux desktops
(GNOME, KDE Plasma, Cinnamon, Xfce and others, on X11 and Wayland).

> Linux downloads are added to the [Releases page](https://github.com/Shahrukhalid/nexonic-download-manager/releases)
> from the first release that includes them. Until then, every change is built and tested automatically (GitHub
> Actions, workflow **Linux**), and the test builds can be downloaded from the workflow run.

## Which file to download

| Your system | File |
|---|---|
| Any distribution, 64-bit Intel/AMD | `NexonicDownloadManager-Linux-x86_64.AppImage` |
| Any distribution, 64-bit ARM | `NexonicDownloadManager-Linux-aarch64.AppImage` |
| Ubuntu, Debian, Linux Mint, Pop!_OS, elementary OS, Zorin OS | `nexonic-download-manager_<version>_amd64.deb` (`_arm64.deb` on ARM) |
| Fedora, openSUSE, RHEL, AlmaLinux, Rocky Linux | `nexonic-download-manager-<version>-1.x86_64.rpm` (`.aarch64.rpm` on ARM) |
| Arch Linux, Manjaro, EndeavourOS, SteamOS | the AppImage |

Everything is included (the .NET runtime, the video engine and ffmpeg). Nothing else needs to be installed.

## Install

**AppImage** (works on every distribution and updates itself):

```bash
chmod +x NexonicDownloadManager-Linux-x86_64.AppImage
./NexonicDownloadManager-Linux-x86_64.AppImage
```

On first start the app adds itself to your app menu, sets up the browser helper and adds the `ndm` command
(in `~/.local/bin`). Keep the AppImage where you want it (for example in `~/Applications`); if you move it, open it
once from the new place. If it doesn't start, install the `fuse3` package, or run it with `--appimage-extract-and-run`.

On **SteamOS** (Steam Deck), switch to Desktop Mode and use the AppImage; the system files stay untouched.

**Ubuntu, Debian and others with .deb:**

```bash
sudo apt install ./nexonic-download-manager_*_amd64.deb
```

**Fedora:** `sudo dnf install ./nexonic-download-manager-*.x86_64.rpm`
**openSUSE:** `sudo zypper install ./nexonic-download-manager-*.x86_64.rpm`

The packages install into `/opt/nexonic-download-manager` with `nexonic-download-manager` and `ndm` in `/usr/bin`.
They run no install scripts.

## Check the download

Each release lists the SHA-256 checksum of every file (also in `SHA256SUMS-linux-x64.txt` and
`SHA256SUMS-linux-arm64.txt`). In the folder with the files:

```bash
sha256sum -c --ignore-missing SHA256SUMS-linux-x64.txt
```

The AppImage's own updater only installs an update after checking it against the checksum GitHub publishes for it.

## Browser extension

Open the app once, then in **Options → Browsers & Command Line** choose **Open the Extension Folder**.

- **Chrome, Chromium, Edge, Brave, Vivaldi, Opera:** open the browser's extensions page, turn on **Developer mode**,
  click **Load unpacked** and choose the `Extension` folder.
- **Firefox:** open `Firefox Add-on.xpi` with Firefox.

The app registers its browser helper for every browser that is set up for your user (the native messaging files in
`~/.config/<browser>/NativeMessagingHosts` and `~/.mozilla/native-messaging-hosts`, Flatpak and Snap Firefox
included). **Set Up Browsers Again** repeats this after you install another browser.

## Passwords and cookies

Download passwords, the proxy password and browser cookies are saved encrypted (AES-256-GCM). The key is kept in your
desktop's keyring (GNOME Keyring, KWallet or another Secret Service), never in a file. The app uses `secret-tool`
(package `libsecret-tools` on Debian/Ubuntu, `libsecret` on Fedora). Without a keyring, the app says so in the status
bar and keeps these secrets only until it closes: they are never written to disk unencrypted.

## Desktop integration

- Notifications use the desktop's notification service (`notify-send`).
- The tray icon uses the StatusNotifier standard (KDE, Cinnamon, Xfce, Ubuntu's GNOME; plain GNOME needs the
  AppIndicator extension). Closing the window keeps downloads running; open the app again to bring it back.
- Light and dark mode follow the system, or choose one in **Options → General**.
- Finished files record where they came from (`user.xdg.origin.url`), like browser downloads.
- **Delete and Move File to Trash** uses the desktop's trash.
- Queues can sleep or shut down the computer when they finish (through systemd-logind).
- Files and links can be opened with the app (Open With), from `nexonic://add?url=…` links, or with `ndm <link>`.

## Remove

- **AppImage:** in **Options → Browsers & Command Line** choose **Remove Desktop Integration** (or run the AppImage
  with `--remove-integration`), then delete the AppImage.
- **Packages:** run `nexonic-download-manager --remove-integration`, then `sudo apt remove nexonic-download-manager`
  (or `dnf remove` / `zypper remove`).

Your downloads list and settings are in `~/.local/share/Nexonic Download Manager`; delete that folder to remove them too.

## Build

Needs the .NET 8 SDK, python3, `dpkg-deb`, `rpmbuild` and `desktop-file-validate`:

```bash
bash scripts/build-ffmpeg.sh linux .ffmpeg-cache/linux-x64
bash scripts/fetch-video-tools.sh linux-x64 .tools-cache/linux-x64
bash linux/packaging/build.sh x64 .ffmpeg-cache/linux-x64 .tools-cache/linux-x64
bash linux/packaging/check.sh x64      # optional: checks the files and opens every window under Xvfb
```

The files are in `build/linux-x64`. On a 64-bit ARM computer use `arm64` and `linux-arm64`.

The code: `linux/App` is the window (Avalonia, MIT License), `linux/Desktop` the Linux parts (download manager,
keyring, browser helper registration, desktop integration), and both use the download engine in `windows/Core`, shared
with the Windows app.

## Other systems

- **ChromeOS:** the AppImage and the .deb run in the Linux development environment (Crostini). Downloads saved there
  appear under **Linux files**; the extension in ChromeOS's own Chrome cannot reach the app inside Linux, so use
  Chrome or Firefox installed in the Linux environment. Not tested on every Chromebook.
- **FreeBSD:** not supported. .NET has no official FreeBSD build and the Linux compatibility layer does not run
  AppImages reliably; this may be looked at again later.
- **Flatpak:** not offered yet. A Flatpak sandbox would need extra permissions for the browser helper, the `ndm`
  command and saving to any folder.

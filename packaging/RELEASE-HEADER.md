# Nexonic Download Manager 0.9.1

This patch installs the official Nexonic icon across supported apps, installers and browser extensions. Existing application identifiers, settings, downloads and update compatibility are preserved.

Torrent support remains the experimental Linux provider from v0.9.0. Full native torrent support across platforms is still in development and is not included here. Android APK licensing and corresponding-source requirements remain unchanged; the release must include `Android-GPL-Sources.tar.gz`.

**Mac, first launch:** macOS says it *could not verify* the app because it's free and not sold through Apple. Click **Done**, open **System Settings → Privacy & Security**, click **Open Anyway**, then **Open Anyway** again and enter your Mac password. Only once; updates installed with **Update Now** open without a warning. [Pictures of each step](https://github.com/Shahrukhalid/nexonic-download-manager#first-launch-on-mac)

**Windows:** if SmartScreen appears, click **More info → Run anyway**.

**Android:** open `NexonicDownloadManager-Android.apk` on your phone and allow installing from your browser or Files app when asked. Older 32-bit phones: `NexonicDownloadManager-Android-32bit.apk`.

**Linux:** `NexonicDownloadManager-Linux-x86_64.AppImage` runs on every distribution (`chmod +x`, then open it); ARM computers: `-aarch64.AppImage`. Ubuntu, Debian, Mint, Pop!_OS: the `.deb`. Fedora, openSUSE: the `.rpm`. Check files with `sha256sum -c --ignore-missing SHA256SUMS-linux-x64.txt`. [Linux guide](https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/LINUX.md)

**Experimental Linux torrents:** off by default; install system `python3-libtorrent` (2.x) for `/usr/bin/python3`.
Preview torrent/magnet metadata before starting. The initial provider requires No proxy and no shared global
speed limit or DNS over HTTPS; per-torrent download limits are available. Other platforms do not include a torrent UI.
[Setup and limitations](https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/TORRENTS.md)

# Nexonic Download Manager 0.13.0

**Torrents on every platform:** Android and iPhone/iPad join Mac, Windows and Linux. Open a magnet link or a `.torrent` file, choose the files and download; every piece is checked. On iPhone and iPad torrents download while the app is open and continue when you come back. **Also new (from 0.12.0, which wasn't published):** Windows and Linux get torrent details, Peers and Trackers windows, settings that apply while a torrent runs, port forwarding and encryption, and open `.torrent` files and magnet links; new torrents no longer cap uploading, which makes them faster on every platform. Android APK licensing and corresponding-source requirements remain unchanged; the release must include `Android-GPL-Sources.tar.gz`; the torrent engine's sources are in `Android-Torrent-Sources.tar.gz`.

**Mac, first launch:** macOS says it *could not verify* the app because it's free and not sold through Apple. Click **Done**, open **System Settings → Privacy & Security**, click **Open Anyway**, then **Open Anyway** again and enter your Mac password. Only once; updates installed with **Update Now** open without a warning. [Pictures of each step](https://github.com/Shahrukhalid/nexonic-download-manager#first-launch-on-mac)

**Windows:** if SmartScreen appears, click **More info → Run anyway**.

**Android:** open `NexonicDownloadManager-Android.apk` on your phone and allow installing from your browser or Files app when asked. Older 32-bit phones: `NexonicDownloadManager-Android-32bit.apk`.

**Linux:** `NexonicDownloadManager-Linux-x86_64.AppImage` runs on every distribution (`chmod +x`, then open it); ARM computers: `-aarch64.AppImage`. Ubuntu, Debian, Mint, Pop!_OS: the `.deb`. Fedora, openSUSE: the `.rpm`. Check files with `sha256sum -c --ignore-missing SHA256SUMS-linux-x64.txt`. [Linux guide](https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/LINUX.md)

**Torrents:** torrents use your proxy setting (No proxy, HTTP, SOCKS5 or the system's proxy) and the speed limit for all downloads; DNS over HTTPS is not used for torrents. Peers and trackers can see your IP address (or your proxy's). [Torrent guide](https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/TORRENTS.md)

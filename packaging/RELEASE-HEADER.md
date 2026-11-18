# Nexonic Download Manager 0.36.1

**Mac fixes and an up-to-date Firefox add-on page.** On Mac, the About window no longer opens by itself after downloads and updates, and finished downloads can be dragged straight into Finder or other apps as files. The Firefox add-on is now kept up to date on addons.mozilla.org. Includes 0.36.0 (Android pop-up for shared links).

**Mac, first launch:** macOS says it *could not verify* the app because it's free and not sold through Apple. Click **Done**, open **System Settings → Privacy & Security**, click **Open Anyway**, then **Open Anyway** again and enter your Mac password. Only once; updates installed with **Update Now** open without a warning. [Pictures of each step](https://github.com/Shahrukhalid/nexonic-download-manager#first-launch-on-mac)

**Windows:** if SmartScreen appears, click **More info → Run anyway**. [Code signing policy](https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/CODE-SIGNING.md)

**Android:** open `NexonicDownloadManager-Android.apk` on your phone and allow installing from your browser or Files app when asked. Older 32-bit phones: `NexonicDownloadManager-Android-32bit.apk`.

**Linux:** `NexonicDownloadManager-Linux-x86_64.AppImage` runs on every distribution (`chmod +x`, then open it); ARM computers: `-aarch64.AppImage`. Ubuntu, Debian, Mint, Pop!_OS: the `.deb`; Fedora, openSUSE, RHEL: the `.rpm` (both use the system's .NET 10, see the guide). Check files with `sha256sum -c --ignore-missing SHA256SUMS-linux-x64.txt`. [Linux guide](https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/LINUX.md)

**Torrents:** torrents use your proxy setting (No proxy, HTTP, SOCKS5 or the system's proxy) and the speed limit for all downloads; DNS over HTTPS is not used for torrents. Peers and trackers can see your IP address (or your proxy's). [Torrent guide](https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/TORRENTS.md)

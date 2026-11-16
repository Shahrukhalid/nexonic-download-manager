# Nexonic Download Manager 0.34.0

**Keep files up to date by themselves.** Right-click a finished download → Keep in Sync: Nexonic checks the server every hour, 6 hours, day or week with one tiny request and downloads the file again only when it really changed. The old copy is kept until the new one is complete, then replaced under the same name. Includes 0.33.0 (Import Download List).

**Mac, first launch:** macOS says it *could not verify* the app because it's free and not sold through Apple. Click **Done**, open **System Settings → Privacy & Security**, click **Open Anyway**, then **Open Anyway** again and enter your Mac password. Only once; updates installed with **Update Now** open without a warning. [Pictures of each step](https://github.com/Shahrukhalid/nexonic-download-manager#first-launch-on-mac)

**Windows:** if SmartScreen appears, click **More info → Run anyway**. [Code signing policy](https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/CODE-SIGNING.md)

**Android:** open `NexonicDownloadManager-Android.apk` on your phone and allow installing from your browser or Files app when asked. Older 32-bit phones: `NexonicDownloadManager-Android-32bit.apk`.

**Linux:** `NexonicDownloadManager-Linux-x86_64.AppImage` runs on every distribution (`chmod +x`, then open it); ARM computers: `-aarch64.AppImage`. Ubuntu, Debian, Mint, Pop!_OS: the `.deb`; Fedora, openSUSE, RHEL: the `.rpm` (both use the system's .NET 10, see the guide). Check files with `sha256sum -c --ignore-missing SHA256SUMS-linux-x64.txt`. [Linux guide](https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/LINUX.md)

**Torrents:** torrents use your proxy setting (No proxy, HTTP, SOCKS5 or the system's proxy) and the speed limit for all downloads; DNS over HTTPS is not used for torrents. Peers and trackers can see your IP address (or your proxy's). [Torrent guide](https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/docs/TORRENTS.md)

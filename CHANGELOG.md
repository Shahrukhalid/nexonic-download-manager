## 0.30.1

Fixes for 0.30.0:
- Archives that need a password can now be unpacked: add the passwords under Downloads → Archive Passwords (Windows,
  Linux); each is tried in turn, and a wrong one leaves nothing behind. In 0.30.0 nothing could pass a password.
- Checksum tool (Windows): typing a path no longer starts a calculation for every key; only the latest file's result
  is shown, and closing the window stops the reading.
- Queue countdown (Windows): "Quit" now says that Nexonic quits, not the computer.
- Browser extension: Alt+click lets only the clicked link download in the browser (0.30.0 let every download from
  any tab through for 6 seconds), and it still works when the browser restarts the extension's background script.
- Profile Batch on X uses only X's own public timeline again; the 0.30.0 fallback sent the profile name to an outside
  service.

## 0.30.0

**Checksum verification and file integrity** (Windows)
- Dedicated Checksum verification tool (Tasks → Verify Checksum or right-click any download): calculate MD5, SHA-1,
  SHA-256 and SHA-512 hashes in parallel on any file.
- Automatic algorithm detection from clipboard input (32, 40, 64 or 128 hex digits) with real-time match/mismatch status indicators.
- One-click copy buttons for every computed hash algorithm.

**Queue completion safety alert** (Windows)
- Safety countdown window (30 seconds) before automatic shutdown, sleep or app exit when a scheduled queue finishes.
- Prominent Cancel button to prevent loss of unsaved work when at the computer, with optional immediate action button.

**Browser controls** (Chrome, Edge, Brave, Opera, Firefox)
- Modifier key bypass: hold Alt while clicking any link to bypass Nexonic and download directly in the browser.

**Appearance and archive extraction**
- OLED Black theme: true pitch-black theme (#000000) for OLED/HDR displays and high-contrast environments.
- Password-protected archive extraction (Windows, Linux; reachable from 0.30.1, see there).

## 0.29.0

**Profile Batch** (Windows, Linux, Mac, Android): paste a public profile, channel or playlist link and the app lists its
videos, then downloads them in the quality you choose (Best, 8K to 480p, Original or sound only).
- No login, account, cookies or passwords. A site that only shows its videos to signed-in visitors is reported as such;
  nothing is bypassed, and not every profile on every site can be listed.
- All, New Only (skips what you already downloaded) or Preview first; filters by date, length, keywords, type and
  number; export the list as TXT, CSV or JSON.
- Each profile gets its own folder and queue. An interrupted scan continues where it stopped instead of starting over.
- Save a profile and the app checks it for new videos on a schedule.
- Checked on real public profiles without signing in: YouTube, Vimeo, SoundCloud, Dailymotion and TikTok each listed 40
  videos with none inaccessible, and a scan stopped after 5 continued without repeating them. Archive.org failed in the
  video engine, Reddit refused the request (403) and Instagram needs a login; the app says so instead of failing
  silently.

**Your choice of units, dates and types** (all apps)
- Sizes in binary, IEC (MiB) or decimal units, speeds in bytes or bits, and relative dates ("5 min ago").
- New file types: AI models (GGUF, safetensors), CAD (DWG, STEP), camera RAW (CR3, DNG), LZ4, AppImage and M2TS.
- Sound downloads can be converted to MP3, Opus or FLAC.
- Batch lists (`p[1-20].{jpg,png}`) with a link filter (`*.jpg !*thumb*`) before anything is added.

**Also new**
- Move the data folder (settings, history, unfinished parts) to another drive: it is copied and checked, and moved at the
  next start (Windows, Linux, Mac).
- Find downloads whose file was deleted or moved, and clear them from the list.
- Resumed downloads get their own slots, so they don't wait behind new ones.
- Your own colour scheme with a contrast check (Mac, Android); RSS feeds with rules, debrid accounts (your own key, off
  by default) on Mac and Android; AllDebrid for torrents.
- Mac: graphical category editor, app font and text size, your own yt-dlp and FFmpeg, share ratio and uploaded columns.
- Android: notification sounds, vibration and quiet hours, font and text size, graphical categories.
- Linux: list columns can be moved.

**Fixes from your reports** (#199)
- Mac: toolbar buttons take clicks on their whole area, not only on the icon.
- Mac: option to keep Nexonic in the menu bar without a Dock icon when its window is closed.
- Mac: the browser helper is registered for Arc, Opera, Opera GX, Thorium and the Beta and Dev channels of Edge and
  Brave.
- Browser extension: choose the video button's corner, size, colour, and text or arrow only.

## 0.28.0

What this release was measured on: faster verified downloads, lower resource use, a more reliable app and smaller
installs. Every number below compares 0.27.0 with 0.28.0 on the same machine.

**Faster downloads, every file checked**
- Automatic connections (on by default, all apps): each download starts with 4 connections and doubles them every
  second while the speed keeps rising, up to your setting (now up to 256). A server that answers "too busy" (429/503)
  is given the time it asks for, and the app never climbs back to the number it refused. A refused part waits; nothing
  is lost or written twice.
  On a local test server, every file checked against its SHA-256: 120 MB from a server sending 1 MB/s per connection
  took 15.1 s with 0.27.0 and 6.0 s now; from a server that refuses more than 6 connections, 16.3 s and 15.2 s. Without
  a speed limit (1 GB) speed, CPU time and memory are the same as before.
- Several sources for one file: extra mirror addresses or a Metalink (.meta4) file. Each part comes from the source
  measured fastest so far; a mirror that sends a different file is dropped before it can damage yours.
- Mac: downloads really use more than 32 connections now (the setting offered 256, the engine stopped at 32).

**Smaller installs** (release files of 0.27.0, CI builds of 0.28.0)
- Android: 60.3 MB to 30.0 MB (32-bit: 53.0 MB to 27.4 MB). The video engine's FFmpeg is now a small build made from
  source that only re-packs video; phones that update also get back the 78 MB the old one took after unpacking.
- Linux AppImage: 50.9 MB to 46.1 MB (Arm: 48.5 MB to 43.7 MB). Updates of the AppImage download only the parts that
  changed (a .zsync file comes with every release); anything unexpected falls back to the full file, always checked.
- Windows installer: 55.8 MB to about 47 MB. Windows Forms and the .NET debugger files are no longer installed.
- New: Windows on Arm (its own installer and portable zip, built and tested on an Arm machine).

**More reliable**
- Chapters and subtitles go into saved videos again: the bundled FFmpeg (Windows, Linux, Mac) could not read them, so
  the video engine stopped with an error after downloading. Checked with the real engine on every build.

**Also new**
- Encrypted DNS over TLS as well as HTTPS, with a strict mode that never falls back to unencrypted lookups.
- Server panel: the server's country (from a database on your computer; addresses are never sent anywhere), its HTTP
  version and a measured speed.
- Speed schedule by time of day (Windows, Linux, Mac, Android): "Mon-Fri 09:00-17:00 300".
- cURL: paste a browser's "Copy as cURL" into Add URL (address, referrer, cookies, login), and Copy as cURL on any
  download (all apps).
- A link that is already in the list (or downloaded, with the file still there) is pointed out before it is added.
- RSS rules: size limits, one download per episode in the quality you prefer, and a preview of what a rule takes.
- Torrents through your own debrid account (Real-Debrid, Premiumize; off until you add a key).
- Colour schemes (contrast-checked, import and export), your choice of font, sounds for finished, failed and queue
  done with quiet hours, a saved column layout, and a category list you can reorder (Windows and Linux).
- Wake the computer to start a queue on Linux (where the system allows it) and Mac; Android can be the default app for
  web links.
- Your own yt-dlp and FFmpeg can be chosen in Options (Windows and Linux).

## 0.27.0

- **Remote management is back** (Windows, Linux, server): manage downloads from a phone or another device on your
  network. Off until you turn it on in Options → Remote; a phone pairs by scanning a QR code, and every request needs the
  pairing key (wrong keys are slowed down, other sites' pages are refused). Use it on trusted networks only.
- **Headless server and Docker:** `nexonic-server` runs the download manager without a window, for a NAS or a home
  server; the Docker image wraps it. See docs/SERVER.md.
- **Custom categories on every app** (Windows, Linux, Mac, Android): give a category an icon and sites
  (`📚 Books: epub pdf > Books`, `Releases: zip @ github.com/*/releases > Releases`). Sites include their subdomains and
  `*` matches anything; a category with sites and no types takes every file from those sites. Categories are checked in
  your order, so reordering and renaming is editing the lines. On Windows and Linux they also appear in the sidebar with
  their counts.

## 0.26.0

- **Smaller downloads:** the video engine is no longer in the installers. The app downloads its official release the
  first time you save a video (checked against its published SHA-256 list) and keeps it current. The Linux download went
  from 204 MB to 78 MB.
- **Download limit** (all apps): at most N MB every H hours. When it is reached, downloads stop and continue by
  themselves when the next period starts (useful on mobile data or capped connections).
- **Proxy scripts (PAC)** on Windows, Linux and Mac: a script from a web address or a file decides the proxy for each
  address. On Windows and Linux it runs in the bundled script engine with no file, program or module access.
- **Link Grabber** (Windows, Linux and Mac): paste text with links or a web page address; links are grouped into
  packages (the parts of a split archive together), each in its own folder, and unpacked when all of it is done. On
  Android and iPhone, Add finds the links in any pasted text.
- **Whole galleries** (Windows, Linux and Mac): save every picture of a gallery or profile on sites like Reddit, Pixiv,
  DeviantArt and Imgur. The gallery engine is downloaded the first time it is used.
  Galleries that need you signed in work too: the site's cookies and your login are passed to it privately, never on
  the command line.
- **List columns:** Finished, Folder and Can Resume, shown or hidden from View → Columns (on Mac, right-click a column
  title). The Add windows start in the folder you used last time.
- **Appearance:** Light, Dark or the same as the system, now also on Windows, Mac, Android and iPhone.
- **Unpacking:** split archives wait until every part is downloaded; on Mac, .7z, .rar and .tar unpack too.
- **Queues:** drag downloads onto a queue in the sidebar to move them; Start/Stop All Queues and Open Downloads Folder
  in the tray and menu bar; Windows can wake the computer from sleep to start a queue.
- **Command line:** `ndm` accepts IDM-style switches (`/d`, `/p`, `/f`, `/a`), and `ndm list --json` prints the list
  for scripts (no cookies or passwords).
- **Crash reports** are saved on your device (never sent anywhere); Help → Crash Reports opens them.
- **Check Server…** in a download's menu checks the link step by step; **several QR codes** when the selected links
  don't fit in one; **portable mode** with a `portable.txt` file next to the app.
- **110 languages** on every app. 15 are reviewed; the others are machine translations, marked as such.
- Dates in the list follow your region. The browser helper's server mode, phone pairing and Docker image were removed.

## 0.25.0

- **Remote management (Windows and Linux, off by default):** Options → Remote, then Pair a Phone. The phone's camera opens
  a page that lists, adds, stops, resumes and removes downloads. Only paired devices get in (the key travels in the
  code, never in a request line), wrong keys are slowed down, and Unpair All Devices makes a new key.
- **Server for NAS and Docker:** `nexonic-server` runs without a window and prints the pairing code; `docker/` has the
  image and a compose file (docs/SERVER.md).
- **Download Images from Page** (Tasks menu, Ctrl+Shift+I): the pictures of one page, full-size where it links to them,
  saved in a folder named after the page; the page is sent as the referrer.
- **Your own categories** (Options → Categories): one per line, `Name: types > folder`; they come before the built-in
  types.
- **Check Torrent Files Again** in a torrent's menu: every piece on disk is checked and damaged ones are downloaded again.
- **Network Diagnostics** (Help menu): name lookup, connection, secure connection and the server's answer for one link,
  with your settings. **Internet addresses** (Options → Connection): IPv4 and IPv6, IPv4 only or IPv6 only.
- **Interface size** (Options → General, 75 % to 200 %), and names for screen readers on the toolbar, search box, lists
  and new windows.
- **Smaller downloads:** the Mac disk image uses LZMA and the Linux .rpm xz; the Linux app leaves out unused language
  files, the Android app keeps only its own 15 languages of the libraries' texts, and the Mac app is compiled for size.
- **Browser stores:** `scripts/build-extensions.py` also makes the Chrome Web Store, Edge Add-ons and addons.mozilla.org
  uploads (build/store/), and `npm test` checks the stores' rules (docs/STORES.md).

## 0.24.0

- **RSS feeds with download rules** (Windows and Linux, Tasks → RSS Feeds): add an RSS or Atom feed, the words its
  titles must have (or a /regular expression/) and the words to skip. New matching items are downloaded by themselves:
  files and videos, and torrents (magnet or .torrent) when torrents are on. Each feed has its own folder and check
  interval; items already in the feed when you add it are skipped unless you choose otherwise.
- **More archives unpack when finished:** .7z, .rar (also split .part1.rar sets) and .tar, .tar.gz, .tar.bz2 and .tar.xz,
  besides .zip (Downloads → Unpack Finished Archives). Password-protected archives are left alone, files never land
  outside the new folder and nothing is overwritten.
- **Debrid account (optional, off by default):** Options → Connection. Choose Real-Debrid, AllDebrid or Premiumize, enter
  your own API key and the sites whose links should go through it; only those links are sent to the service, and a fresh
  link is asked for on every resume. The key is saved encrypted and never in backups.
- **Limit downloads while you use the computer:** Options → Connection. Full speed after a minute without keyboard or
  mouse input (Linux: X11 and XWayland desktops).
- **Open With…** (Windows) in the download's menu.
- **MCP server for assistant apps:** `ndm --mcp` (see docs/API.md). Adding downloads always works; listing, pausing,
  resuming and removing use the local automation API and its key.

## 0.23.0

- **Android: Scan QR code** on the Downloads screen. Scan the QR code the Mac, Windows or Linux app shows for a
  download (or any QR code with a web link or magnet link) and it opens in the Add window. The camera is asked for
  the first time you use it; phones without a camera can still install the app.
- **Android: torrent blocklist** (Settings): an https:// address with a P2P, DAT or CIDR list (also `.gz`), downloaded
  again once a day. Torrents never connect to those peers or trackers.

## 0.22.0

- **Mac: network adapter and torrent blocklist** (Settings → Connection). Torrents and videos use only the chosen
  adapter's address; regular downloads stop within a second when it disconnects instead of continuing on another
  connection. The blocklist takes a file or an https:// address (P2P, DAT or CIDR, also `.gz`).
- **Android: VPN only** (Settings). Downloads, videos and torrents go only through the VPN, bound to it, and stop when
  it disconnects; they continue by themselves when it is back. A note on the Downloads screen says when they wait.

## 0.21.0

- **Keep downloads on your VPN (Windows and Linux):** Options → Connection → Network adapter. Downloads, videos and
  torrents then use only that adapter, and if it disconnects they stop instead of using another connection (a kill
  switch). DNS over HTTPS lookups go through the same adapter. Check the Connection shows whether it is connected.
- **Torrent blocklist (Windows and Linux):** Options → Connection → Torrent blocklist takes a file or an https://
  address with P2P, DAT or CIDR lines (also `.gz`). Torrents never connect to those peers or trackers; a list from an
  address is downloaded again once a day, and the last copy is used when the server can't be reached.
- **Share links download the file itself** on every app: Google Drive file links, Google Docs, Sheets and Slides (as Word, Excel and PowerPoint files), Dropbox share links and GitHub
  file pages.

## 0.20.2

- **The language changes at once:** picking a language in Settings (Options on Windows and Linux) switches every
  open window and menu right away, with no restart. Arabic and Urdu switch the layout to right-to-left as well.
- **Mac: about 90 more texts translated** into all 15 languages (Settings, torrents, playlists, the Media Library, the
  toolbar menus and the QR window), so these screens no longer show English.

## 0.20.1

- **Support the project:** a "Support the Project" item in the Help menu (Mac, Windows, Linux), in the About window,
  and in Settings → About on Android, iPhone and iPad. It opens the Patreon page; every app stays free.

## 0.20.0

- **Seven more languages:** Chinese (Simplified and Traditional), Japanese, Korean, Russian, Indonesian and Turkish,
  on Windows, Linux, Mac, iPhone, iPad and Android. English stays the default.
- **All 15 languages reviewed:** every translation was checked and corrected (for example Pause and Stop, and Resume
  and Restart, are now different words in Hindi and Urdu), and the "machine translation" labels are gone
  ([docs/TRANSLATIONS.md](docs/TRANSLATIONS.md)).
- **Pick the language in every app:** Mac Settings → General → Language (with Restart Now), iPhone and iPad
  Settings → Language, Android Settings → Language (applies right away). Windows and Linux keep Options → General →
  Language.
- **The browser extension is translated** into all 15 languages: its menu, right-click items and the "Download this
  video" button. Choose a language in the extension's Settings, or keep the browser's.
- Android now follows the system's language for every translated language (it was English only on some phones).
- Chinese follows the region: Traditional for Taiwan, Hong Kong and Macau, Simplified elsewhere.

## 0.19.2

- **iPhone and iPad app for sideloading:** every release now includes `NexonicDownloadManager-iOS-unsigned.ipa`.
  Install it with AltStore or Sideloadly; a free Apple ID re-signs it every 7 days. The App Store version needs a
  paid Apple developer account and is not available yet.
- **Website:** Mac and Windows screenshots are shown large with a row of thumbnails; dialog thumbnails are zoomed so
  they are readable.
- Build tools updated (.NET and Java setup, SBOM action, the extension's fuzz-test library).

## 0.19.1

- **Add Torrent works the same everywhere (Windows and Linux, like the Mac).** The torrent's name, size and files load
  by themselves as soon as it is opened, dropped, chosen or pasted; there is no Load metadata button. If torrents are
  off, a "Turn on torrents" box at the top loads the torrent when ticked. Problems are shown right under the link
  instead of at the bottom of the window.
- **Start Download is always on screen:** speed limits, connections, seeding and trackers are folded into More
  options.
- **Drop a `.torrent` file onto the main window** to open it in Add Torrent (Windows, Linux and Mac).
- Windows: the About window shows a sharp icon. About lists every platform the app runs on.

## 0.19.0

- **Big downloads finish instantly (Mac, Windows, Linux).** Every connection now writes its part straight into one
  file in the save folder, shown as `name.nxpart` while it downloads. When the last byte arrives the file is only
  renamed, so there is no slow "joining" step at the end, and a 5 GB download no longer needs 10 GB of free space.
- **Checks before a big download starts:** if the drive doesn't have enough free space, or is formatted as FAT32 and
  can't hold a file over 4 GB, the download stops at once with a clear message instead of failing at the end.
- **Downloads started with an older version** still finish: their pieces are joined with a "Joining parts… %"
  message, and if that fails the half-written file is removed and the pieces are kept so Resume can try again.
- Canceling or removing an unfinished download also deletes its `.nxpart` file.
- Browser extension: suggested file names no longer turn `%2F` into "/", drop control characters, and never use "."
  or ".." as a name.
- New code signing policy and privacy policy ([docs/CODE-SIGNING.md](docs/CODE-SIGNING.md)).

## 0.18.1

- **The video engine runs under the app's own name** (`nexonic-video-engine`) on Mac, Windows and Linux, so that is
  what Activity Monitor, Task Manager and the app's folder show. The engine's messages use plain wording, and the
  third-party notices list it as "the video engine", with every licence of its bundled parts kept in full.
  Nothing else changes: the engine moves to its new folder by itself the first time the app opens.
- The torrent engine builds without compiler warnings on every platform.

## 0.18.0

- **Windows: videos start sooner.** The video engine now ships unpacked. Before, it unpacked itself before every
  video and Windows scanned it each time. It still keeps itself up to date once a day, checked against the official
  fingerprints. Sites and qualities are unchanged.
- **Windows and Linux: Add Download finds the qualities.** Paste one video link and the window looks it up while it
  is open: pick Best, a lower quality (1080p, 720p…) or Sound only and see the size. Start then begins at once.
- **Windows and Linux: Cancel keeps the download in the list** as Canceled instead of removing it. What was
  downloaded is deleted; Resume starts it again. (Torrents are still removed; their files stay on disk.)
- **Browser extension finds more streamed videos (#108).** Players that load their stream from an address without a
  file extension, or from a background worker (common on streaming sites), are now detected while the video plays,
  and the browser hands the video over with your session. Sites that check for a real browser now get a clear
  message instead of a raw error when the app is asked to open the page by itself.
- **Windows: double-click a download that isn't finished** to open its progress window; a finished one still opens
  its file (as on Linux and Mac).

## 0.17.5

- **Mac: smaller download again, same fast video start.** The video engine is packed small inside the app (about
  26 MB instead of 52) and unpacked once the first time the app opens after installing or updating. Sites,
  qualities, speed and updates are unchanged.

## 0.17.4

- **Mac: videos start much sooner.** The video engine now ships unpacked. Before, it unpacked itself on every run and
  macOS checked it each time, which added about 18 seconds before every video and before the quality list appeared.
  Now it starts in a fraction of a second (macOS checks it once, right after installing or updating). It still keeps
  itself up to date once a day, checked against the official fingerprints. Sites and qualities are unchanged.

## 0.17.3

- **Mac: Cancel keeps the download in the list** as Canceled (like IDM) instead of removing it. What was downloaded
  is deleted; Resume starts it again from the beginning.
- **Mac: double-click a download that isn't finished** to open its progress window; a finished one still opens its
  file.
- **Mac: video links start faster and show their qualities.** Paste one video link in Add Download and the app looks
  it up while the window is open: pick Best, a lower quality (1080p, 720p…) or Sound only, and see the size. Start
  then begins downloading at once instead of looking the video up again.

## 0.17.2

- **Mac: a smaller download.** The app and its browser helper ship without build-time symbol tables, as apps built
  with Xcode do. Nothing else changes: settings, downloads, torrents and updates work as before.

## 0.17.1

- **Smaller downloads, same features.** The Android apps no longer carry the torrent engine's build-time debugging
  information, and on Windows the browser helper and `ndm` command now share the app's .NET runtime instead of
  bundling a second copy (as on Linux). The Linux torrent engine is smaller too. Nothing else changes: settings,
  downloads, torrents and their progress stay where they are, and updating works as before.

## 0.17.0

- **Android: playlists and channels.** Tap the playlist button on the Downloads screen, or share or paste a YouTube
  playlist or channel, Vimeo showcase, SoundCloud set or Bandcamp album link. Nexonic lists its videos (up to 500);
  tick the ones you want and they open in the Add window.
- **Android: subtitles** (Settings › Save subtitles with videos, off by default): videos keep the subtitles the site
  offers in your phone's language and in English, inside the file. A missing subtitle never stops the video.
- **Media Library on Android, iPhone and iPad:** your finished videos and music with preview pictures; tap one to play
  it. Android: the library button on the Downloads screen. iPhone and iPad: Downloads › More › Media Library, with
  search, Share and Show in Files.

## 0.16.0

- **Playlists and channels** (Mac, Windows and Linux): Tasks › Add Playlist or Channel… (Ctrl+Shift+L, ⇧⌘L on Mac)
  lists the videos of a YouTube playlist or channel, a Vimeo showcase, a SoundCloud set, a Bandcamp album and more (up
  to 500). Tick the ones you want and they open in the Add window. On Windows and Linux, a playlist or channel link
  pasted into Add URL opens this window by itself.
- **Subtitles** (Mac, Windows and Linux; Downloads › Save Subtitles With Videos, off by default): videos keep the
  subtitles the site offers in your computer's language and in English, inside the file. A missing subtitle never
  stops the video.
- **Media Library** (Downloads › Media Library…, Ctrl+L, ⌘L on Mac): your finished videos and music in one window,
  with preview pictures, search, Videos/Music filter, Play and Show in Folder.

## 0.15.0

- **Videos keep their details and chapters** (Mac, Windows, Linux and Android): saved videos and sound files now
  carry the title, channel or artist, date and description, and the video's chapters, so players show chapter marks
  and media libraries list them properly.

## 0.14.0

- **Windows and Linux: a toolbar like IDM's.** Large coloured buttons with their names, in IDM's order: Add URL,
  Torrents, Resume, Stop, Stop All, Select All, Delete, Delete Completed, Scheduler, Start Queue, Stop Queue, Grabber,
  Batch and Options. Resume, Stop and Delete are greyed out until downloads are selected.
- **Windows and Linux: menus like IDM's.** Tasks, Downloads, View and Help, with shortcuts: Ctrl+R resume, Ctrl+.
  stop, Ctrl+Shift+R resume all, Ctrl+Shift+P stop all, Ctrl+O open, Ctrl+Shift+T add torrent, Ctrl+Shift+B batch,
  Ctrl+Shift+G grabber. View › Show Toolbar Buttons and Show Button Names hide the buttons or just their names.
- **Unzip when finished** (Mac, Windows and Linux; Downloads › Unzip Finished .zip Files, off by default): a finished
  `.zip` download is unpacked into a new folder next to it ("Photos", then "Photos (2)"…). Entries that would land
  outside that folder are skipped, and archives that unpack to more than 64 GB are left alone.

## 0.13.0

- **Torrents on Android.** Open a magnet link or a `.torrent` file (or share one to Nexonic), choose the files, and the
  torrent downloads in the background with the other downloads, showing seeds, peers and speeds. Every piece is
  checked; finished files go to your Downloads folder. Torrents stay off until you turn them on; phones don't keep
  sharing (seeding) after the download.
- **Torrents on iPhone and iPad.** Add a magnet link or a `.torrent` file from the Downloads menu, Files or another app.
  Torrents download while Nexonic is open, pause with a checkpoint when it goes to the background and continue when
  you come back. Finished files are in Files › Nexonic › Torrents.
- Android releases now also include `Android-Torrent-Sources.tar.gz`, the exact sources the torrent engine was built
  from.

## 0.12.0

- **Windows and Linux: torrent details like a BitTorrent client.** The Torrent Manager shows downloaded and uploaded
  amounts, speeds, seeds and peers (connected and in the swarm), connections, pieces, availability and time active.
  New **Peers** (IP, client, flags, progress, speeds) and **Trackers** (status, seeds, peers, Update Tracker) windows.
- **Windows and Linux: Torrent Settings take effect right away** on a running torrent: download and upload limits,
  connections per torrent, upload slots, download pieces in order, seeding, trackers and file priorities.
- **Windows and Linux: Torrent Network settings:** router port forwarding (UPnP and NAT-PMP, off by default) and
  encryption.
- **Windows and Linux: open torrents from anywhere.** Double-click a `.torrent` file or click a magnet link and the
  Add Torrent window opens (Windows registers them for the app unless another program already handles them; it is
  always listed under "Open with").
- **Faster torrents:** new torrents no longer cap uploading at 32 KB/s. Peers send faster to those who upload, as in
  other BitTorrent clients. Set an upload limit per torrent if you need one.

## 0.11.4

- **Mac: a toolbar like IDM's.** Large coloured buttons with their names: Add URL, Torrent, Resume, Stop, Stop All,
  Select All, Delete, Delete Completed, Scheduler, Start Queue, Stop Queue, Grabber, Batch and Options. Turn the
  buttons or their names off under View.
- **Mac: menus for every command.** File has Add URL, Add Torrent, Add Batch Download and Site Grabber; a new
  Downloads menu has Resume, Stop, Resume All, Stop All, Open, Show in Finder, Properties, Delete, Delete Completed,
  Scheduler and the queue commands, with keyboard shortcuts.

## 0.11.3

- **Fixed: torrents wouldn't load** with the message "Torrents require No proxy". The default proxy setting ("Use the
  system settings") blocked every torrent. Torrents now follow your proxy setting instead: No proxy connects
  directly, an HTTP or SOCKS5 proxy from Settings carries peer, tracker and DHT traffic, and system settings use your
  computer's proxy when one is turned on. Mac, Windows and Linux.
- The speed limit for all downloads now also caps each torrent, instead of stopping torrents.

## 0.11.2

- **Mac: open torrents from anywhere.** Double-click a `.torrent` file in Finder or click a magnet link in your browser
  and Nexonic opens it in the Add Torrent window. A torrent pasted into Add URL goes there too, so you see its files
  before downloading (before, it was added as a plain download and disappeared).
- **Mac: torrent details like a BitTorrent client.** Select a torrent for General (pieces, downloaded and uploaded,
  speeds, seeds and peers connected and in the swarm, ratio, availability, connections, time active), Trackers
  (status, seeds, peers, Update Tracker), Peers (IP, client, flags, progress, speeds) and Files. The list has a
  Seeds / Peers column.
- **Mac: Torrent Settings** (right-click): download and upload limits, connections per torrent, upload slots, download
  pieces in order, seeding, trackers and file priorities. A running torrent takes the changes right away.
- **Mac: Settings → Torrents:** router port forwarding (UPnP and NAT-PMP, off by default) and encryption.
- Torrent engine (all platforms): peer list, tracker status, swarm and piece details, connection limits and tracker
  update. Settings, download history and update compatibility are unchanged.

## 0.11.1

- Everything in 0.11.0 below (torrents on Mac). 0.11.0 was not published: a source server failed while its Android
  source archive was being built.
- Android source archive: every downloaded source is now verified (recipe checksum, or a real archive rather than an
  error page), downloads retry, and FFmpeg and v4l-utils have backup sources.

## 0.11.0

- **Torrents on Mac:** the same built-in engine as Windows and Linux. Click **Torrents** in the toolbar, turn torrents
  on, and add a magnet link, a link to a `.torrent` file or a `.torrent` file from your Mac. Preview the files first,
  choose Skip/Low/Normal/High for each, pick a folder and queue, set download/upload limits, and optionally keep
  seeding until a ratio or time limit.
- Torrents use the existing queues and scheduler on Mac; seeding doesn't take a download slot. Every piece is checked
  and each finished file gets a SHA-256. Torrents stay off until you turn them on.
- Limits: proxies, the shared global speed limit and DNS over HTTPS are not used for torrents; peers and trackers can
  see your IP address. Android and iPhone/iPad torrent support is still in development and not part of this release.
- Application identifiers, settings, download history and update compatibility are unchanged.

## 0.10.0

- **Torrents on Windows and Linux:** a built-in torrent engine (libtorrent 2.0.11) replaces the optional Linux Python
  bridge; nothing extra to install. Open **Torrents → Add Torrent** and add a `.torrent` file, an HTTP(S) torrent link or
  a magnet link. Torrents stay off until you enable them.
- Preview the name, hash and files before downloading; choose Skip/Low/Normal/High per file, a destination and a queue.
- Progress, peers, seeds, speeds, ETA and ratio; pause/resume, per-torrent speed caps, DHT, tracker edits and optional
  seeding with ratio/time limits (off by default).
- Every piece is checked during the download and again before completion, then each selected file gets a SHA-256.
  Checkpoints survive restarts and crashes; removing a torrent keeps its files.
- Fixed: a damaged download list could be backed up more than once on Mac and Windows.
- Limits: system/custom proxies, the shared global speed limit and DNS over HTTPS are not used for torrents (they pause
  instead of bypassing your settings). Peers and trackers can see your IP address. Mac, Android and iPhone/iPad torrent
  support is still in development and not part of this release.
- Application identifiers, settings, download history and update compatibility are unchanged.

## 0.9.1

- Replace application artwork with the official white tile, gradient N, orbital swoosh and blue download arrow on Windows, macOS, Linux, Android, iPhone/iPad and browser extensions.
- Generate platform sizes and formats from the owner-supplied master, with cropped small icons, adaptive Android icons and explicit window/menu-bar resources.
- Install every Linux launcher size and refresh existing icon files during integration updates.
- Keep application identifiers, settings, download history, data locations and update compatibility unchanged.
- Restrict screenshot publishing to main; review builds produce artifacts only.

Torrent functionality is unchanged from v0.9.0. Native all-platform torrent development remains under review and is not part of this release.

## 0.9.0

- **New icon:** the approved Nexonic design across the desktop and mobile apps, installers and browser extensions.
  Application identifiers, existing data folders and update channels are unchanged.
- **Linux, experimental torrents (off by default):** preview a local `.torrent` file or magnet link, review its files,
  choose Skip/Low/Normal/High priorities, and download through the existing queues and scheduler.
- **Torrent resume:** native piece checkpoints persist between runs; missing or unusable checkpoints fall back to
  piece checks. Stop allows the worker to save its state. Removing a torrent retains its downloaded and partial files.
- **Optional Linux setup:** install system `python3-libtorrent` (2.x) for `/usr/bin/python3`. Ordinary file/video downloads
  keep working without it, including in the AppImage. Peers and trackers can see your IP address.
- **Initial torrent limits:** Linux only, v1 torrents only, no continued seeding after completion. System/custom proxy,
  shared global speed-limit and DNS-over-HTTPS configurations are refused by the torrent provider; choose No proxy,
  disable those shared settings, and use a per-torrent download cap. Torrent history is excluded from export/import
  backups. See [the torrent guide](docs/TORRENTS.md).
- **Linux download engines:** automatic source detection and a manual File/HLS/Video/Torrent choice reuse the existing
  HTTP, streamed video and video-site engines. Browser/CLI/local API additions remain HTTP(S)-only.
- Existing HTTP/video features, saved download progress, credentials and queues are preserved when upgrading.
- Full seeding, tracker management and new playlist/gallery/media features are still in development. Other platforms
  receive the icon update; this release does not add torrents to Windows, Mac, Android or iPhone/iPad.
- Android retains its GPL-3.0 distribution notices and required corresponding-source archive. Translations remain
  marked for human review.

## 0.8.1

- **Linux:** fixed a crash when adding a second download, creating a queue, or rebuilding the downloads list.
- **Linux:** the app now starts when its data folder has a long path.
- **Linux checks:** every build exercises the packaged app with real downloads, file integrity, pause and resume,
  command-line queues, local API security and the bundled video tools on both Intel/AMD and ARM computers.
- Clearer Linux crash reports and local API error logs help diagnose problems.
- No new user-facing features on Mac, Windows, Android, iPhone/iPad or the browser extension.

## 0.8.0

- **Site settings:** a login, cookies, user agent, proxy, connection count and speed limit per website (subdomains
  included). Logins are kept encrypted and sent only to that site. Windows, Linux and Mac.
- **Backup:** export and import settings, site settings, queues and the downloads list. Passwords and cookies are
  never put in a backup.
- **Network options:** reconnect after silent seconds, tries per part, DNS-over-HTTPS (Windows, Linux) and a network
  check.
- **Download on your phone:** Show QR Code on any download in the Mac, Windows and Linux apps; scan it and the Android
  app adds the download. Only the link is shared, never cookies or passwords.
- **Android:** bookmarks in the built-in browser, a choice of search engine, save to an SD card or USB folder, and
  separate switches for finished and failed notifications.
- **iPhone and iPad:** Pause, Resume and Status actions for Shortcuts and Siri; Command-N adds a download on iPad.
- **Browser extension:** keep chosen sites in the browser, pause catching downloads for a while, clearer badge.
- **Tray:** a Speed Limit menu; the main window remembers its size and place.
- **Automation (optional, off by default):** a local API on Windows and Linux for scripts, reachable only from this
  computer and protected by a key. See [docs/API.md](docs/API.md).
- **Languages:** Arabic, German, Spanish, French, Hindi, Portuguese and Urdu on every app (Arabic and Urdu right to
  left). These are machine translations waiting for review by native speakers; see
  [docs/TRANSLATIONS.md](docs/TRANSLATIONS.md).
- **Packages:** Scoop bucket, winget and Homebrew manifests ([docs/PACKAGES.md](docs/PACKAGES.md)).
- **License:** the project is now available under MIT OR Apache-2.0, at your option.
- **Android licenses:** the Android app's video engine is GPL-3.0, so the app as a whole is distributed under GPL-3.0.
  Settings → About → Open-source licenses lists every component, and each release now includes
  `Android-GPL-Sources.tar.gz` with their source code.
- **Safer:** importing a backup never takes passwords or the automation switch from the file; QR codes drop a user
  name and password from links and warn when a link looks private (a token or signature); the local API accepts a
  few connections at a time.
- Removed the unused video server for the old iPhone shortcut.

## 0.7.0

- **New: Linux app.** The same download manager as on Windows: fast multi-connection downloads that pause and resume,
  queues and scheduler, batch download, site grabber, videos from YouTube and 1,000+ sites with quality choices,
  clipboard watching, tray icon, notifications, light and dark mode. Works with Chrome, Chromium, Edge, Brave,
  Vivaldi, Opera and Firefox, and adds the `ndm` command.
- Linux downloads: an AppImage for every distribution (updates itself), `.deb` for Ubuntu, Debian, Mint and Pop!_OS,
  `.rpm` for Fedora and openSUSE, each for 64-bit Intel/AMD and ARM. Passwords and cookies are kept encrypted with
  your desktop's keyring. See the [Linux guide](docs/LINUX.md).
- Android: videos are added right away (the page is read in the background), real progress with size and speed for
  videos, and long-press to select several downloads to pause, start or delete.
- Mac and Windows apps are unchanged.

## 0.6.10

- **Android: video downloads work.** In 0.6.9 every video link stopped with an "s2.e" message because a part of the
  video engine was removed from the app by mistake. Fixed, and every build is now checked for it.
- Android: Facebook share links (facebook.com/share/…, fb.watch) open the right video.
- Android: videos that need you to be signed in (some Instagram and Facebook videos) work after you sign in once in
  the app's Browser tab.
- Android: clear messages when a video can't be saved, instead of technical codes.
- Mac and Windows apps are unchanged.

## 0.6.9

- **Android: videos from YouTube, Facebook, Instagram, TikTok, X and 1,000+ other sites.** Share or paste a video
  link and pick the quality (up to 4K, or sound only). Before, these links saved the web page instead of the video.
  It uses the same video engine as the Mac and Windows apps, which updates itself once a day.
- Android: **Download this video** button in the built-in browser on video pages.
- Android: two downloads now. `NexonicDownloadManager-Android.apk` fits almost every phone (64-bit);
  `NexonicDownloadManager-Android-32bit.apk` is for older phones.
- Mac and Windows apps are unchanged.

## 0.6.8

- **New: Android app.** Built-in browser that finds the videos, music and files on a page, Share → Nexonic from any
  app, fast multi-connection downloads that resume, queue and schedule, Wi-Fi only, and notifications. Download
  `NexonicDownloadManager-Android.apk` below (Android 10 or later).
- **iPhone and iPad app (in testing):** Download with Nexonic in the Share sheet, a Shortcuts/Siri action, background
  downloads and Files app folders. Not on the App Store yet.
- Mac and Windows apps are unchanged.

## 0.6.7

- **Clear message when a video needs you to be signed in** (for example some Vimeo videos): instead of the video
  engine's technical text, the app explains what to do: open the video in your browser while signed in and click
  Download this video, or enter your account under Properties → Login.

## 0.6.6

- **Select All** button in the toolbar (click again to deselect).
- **Asks before deleting:** Delete, Delete and Move to Trash, and Delete Completed now ask first. Removing from the
  list keeps the files unless you tick "Also move the downloaded files to the Trash".
- **Download this video** in the browser shows *Video, best quality* and *Sound only* at once again (no waiting).
  The app's download window still lets you pick another quality.
- **Categories like IDM:** files are sorted by type automatically. All Downloads, Unfinished and Finished each open
  into Compressed, Documents, Music, Video, Programs, Images and General, and each type is saved in its own folder
  (Downloads/Video, Downloads/Music…; General files stay in Downloads). On for everyone; turn it off in Settings.
- **Pasted image links from Google Images** now download the picture itself instead of Google's page (also Google
  result links, Facebook and Reddit redirect links). Several links pasted on one line, separated by commas, are added
  one by one.
- **Updates:** only one update can run at a time. Checking for updates while one is downloading shows its progress
  instead of starting a second download.

## 0.6.5

- **Update checks work on busy networks:** GitHub allows only 60 update checks an hour per network, so on shared
  office or mobile networks "Check for Updates" could fail with "Check your internet connection". The app now
  falls back to a copy of the update details attached to every release (still verified by SHA-256 before
  installing).
- **First launch on Mac:** step-by-step pictures for allowing the app the first time (README, website, release
  notes and Help).

## 0.6.4

- **No more Keychain password prompts on Mac:** after an update, macOS no longer asks for your login password when
  the app starts. Cookies, download passwords and the proxy password are now kept in a private file that only your
  user account can read (`secrets.json` in the app's Library folder, protected on disk by FileVault), never in the
  download lists. Values saved by 0.6.2/0.6.3 are moved over when macOS allows it without asking; otherwise
  re-enter the proxy password once in Settings.
- The "Passwords and cookies can't be saved in the Keychain" message is gone.

## 0.6.3

- **Pick the video quality:** the Download this video button and the download window list the qualities each video
  is offered in (for example 2160p (4K), 1080p, 720p, 480p), with the approximate size, plus Sound only. The list is
  read from the video itself; the best one is chosen by default. The downloads list shows the chosen quality.

## 0.6.2

- **Security:** cookies and logins are only ever sent to the download's own server (not to video pieces on other
  hosts, not after a redirect to another host or from https to http). Logins answer only the original server.
- **Secrets at rest:** cookies, download passwords and the proxy password are kept in the macOS Keychain or
  encrypted for your Windows user (DPAPI). Lists saved by older versions are moved over on first launch.
- **Video engine:** uses the proxy from Settings; cookies, logins and the proxy are no longer visible in the list of
  running programs.
- **Updates:** only this app's own release file is installed, and only if it matches the SHA-256 GitHub publishes;
  on Mac the new app's identity and signature are checked first.
- **Reliable resume:** a file that changed on the server is downloaded again from the start instead of being mixed
  with the old parts; every part the server sends is checked; parts that make progress get fresh retries.
- **Saved lists:** written safely on Windows; a damaged list is kept as a copy instead of being replaced.
- **Checksums:** a mismatch is now shown as an error on the download.
- **Extension:** only web links are handed to the app; a page can only ask about its own tab.
- **Keychain problems are explained:** if the Keychain is locked or access is denied, the app says so (with Try
  Again) instead of writing passwords to a file.
- **Time left** no longer jumps back after 24 hours or shows nonsense for huge files (Windows could crash on it).
- **Releases:** tests and a full check of every bundled tool run before anything is published; downloaded tools are
  verified by checksum; full license texts for ffmpeg and QuickJS are included.

## 0.6.1

- Fix: the Windows build of 0.6.0 (same features as 0.6.0 below).

## 0.6.0

- **In-app updates:** when a new version is released, the app tells you when it opens (and once a day while it runs),
  with what's new and three choices: Update Now, Remind Me Later or Skip This Version. Update Now downloads the new
  version, closes the app, installs it and opens it again (Mac: the app in Applications; Windows: the installed app,
  the portable copy opens the download page). Turn it off or check by hand in Settings (Options on Windows) and from
  Check for Updates in the app and Help menus.
- **Video progress window shows real activity:** Show Details lists the picture and sound pieces with how much of each
  is downloaded, like the connection list of a normal download, and the window shrinks when details are hidden and
  grows back when shown.
- **Transfer rate and time left for videos:** the speed is averaged over the last few seconds, so videos (which arrive
  in bursts) show a steady transfer rate, a time left and a smooth graph instead of empty values and spikes.
- **Clear last step:** at the end of a video the status says "Joining picture and sound…" instead of staying on 99%.
- **Real video name from the start:** the list and the progress window show the video's title (with a video icon)
  as soon as the download begins.

## 0.5.9

- **Firefox add-on signed by Mozilla:** the `.xpi` in each release now installs in regular Firefox by dragging it onto
  the window (no more "could not be installed because it has not been verified").

## 0.5.8

- **Browser extension uses much less processing power:** it no longer inspects every request of every page, only the
  ones that can be audio, video or a stream playlist, and the video button re-positions at most once per frame while
  scrolling. Busy sites in Firefox and Chrome no longer keep the extension working in the background.

## 0.5.7

- Published by mistake from the 0.5.6 code; use 0.5.8.

## 0.5.6

- **Fix: wrong video size and progress.** Videos downloaded in many small pieces could show a size and amount far too
  large (for example 1.5 GB for a 73 MB video) and jumpy speeds. Picture and sound are now counted separately and each
  only ever grows, so size, progress, speed and time left are accurate.

## 0.5.5

- **Videos start right away:** the download window now looks the video up while it is open, like IDM checks a file
  (real title, file size and best quality, with "Finding the best quality…" meanwhile). Pressing Start reuses what was
  found, so the download begins at once instead of waiting 10–15 seconds at 0%.
- **Progress window for videos:** a compact window with real details (video site, best quality or sound only, folder,
  saved file name) under Show Details, instead of an empty area.
- **Installer:** waits until the running app has really quit, so the new version opens afterwards.

## 0.5.4

- Releases include a plain zip of the Mac app, so the one-line Terminal installer is faster and quieter (no disk image to mount).

## 0.5.3

- **Videos open the download window too:** the video button on pages, the extension popup and the right-click menu now
  show the same download window as file links (name, size or best quality, folder, sound only), instead of starting
  straight away.
- **"Preparing video…" status:** video sites need a few seconds to find the best quality before the first byte arrives;
  the list and the progress window now say so instead of sitting at 0%.

## 0.5.2

- Mac: the progress window is taller and can be resized, so more connections are visible at once.

## 0.5.1

- Mac build fix for the new progress window (0.5.0 was not published).

## 0.5.0

- **Download window:** clicking a download link in Chrome, Edge, Brave, Opera or Firefox opens a compact window with the
  file name (editable), size, address, category, folder and queue, and Start Download / Download Later / Cancel. The
  size and the real file name are read from the server when the browser doesn't know them.
- **Progress window for each download:** status, size, downloaded, transfer rate, time left, resume capability, a live
  speed graph, the start positions and progress of every connection, and Pause/Resume, Cancel and Hide details.
  Several downloads each get their own window; finished downloads close theirs.
- **New options** (Settings/Options, General): show the download window, show progress windows, and bring the main
  window to the front for browser downloads. Otherwise the app stays in the background (menu bar or tray).
- **No duplicates:** the same link is never added twice (a second click shows the running download), and the extension
  ignores repeated hand-offs of one link. If the app doesn't answer within 8 seconds, the browser keeps the download.
- Show Progress in the right-click menu of the download list.

## 0.4.5

- **One app window only:** downloads sent from the browser are added to the app that is already running instead of
  opening another window. Opening the app again brings the running window to the front (Mac and Windows).
- New releases are published automatically once the Mac and Windows files are built.

## 0.4.4

- **Terms of Use and License Agreement:** shown when you open the Mac disk image (Agree / Disagree) and in the Windows
  installer (I accept the agreement); included with the app as TERMS.txt and linked from About and the Help menu.
- Windows installer lists the publisher, support and update links.
- Clearer first-launch steps for macOS ("could not verify" and "damaged" messages) and for updating, in the README
  and the built-in Help.

## 0.4.3

- **Help:** a built-in Help window on Mac (Help menu, ⌘?) and Windows (Help button, F1) with topics for getting started,
  the browser extension, videos, queues, batch and grabber, settings, the command line, troubleshooting and shortcuts.
  The same guide is online in docs/HELP.md.
- **About:** a new About window with the version, "Made by Shahrukh Khalid", links to the website, GitHub, source code
  and problem reports, the license and a Check for Updates button.
- Help menu on Mac: Online Help, Report a Problem, Release Notes, Website and Source Code.

## 0.4.2

- New app icon on Mac, Windows and in the browser extension.
- Mac installer window: clear "drag to Applications" layout, the browser add-ons below, and a thank-you note with links
  to github.com/Shahrukhalid and www.shahrukhalid.com.
- Windows installer: the same thank-you note and links on the last page.

## 0.4.1

- Video sites: the progress bar, speed and time left now update while a video downloads (before, it jumped from 0% to
  done), and continue smoothly from the picture to the sound part.
- Cleaner file names for saved videos (the page title only).

## 0.4.0

- **YouTube, Instagram, Facebook, X (Twitter), TikTok, Vimeo, Reddit and 1,000+ other sites:** save videos in the best
  quality (up to 4K/8K) or sound only, from the video button, the extension popup, the right-click menu, Add URL or `ndm`.
  Feeds work too: the button saves the post a video belongs to.
- Built-in video engine that keeps itself up to date, and it now also joins WebM/VP9/AV1 and Opus. Nothing else to
  install.
- Copy-protected services stay unsupported.

## 0.3.0

- **Streamed video (HLS):** the extension now finds streamed videos on any site that doesn't copy-protect them, lists
  them best quality first (for example 1080p) with the page's title as the name, and hides the lower qualities.
- The app downloads the highest resolution and bit rate, fetches the pieces in parallel, resumes after a pause or
  restart, and joins picture and sound into one `.mp4`.
- Everything is included, so streamed video always comes out as one `.mp4` with no extra installs.
- Copy-protected videos are shown as protected instead of failing later.
- README shows real screenshots of the Mac and Windows apps, taken automatically from each build.
- Paste a `.m3u8` link into Add URL, or use `ndm https://…/master.m3u8`.

## 0.2.1

- Cleaner, more professional windows on Mac and Windows: the system's file icons in the list, coloured status dots,
  item counts in the sidebar, a search box, a status bar with totals and speed, and a helpful empty view.
- Release files now keep the same names, so the download links in the README always fetch the newest version.
- README: direct downloads and step-by-step setup for Mac, Windows, Chrome-type browsers and Firefox.

## 0.2.0

The Mac and Windows apps now have the same features and toolbar, and there is a Firefox add-on.

- **Scheduler and queues:** several queues, each with start and stop times, weekdays, its own limit and an action when
  done (quit, sleep or shut down). Move downloads between queues.
- **Batch downloads** from patterns such as `file[001-100].jpg`, `[a-z]` and `[0-100:5]`.
- **Site Grabber:** explore a website section and pick its files by type.
- **Clipboard monitoring:** copying a link to a file opens the Add window.
- **Download this video** button over players, plus media and link lists in the extension popup.
- **Proxy** (system, HTTP, SOCKS5) and **password-protected downloads**.
- **Refresh Link:** continue an expired download from a new link, pasted or caught from the browser.
- **After a download:** Download Complete window, sound, checksum check, optional virus scan, files marked as downloaded.
- **Per-download speed limit** and a richer Add window (name, folder, queue, login, checksum, limit, Download Later).
- **Command line:** `ndm` to add links, batches, queues and options from scripts.
- **Firefox add-on** (`.xpi`) next to the Chrome extension.

## 0.1.0

First release.

- Downloads split into up to 32 parallel connections; a free connection takes over half of the largest remaining part.
- Pause, resume after quitting, automatic retries, speed limit and a download queue.
- Categories, a per-part progress bar, notifications, a menu bar icon and a Dock badge.
- Windows app with the same engine: installer and portable zip, tray icon, single instance.
- Browser extension: catches downloads (with the page's sign-in cookies), right-click "Download with Nexonic", popup with settings.

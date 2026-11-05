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

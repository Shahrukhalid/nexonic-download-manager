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

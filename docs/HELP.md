# Nexonic Download Manager Help

The same guide is built into the app: **Help → Nexonic Download Manager Help** (⌘?) on Mac, the **Help** button (F1) on Windows. Shortcuts below use Mac keys; on Windows use Ctrl instead of ⌘.

## Contents

- [Getting Started](#getting-started)
- [Browser Extension](#browser-extension)
- [Saving Videos](#saving-videos)
- [Queues and Scheduler](#queues-and-scheduler)
- [Batch and Site Grabber](#batch-and-site-grabber)
- [Speed and Settings](#speed-and-settings)
- [Site Settings, Backup and Network](#site-settings-backup-and-network)
- [Download on Your Phone](#download-on-your-phone)
- [Command Line](#command-line)
- [Troubleshooting](#troubleshooting)
- [Keyboard Shortcuts](#keyboard-shortcuts)

## Getting Started

**Add a download.** Click Add URL (⌘N), paste one or more links and press Start Download. Links you copy are offered automatically when the Add window opens.

**Pause and resume.** Select a download and press Stop or Resume. Downloads continue where they stopped, even after you quit the app or restart the Mac.

**Find your files.** Finished files are saved in the folder chosen in the Add window (your Downloads folder by default). Double-click a download to open it, or right-click and choose Show in Finder.

## Browser Extension

**Chrome, Edge, Brave and Opera.** Open the app once. Unzip NexonicExtension-Chrome.zip, open the browser's extensions page, turn on Developer mode, click Load unpacked and choose the folder. You can also save the extension folder from Settings, under Browsers & Command Line.

**Firefox.** Drag NexonicAddon-Firefox.xpi onto Firefox. For a quick test, open about:debugging and choose Load Temporary Add-on.

**Download and progress windows.** When you click a download link in the browser, the download window shows the file name, size, category and folder: choose Start Download, Download Later or Cancel. Each download you start gets its own progress window with the speed, time left, a speed graph and every connection. Turn either window off in Settings, General.

**What it does.** Downloads from the browser are handed to the app automatically. Right-click a link and choose Download with Nexonic, or use the video button that appears on pages with video.

**Keeping a download in the browser.** In the extension's menu: **Pause for 30 minutes**, **Keep downloads from this site in the browser**, or list sites and file types under Settings. Files smaller than the size set there also stay in the browser.

**A red ! on the extension's button** means the app could not be reached. Open the app once so it registers with the browser, then restart the browser; meanwhile downloads stay in the browser.

## Saving Videos

**From a website.** Click the video button the extension shows on the page, or paste the page link into Add URL. Videos are saved in the best available quality as .mp4.

**Sound only.** Choose Sound only in the Add window to save just the audio.

**Streamed video.** Streams (.m3u8) are found by the extension, downloaded in parallel and joined into one .mp4. Copy-protected services are not supported.

## Queues and Scheduler

**Queues.** Put downloads in a queue to run them one after another or a few at a time. Right-click a download and choose Move to Queue.

**Scheduler.** Start a queue at a set time, stop it at another, and optionally quit the app, sleep or shut down the Mac when it is done.

## Batch and Site Grabber

**Batch download.** Download a numbered series such as file01.jpg to file50.jpg by writing the link once with a pattern, for example file[01-50].jpg.

**Site Grabber.** Enter a page address to list every file linked from it, filter by type and download the ones you check.

## Speed and Settings

**Faster downloads.** Each file is split into parts that download at the same time. Change Connections per download in Settings (up to 32).

**Speed limit.** Set a limit for all downloads in Settings, or for one download in its Properties.

**Proxy and sign-in.** Set a proxy in Settings. For password-protected downloads, add the user name and password in the Add window or under Properties.

## Site Settings, Backup and Network

*Windows, Linux and Mac. On Mac they are under Settings → Sites, Backup and Connection; DNS over HTTPS and Check the
Connection are on Windows and Linux.*

- **Site settings** (Options → Sites): connections, speed limit, tries, timeout, user agent, folder, queue, login and
  proxy for every download from one website. `example.com` also covers its subdomains. A site's login is only sent to
  that site, never to a site it redirects to, and is saved encrypted.
- **Backup** (Options → Backup): export your settings, site settings, queues and downloads list to a file, and import
  it on another computer. Passwords and cookies are never put in a backup. **Export Links** saves every link as text.
- **Advanced network** (Options → Connection): smallest part size, tries, timeouts, DNS over HTTPS, and
  **Check the Connection** to see how long the name lookup and the connection take.

## Download on Your Phone

Right-click one or more downloads and choose **Show QR Code**. Scan the code with your phone's camera: the Nexonic app
on the phone (Android now; iPhone when its app is released) opens with the links in its Add window. Only the links are
in the code, never cookies or passwords, so downloads that need you to be signed in may not work on the phone. About
10–20 links fit in one code.

## Command Line

**Install.** Open Settings, go to Browsers & Command Line and click Install ndm Command…. Then open Terminal and type ndm help.

**Examples.** ndm https://example.com/file.zip adds a download. ndm https://example.com/file.zip --queue Night adds it to a queue, and ndm help lists every option.

## Troubleshooting

**A link has expired.** Right-click the download, choose Refresh Link and paste a fresh link. The download continues without starting over.

**macOS won't open the app.** Install or update with one Terminal command and macOS won't ask: curl -fsSL https://shahrukhalid.github.io/nexonic-download-manager/install.sh | bash — otherwise: 1) when macOS says the app was Not Opened, click Done; 2) open System Settings, then Privacy & Security, scroll to Security and click Open Anyway; 3) click Open Anyway again and enter your Mac password. macOS asks only once, and updates installed with Update Now open without a warning. On macOS 13 and 14 you can also right-click the app and choose Open. Pictures of each step: https://github.com/Shahrukhalid/nexonic-download-manager#first-launch-on-mac

**macOS says the app is damaged.** Open Terminal and run: xattr -cr "/Applications/Nexonic Download Manager.app" Then open the app again. This happens because the app is free and not sold through Apple.

**The old icon still shows.** Install the new version over the old one in Applications, quit the app and open it again. Restarting the Mac refreshes the icon cache.

**The browser doesn't hand over downloads.** Open the app once so it can register itself with your browsers, then restart the browser.

**A video site stopped working.** The video engine updates itself every day. Try again later, and if it still fails, report the problem with the page link.

**Updating the app.** When a new version is released, the app tells you when it opens, with what's new. Choose Update Now to install it (the app closes, updates and opens again), Remind Me Later, or Skip This Version. You can also use Check for Updates in the menu, or turn the check off in Settings.

## Keyboard Shortcuts

**Downloads.** ⌘N  Add URL  
⇧⌘P  Stop all  
⇧⌘R  Resume all  
⌘,  Settings  
⌘?  Help

## Still stuck?

- [Report a problem](https://github.com/Shahrukhalid/nexonic-download-manager/issues/new) (include your system, the app version from About, and the link if it is public).
- [Latest version and release notes](https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest)
- [Terms of Use and License Agreement](../TERMS.md)

Made by [Shahrukh Khalid](https://www.shahrukhalid.com) · [GitHub](https://github.com/Shahrukhalid) · MIT License

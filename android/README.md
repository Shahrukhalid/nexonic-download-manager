# Nexonic Download Manager for Android

Native Android app (Kotlin, Jetpack Compose), Android 10 or later. Same download rules as the Mac and Windows apps.

## What it does

- **Fast downloads:** up to 32 connections per file. A connection that finishes takes over half of the largest part still downloading.
- **Pause and resume**, also after the app or the phone restarts. A file that changed on the server is downloaded again instead of mixed up. Expired links can be refreshed.
- **Built-in browser** with an address bar, back/forward and search. Downloads started on a page open the download window (name, size, type, whether it can resume). Long-press a link or image to download it. **Found on this page** lists the videos, music and files on the page, with their quality labels when the page names them.
- **Share from any browser:** in Chrome or another app, *Share → Download with Nexonic*. Web links can also be opened with Nexonic.
- **Queues:** several downloads at a time (1–6), others wait their turn. *Later* keeps a download for the scheduler or *Start all*.
- **Scheduler:** start downloads kept for later at a set time every day.
- **Background downloads** with a progress notification and *Pause all*; a notification when each file is done.
- **Wi-Fi only**, speed limit, retries, logins (user name and password), batch patterns like `file[01-50].jpg`, several links pasted at once.
- **Files:** finished files go to *Download/Nexonic* (by type: Video, Music, Documents…), visible in the Files app. Open, share or delete them from the app.
- **List:** All / Unfinished / Finished, filter by type, search, and per-download details with each connection's progress.
- **Private:** no account, ads or tracking. Cookies and passwords are encrypted with a key held in the Android Keystore, and sent only to their own site (never after a redirect to another site or from https to http). Third-party cookies are blocked in the browser by default. The app is excluded from cloud backup.

## Limits on Android

- Chrome for Android does not run desktop extensions. Use the built-in browser, or *Share → Download with Nexonic* from Chrome.
- Video pages (YouTube, Facebook, Instagram, TikTok and 1,000+ sites) go through the video engine: yt-dlp with Python and ffmpeg, from [youtubedl-android](https://github.com/yausername/youtubedl-android). It updates yt-dlp once a day (Settings → Video engine). It adds about 50 MB, so releases ship one APK per processor type: `NexonicDownloadManager-Android.apk` (64-bit ARM) and `-32bit.apk`. Copy-protected (DRM) video is not supported.
- Copy-protected video is never supported.
- Android limits background work. Downloads run as a data-sync foreground service. Android 15 may stop it after several hours; downloads then wait and continue when you open the app. Exact scheduled starts need *Alarms & reminders* permission; without it, Android may delay the start or show a notification to tap.

## Build

```bash
gradle -p android testDebugUnitTest assembleDebug   # JDK 17 and the Android SDK
```

The *Android* GitHub Actions workflow runs the tests and builds `NexonicDownloadManager-Android-debug.apk` and the signed release APK on every change.
When a version tag (`v*`) is pushed, the *Android release* workflow builds the signed app and adds `NexonicDownloadManager-Android.apk` to that release, next to the Mac and Windows files; the download links on the website and in the README always fetch it. The app's version comes from `VERSION`, like the desktop apps.
The release APK is signed when these repository secrets are set: `NDM_ANDROID_KEYSTORE_B64`, `NDM_ANDROID_KEYSTORE_PASSWORD`, `NDM_ANDROID_KEY_ALIAS`, `NDM_ANDROID_KEY_PASSWORD`. `setup-android-signing.sh` creates the key on your own computer (with `openssl`) and sets the secrets with `gh`. Keys are never stored in the repository. Back up the key folder: every update must be signed with the same key.

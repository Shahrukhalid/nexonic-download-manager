# Code signing policy

Nexonic Download Manager is free and open source (MIT OR Apache-2.0). This page explains how its release files are
built and signed.

## Windows

Free code signing for Windows is provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

> Status: the application to the SignPath Foundation is in progress. Until it is approved, the Windows files are
> unsigned, and Windows SmartScreen may show "Windows protected your PC" (click **More info → Run anyway**). Every
> file can be checked against the SHA-256 published with its release (see [VERIFY.md](VERIFY.md)).

Signed files (once approved): `NexonicDownloadManager-Windows-Setup.exe`, and the program files inside the installer
and `NexonicDownloadManager-Windows-Portable.zip` that are built from this repository (`NexonicDownloadManager.exe`,
`nexonic-host.exe`, `ndm.exe`). Third-party tools shipped with the app (FFmpeg, the video engine, QuickJS) are
included as published by their own projects and are not signed by this project.

## How releases are built

- Every release is built from this repository's source code by GitHub Actions
  ([`.github/workflows/release.yml`](../.github/workflows/release.yml)), started by a version tag. Nothing is built
  or signed on a personal computer.
- The build scripts and workflow files are part of the source code and are reviewed like any other change.
- Every release file is published with its SHA-256 checksum.
- Each signing request is approved by hand by an approver (below) before anything is signed.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [Shahrukh Khalid](https://github.com/Shahrukhalid) |
| Approvers | [Shahrukh Khalid](https://github.com/Shahrukhalid) |

Changes from other contributors are reviewed by a reviewer before they are merged. All team members use multi-factor
authentication for GitHub and SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it. In detail, it connects only to:

- the addresses you choose to download from (including, for torrents you add, their trackers and peers);
- GitHub, when it checks for a new version of the app (when the app opens and once a day, unless you turn this off in
  Settings, or when you choose Check for Updates);
- GitHub, to download and update its video engine;
- your own browser, through the browser extension, on the same computer only.

There is no account, no analytics and no tracking. The optional local API is off by default and listens only on this
computer (127.0.0.1), with a key you create.

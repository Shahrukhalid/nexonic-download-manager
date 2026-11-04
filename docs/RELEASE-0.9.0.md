# v0.9.0 release preparation

This candidate includes PRs #59–#63: cache cleanup, current package manifests, approved icon assets, download
providers and experimental Linux torrents with native resume checkpoints and per-file priorities.
It remains Nexonic Download Manager; application IDs, data directories and update channels stay stable.

## Candidate verification

The release branch runs the normal Windows, Linux x64/ARM64, Android, iOS and extension checks. The Mac build
also checks Apple silicon and Intel binaries for release branches. No tag or public release is needed for this.
The release source-version guard checks VERSION, Windows assemblies/helpers, the Mac helper, extensions,
mobile version inputs and the current changelog. Tag builds additionally require the exact matching tag.

Windows CI installs the checksum-pinned public v0.8.1 installer on a fresh runner profile, then the candidate.
Both apps must start and retain synthetic paused history, its queue, settings and the original part-file hash.
The per-user install location and installer ID must remain unchanged. Separate compatibility fixtures were
serialized using the actual v0.8.1 Core assembly; current Windows and Linux readers test history, settings,
fake encrypted credentials, engine routing and backup safety. These do not replace real-device checks of
another user's DPAPI, Linux keyring, browser configuration or phone storage.

Linux CI verifies optional native torrent downloads, checkpoint restart, selected-file completion and integrity,
then builds and checks AppImage/DEB/RPM packages and runs the packaged HTTP/API/queue regression smoke tests.
The tag release workflow installs the same optional test runtime and runs those native tests as well.
The native library remains an optional host dependency rather than a bundled app component.

## Publication gates

After candidate checks pass, get the owner's explicit approval to merge and publish v0.9.0. Do not create a tag
before that approval: the tag workflow builds a draft and later publishes it automatically after asset checks.

The production workflow must finish before the release is reported complete. Verify:

- The tag resolves to the approved release commit and VERSION is 0.9.0.
- Mac universal builds and signature verification, Windows setup/portable, both Linux architectures, signed
  Android APKs and both browser extension archives are attached with nonzero sizes.
- The Firefox XPI is Mozilla-signed; Android uses the existing signing key and its version code increases from
  801 to 900. iOS remains an unsigned/testing artifact, not an App Store publication.
- Android-GPL-Sources.tar.gz is attached and its component inventory has no missing-source entries. Own Android
  source is also available from the exact release tag. Existing GPL/third-party notices stay intact.
- latest.json lists every expected update asset and its SHA-256 matches the published file. Linux checksum
  manifests match their packages. Inspect the final release after all upload workflows complete.
- The website advertises the latest published release. Main's VERSION alone must not advertise an unpublished
  candidate; publication triggers a website refresh.

Package-manager hashes for 0.9.0 can only be generated after final assets exist. Keep the verified 0.8.1 manifests
until then, and obtain approval before external Homebrew/Winget submissions. Do not disrupt the existing open
Winget 0.8.0 submission. Human translation review (#46–#52) and real-device checklist #53 remain separate work.

Experimental torrent setup and limits are documented in TORRENTS.md and the release notes. Full seeding,
tracker management and new media-manager features are not included in 0.9.0.

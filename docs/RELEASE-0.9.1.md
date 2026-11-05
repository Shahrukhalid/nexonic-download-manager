# v0.9.1 release checks

Scope: official icon replacement from PR #65; no torrent-engine changes. Preserve all application identifiers and data paths.

Before publication: version/tag consistency; Windows, universal macOS, Linux x64/ARM64, Android and iOS builds/tests; signed Firefox add-on; all 18 expected release assets; Android corresponding-source archive; final asset hashes. Do not publish a draft with missing files.

Artwork checks: exact master SHA-256 `cab9578c52922d1fce461f5910157138dfcf5636ddbc83f42c2d06efefa694d1`, all 44 generated hashes, PNG/ICO/ICNS decoding, opaque iOS icon and Android adaptive assets. Native shell caches and physical-device launchers still require manual visual confirmation.

The existing download links remain stable. Scoop, Homebrew and winget submissions are outside this release operation.

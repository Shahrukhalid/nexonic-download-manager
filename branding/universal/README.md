# A1 Universal Hub — branding preparation

The original owner-supplied PNG is preserved byte-for-byte as master.png. SHA-256:
65f3556135b5e04304def0c28d0b6895cf1233e22e3c81c0eede0621cb50185c

Regenerate with `python3 scripts/make-artwork.py` (Pillow required). The generator refuses a modified master.
Small-size derivatives crop the original central N and download mark; no replacement artwork is invented.
The preview shows actual pixel sizes against light and dark backgrounds. generated-manifest.json records output hashes.

This branch prepares the icon rollout across Windows, macOS, Linux, Android (including adaptive and monochrome),
iPhone/iPad, extensions and website artwork. Product names, application identifiers, data paths and update channels
are preserved. The public name/tagline remain a future launch decision. Do not merge or publish this branding
branch until the owner approves the coordinated Universal Download Manager launch.

The existing installer background is retained; its app shortcut automatically uses the new bundle icon.
The master remains opaque, including its black outer corners. The iOS App Store icon is RGB without transparency.

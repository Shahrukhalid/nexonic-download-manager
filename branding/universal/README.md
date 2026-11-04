# Official Nexonic gradient icon

The owner-supplied image is preserved byte-for-byte in master.png (1536 × 1024 RGB).
SHA-256: cab9578c52922d1fce461f5910157138dfcf5636ddbc83f42c2d06efefa694d1

Run `python3 scripts/make-artwork.py` with Pillow. The generator refuses any other master hash.
It replaces every previously generated icon; it cannot recreate the earlier segmented/neon design.
The exact white tile, gradient N, orbital swoosh and download arrow are retained. No substitute glyphs are drawn.

The square tile crop is [304, 56, 1232, 984], removing unused outer canvas without stretching the design.
Desktop icons mask only the outer tile corners. Small-size icons use a tighter crop around all three original
colored elements, padded on white; the manifest records that crop. Android adaptive icons use those same elements
inside the 66dp safe region and a white background. Android monochrome and macOS menu-bar templates use the original colored-element silhouette.
The Linux tray uses the original colored mark on its white tile for light/dark-panel readability. iOS uses an opaque RGB square as required by its app-icon catalog.

Generated coverage includes Windows multi-resolution ICO; macOS ICNS, iconset and menu-bar template;
Linux 16/24/32/48/64/128/256/512 launcher resources and tray; Android density/adaptive/monochrome resources;
iOS AppIcon; Chrome/Firefox icons; website favicon/icon/share artwork; and the README logo.
Installers and About/window/taskbar/Dock icons reference these assets. The neutral DMG instruction background
contains no old logo and is retained. Historical screenshots are documentation of their original builds;
new platform build screenshots verify the updated app resources.

preview.png shows actual icon sizes on light and dark backgrounds. generated-manifest.json lists every output
hash and the Pillow version. Icon pixels derive entirely from the master. Existing system Arial/DejaVu fonts are
used only for preview/share-image labels, so those text images can differ across operating systems.

This is a review branch. Do not merge, release or deploy website files until the owner approves the rollout.
Application IDs, names, data directories, settings and update channels are unchanged.

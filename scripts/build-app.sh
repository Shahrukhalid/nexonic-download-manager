#!/usr/bin/env bash
# Builds build/Nexonic Download Manager.app, a .dmg, the browser extension zip and the Firefox add-on.
#   UNIVERSAL=0 scripts/build-app.sh      # this Mac's architecture only (faster)
#   SIGN_IDENTITY="Developer ID Application: …" scripts/build-app.sh   # signed with hardened runtime
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="$(cat VERSION)"
APP="build/Nexonic Download Manager.app"
ARCHS=()
# Optimized for size instead of speed (the app spends its time waiting for the network, not computing): a smaller app.
SIZE_FLAGS=(-Xswiftc -Osize)
if [ "${UNIVERSAL:-1}" != "0" ] && xcodebuild -version >/dev/null 2>&1; then ARCHS=(--arch arm64 --arch x86_64); fi

if [ "${#ARCHS[@]}" -gt 0 ]; then python3 scripts/build-torrent-apple.py --universal-mac; else python3 scripts/build-torrent-apple.py; fi

if [ "${#ARCHS[@]}" -gt 0 ]; then
    # One build per chip, then lipo. A single two-chip SwiftPM build prelinks each module into one object, and the
    # current Apple linker drops Swift class data there ("null objc class data" for the torrent module).
    BIN="mac/.build/universal-release"
    rm -rf "$BIN" && mkdir -p "$BIN"
    PARTS=()
    for TRIPLE in arm64-apple-macosx13.0 x86_64-apple-macosx13.0; do
        swift build --package-path mac -c release --triple "$TRIPLE" "${SIZE_FLAGS[@]}"
        PARTS+=("$(swift build --package-path mac -c release --triple "$TRIPLE" --show-bin-path)")
    done
    for P in NexonicDownloadManager nexonic-host; do
        lipo -create "${PARTS[0]}/$P" "${PARTS[1]}/$P" -output "$BIN/$P"
    done
else
    swift build --package-path mac -c release "${SIZE_FLAGS[@]}"
    BIN="$(swift build --package-path mac -c release --show-bin-path)"
fi

rm -rf build && mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
python3 scripts/build-extensions.py
cp "$BIN/NexonicDownloadManager" "$APP/Contents/MacOS/"
cp "$BIN/nexonic-host" "$APP/Contents/MacOS/"
# Release apps ship without symbol tables, as Xcode does (about 20 MB smaller for both chips); signing comes after.
strip "$APP/Contents/MacOS/NexonicDownloadManager" "$APP/Contents/MacOS/nexonic-host"
# Small bundled ffmpeg (re-packs streamed video into .mp4); built once and kept in .ffmpeg-cache/.
if [ ! -x .ffmpeg-cache/mac/ffmpeg ]; then bash scripts/build-ffmpeg.sh mac .ffmpeg-cache/mac; fi
cp .ffmpeg-cache/mac/ffmpeg .ffmpeg-cache/mac/ffprobe "$APP/Contents/MacOS/"
# Video engine for YouTube and other sites (yt-dlp + QuickJS); fetched fresh unless .tools-cache/mac already has it.
if [ ! -x .tools-cache/mac/yt-dlp-engine/yt-dlp_macos ]; then bash scripts/fetch-video-tools.sh mac .tools-cache/mac; fi
cp .tools-cache/mac/qjs "$APP/Contents/MacOS/"
# The unpacked engine, its program renamed nexonic-video-engine, packed small with xz (about 26 MB instead of 52);
# the app unpacks it once on first launch.
ENGINE=build/video-engine
rm -rf "$ENGINE" && cp -R .tools-cache/mac/yt-dlp-engine "$ENGINE" && mv "$ENGINE/yt-dlp_macos" "$ENGINE/nexonic-video-engine"
tar --options xz:compression-level=9 -C "$ENGINE" -cJf "$APP/Contents/Resources/video-engine.tar.xz" --exclude VERSION .
cp "$ENGINE/VERSION" "$APP/Contents/Resources/video-engine-version.txt"
rm -rf "$ENGINE"
# MacOS/nexonic-video-engine starts the engine from the command line and checks (unpacking it once into a temporary folder).
cat > "$APP/Contents/MacOS/nexonic-video-engine" <<'SH'
#!/bin/sh
R="$(cd "$(dirname "$0")/../Resources" && pwd)"
D="${TMPDIR:-/tmp}/nexonic-video-engine-$(cat "$R/video-engine-version.txt")"
if [ ! -x "$D/nexonic-video-engine" ]; then
  T="$D.$$" && mkdir -p "$T" && /usr/bin/tar -xJf "$R/video-engine.tar.xz" -C "$T" && rm -rf "$D" && mv "$T" "$D" || exit 1
fi
exec "$D/nexonic-video-engine" "$@"
SH
chmod +x "$APP/Contents/MacOS/nexonic-video-engine"
cat packaging/THIRD-PARTY-NOTICES.txt .ffmpeg-cache/mac/NOTICE-video-tools-1.txt .tools-cache/mac/NOTICE-video-tools-2.txt \
  > "$APP/Contents/Resources/THIRD-PARTY-NOTICES.txt"
cp packaging/TERMS.txt "$APP/Contents/Resources/TERMS.txt"
cp -R mac/Native/notices "$APP/Contents/Resources/torrent-licenses"
cp -R packaging/Localizations/*.lproj "$APP/Contents/Resources/"
sed "s/__VERSION__/$VERSION/g" packaging/Info.plist > "$APP/Contents/Info.plist"
cp -R build/extension-chrome "$APP/Contents/Resources/Extension"
cp "build/NexonicFirefoxAddon-$VERSION.xpi" "$APP/Contents/Resources/Firefox Add-on.xpi"

# Prepared assets are generated only from branding/universal/master.png.
cp branding/universal/generated/macos/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
cp branding/universal/generated/macos/MenuBarIcon.png "$APP/Contents/Resources/MenuBarIcon.png"

if [ -n "${SIGN_IDENTITY:-}" ]; then
  codesign --force --options runtime --timestamp -s "$SIGN_IDENTITY" "$APP/Contents/MacOS/nexonic-host"
  for tool in ffmpeg ffprobe qjs nexonic-video-engine; do
    codesign --force --options runtime --timestamp -s "$SIGN_IDENTITY" "$APP/Contents/MacOS/$tool"
  done
  codesign --force --options runtime --timestamp -s "$SIGN_IDENTITY" "$APP"
else
  codesign --force --deep -s - "$APP"
fi

# Installer window: background with instructions and links, icons placed on it (packaging/dmg-settings.py).
EXTRA="build/dmg-extra"
mkdir -p "$EXTRA"
cp -R "$APP/Contents/Resources/Extension" "$EXTRA/Browser Extension"
cp "$APP/Contents/Resources/Firefox Add-on.xpi" "$EXTRA/Firefox Add-on.xpi"
tiffutil -cathidpicheck packaging/dmg-background.png packaging/dmg-background@2x.png -out build/dmg-background.tiff 2>/dev/null
python3 -m venv build/venv && build/venv/bin/pip install -q --require-hashes -r packaging/dmg-requirements.txt
build/venv/bin/dmgbuild -s packaging/dmg-settings.py \
  -D app="$APP" -D extension="$EXTRA/Browser Extension" -D xpi="$EXTRA/Firefox Add-on.xpi" \
  -D icns="$APP/Contents/Resources/AppIcon.icns" -D background=build/dmg-background.tiff \
  "Nexonic Download Manager" "build/NexonicDownloadManager-$VERSION.dmg" >/dev/null
echo "Built $APP, build/NexonicDownloadManager-$VERSION.dmg, build/NexonicExtension-$VERSION.zip and build/NexonicFirefoxAddon-$VERSION.xpi"

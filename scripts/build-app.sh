#!/usr/bin/env bash
# Builds build/Nexonic Download Manager.app, a .dmg, the browser extension zip and the Firefox add-on.
#   UNIVERSAL=0 scripts/build-app.sh      # this Mac's architecture only (faster)
#   SIGN_IDENTITY="Developer ID Application: …" scripts/build-app.sh   # signed with hardened runtime
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="$(cat VERSION)"
APP="build/Nexonic Download Manager.app"
ARCHS=()
if [ "${UNIVERSAL:-1}" != "0" ] && xcodebuild -version >/dev/null 2>&1; then ARCHS=(--arch arm64 --arch x86_64); fi

swift build --package-path mac -c release ${ARCHS[@]+"${ARCHS[@]}"}
BIN="$(swift build --package-path mac -c release ${ARCHS[@]+"${ARCHS[@]}"} --show-bin-path)"

rm -rf build && mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
python3 scripts/build-extensions.py
cp "$BIN/NexonicDownloadManager" "$APP/Contents/MacOS/"
cp "$BIN/nexonic-host" "$APP/Contents/MacOS/"
# Small bundled ffmpeg (re-packs streamed video into .mp4); built once and kept in .ffmpeg-cache/.
if [ ! -x .ffmpeg-cache/mac/ffmpeg ]; then bash scripts/build-ffmpeg.sh mac .ffmpeg-cache/mac; fi
cp .ffmpeg-cache/mac/ffmpeg .ffmpeg-cache/mac/ffprobe "$APP/Contents/MacOS/"
# Video engine for YouTube and other sites (yt-dlp + QuickJS); fetched fresh unless .tools-cache/mac already has it.
if [ ! -x .tools-cache/mac/yt-dlp ]; then bash scripts/fetch-video-tools.sh mac .tools-cache/mac; fi
cp .tools-cache/mac/yt-dlp .tools-cache/mac/qjs "$APP/Contents/MacOS/"
cat packaging/THIRD-PARTY-NOTICES.txt .ffmpeg-cache/mac/NOTICE-video-tools-1.txt .tools-cache/mac/NOTICE-video-tools-2.txt \
  > "$APP/Contents/Resources/THIRD-PARTY-NOTICES.txt"
cp packaging/TERMS.txt "$APP/Contents/Resources/TERMS.txt"
cp -R packaging/Localizations/*.lproj "$APP/Contents/Resources/"
sed "s/__VERSION__/$VERSION/g" packaging/Info.plist > "$APP/Contents/Info.plist"
cp -R build/extension-chrome "$APP/Contents/Resources/Extension"
cp "build/NexonicFirefoxAddon-$VERSION.xpi" "$APP/Contents/Resources/Firefox Add-on.xpi"

ICONSET="build/AppIcon.iconset"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s packaging/icon-1024.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  sips -z $((s * 2)) $((s * 2)) packaging/icon-1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

if [ -n "${SIGN_IDENTITY:-}" ]; then
  codesign --force --options runtime --timestamp -s "$SIGN_IDENTITY" "$APP/Contents/MacOS/nexonic-host"
  for tool in ffmpeg ffprobe qjs yt-dlp; do
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
python3 -m venv build/venv && build/venv/bin/pip install -q "dmgbuild>=1.6,<2"
build/venv/bin/dmgbuild -s packaging/dmg-settings.py \
  -D app="$APP" -D extension="$EXTRA/Browser Extension" -D xpi="$EXTRA/Firefox Add-on.xpi" \
  -D icns="$APP/Contents/Resources/AppIcon.icns" -D background=build/dmg-background.tiff \
  "Nexonic Download Manager" "build/NexonicDownloadManager-$VERSION.dmg" >/dev/null
echo "Built $APP, build/NexonicDownloadManager-$VERSION.dmg, build/NexonicExtension-$VERSION.zip and build/NexonicFirefoxAddon-$VERSION.xpi"

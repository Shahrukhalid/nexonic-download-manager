#!/usr/bin/env bash
# Checks a Linux build before it is uploaded: every tool present and running, the packages install, and the app starts
# on a desktop and opens its windows (screenshots in build/linux-<arch>/screenshots).
#   linux/packaging/check.sh x64|arm64
set -euo pipefail
ARCH="$1"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/build/linux-$ARCH"
APP="$OUT/app"
V="$(tr -d '[:space:]' < "$ROOT/VERSION")"
fail() { echo "FAILED: $*" >&2; exit 1; }

for f in NexonicDownloadManager nexonic-host ffmpeg ffprobe yt-dlp qjs Extension/manifest.json "Firefox Add-on.xpi" \
         nexonic-download-manager.png THIRD-PARTY-NOTICES.txt LICENSE.txt; do
  [ -e "$APP/$f" ] || fail "missing from the app folder: $f"
done
[ "$("$APP/NexonicDownloadManager" --version)" = "$V" ] || fail "the app does not report version $V"
"$APP/nexonic-host" help | grep -q "ndm batch" || fail "ndm help"
python3 "$ROOT/scripts/host-ping.py" "$APP/nexonic-host"
"$APP/ffmpeg" -hide_banner -muxers 2>&1 | grep -q " mp4 " || fail "ffmpeg has no mp4 muxer"
"$APP/yt-dlp" --version
"$APP/qjs" -e 'console.log("qjs ok")'

if command -v ffmpeg >/dev/null && [ "$(command -v ffmpeg)" != "$APP/ffmpeg" ]; then
  echo "== The video engine joins picture and sound with the bundled ffmpeg (local test video)"
  SITE="$(mktemp -d)"
  ( cd "$SITE" && ffmpeg -loglevel error -f lavfi -i testsrc=size=640x360:rate=25 -f lavfi -i sine=frequency=440 -t 4 -map 0:v -map 1:a \
      -c:v libx264 -c:a aac -f dash -adaptation_sets "id=0,streams=v id=1,streams=a" dash.mpd )
  ( cd "$SITE" && exec python3 -m http.server 8777 >/dev/null 2>&1 ) &
  SERVER=$!
  sleep 1
  "$APP/yt-dlp" --ignore-config --ffmpeg-location "$APP" -f "bv*+ba/b" --merge-output-format "mp4/mkv" -P "$SITE/out" \
    -o "test.%(ext)s" http://127.0.0.1:8777/dash.mpd
  kill $SERVER
  "$APP/ffprobe" -v error -show_entries stream=codec_type -of csv "$SITE/out/test.mp4" | sort | tr '\n' ' ' | grep -q "stream,audio stream,video" \
    || fail "picture and sound were not joined"
fi

echo "== AppImage"
AI="$(ls "$OUT"/NexonicDownloadManager-Linux-*.AppImage)"
chmod +x "$AI"
[ "$(APPIMAGE_EXTRACT_AND_RUN=1 "$AI" --version)" = "$V" ] || fail "the AppImage does not start"
APPIMAGE_EXTRACT_AND_RUN=1 "$AI" --host help | grep -q "ndm batch" || fail "the AppImage's ndm command"

echo "== .deb"
DEB="$(ls "$OUT"/nexonic-download-manager_*.deb)"
dpkg-deb --info "$DEB"
sudo apt-get install -y -qq "$DEB" >/dev/null
ndm help | grep -q "ndm batch" || fail "ndm after installing the .deb"
[ "$(nexonic-download-manager --version)" = "$V" ] || fail "the installed app"
desktop-file-validate /usr/share/applications/nexonic-download-manager.desktop
for size in 16 24 32 48 64 128 256 512; do
  icon="/usr/share/icons/hicolor/${size}x${size}/apps/nexonic-download-manager.png"
  [ -f "$icon" ] || fail "missing launcher icon: $size"
  [ "$(identify -format '%wx%h' "$icon")" = "${size}x${size}" ] || fail "wrong launcher icon size: $size"
done
sudo apt-get remove -y -qq nexonic-download-manager >/dev/null
[ ! -e /opt/nexonic-download-manager ] || fail "the .deb left files behind"

echo "== .rpm"
RPM="$(ls "$OUT"/nexonic-download-manager-*.rpm)"
rpm -qip "$RPM"
rpm -qlp "$RPM" | grep -qx /opt/nexonic-download-manager/nexonic-host || fail "the .rpm has no browser helper"
rpm -qlp "$RPM" | grep -qx /usr/bin/ndm || fail "the .rpm has no ndm command"

echo "== The app starts on a desktop and opens each window"
SHOTS="$OUT/screenshots"
mkdir -p "$SHOTS"
for view in main add info progress options scheduler batch grabber help about; do
  HOME_DIR="$(mktemp -d)"
  HOME="$HOME_DIR" XDG_RUNTIME_DIR="$HOME_DIR/run" NDM_SHOW="$view" xvfb-run -a -s "-screen 0 1440x900x24" bash -c '
    mkdir -p "$XDG_RUNTIME_DIR" && chmod 700 "$XDG_RUNTIME_DIR"
    "$0" > "$HOME/app.log" 2>&1 &
    pid=$!
    sleep 12
    if ! kill -0 $pid 2>/dev/null; then echo "the app quit while showing $NDM_SHOW:"; cat "$HOME/app.log"; exit 1; fi
    import -window root "$1"
    kill $pid
  ' "$APP/NexonicDownloadManager" "$SHOTS/$view.png"
  rm -rf "$HOME_DIR"
done
ls -la "$SHOTS"
echo "All checks passed for linux-$ARCH"

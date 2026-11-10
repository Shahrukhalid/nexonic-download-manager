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

for f in NexonicDownloadManager nexonic-host ffmpeg ffprobe qjs Extension/manifest.json "Firefox Add-on.xpi" \
         nexonic-download-manager.png THIRD-PARTY-NOTICES.txt LICENSE.txt; do
  [ -e "$APP/$f" ] || fail "missing from the app folder: $f"
done
[ "$("$APP/NexonicDownloadManager" --version)" = "$V" ] || fail "the app does not report version $V"
"$APP/nexonic-host" help | grep -q "ndm batch" || fail "ndm help"
python3 "$ROOT/scripts/host-ping.py" "$APP/nexonic-host"
"$APP/ffmpeg" -hide_banner -muxers 2>&1 | grep -q " mp4 " || fail "ffmpeg has no mp4 muxer"
ENGINE="$ROOT/.tools-cache/linux-$ARCH/yt-dlp"  # the official engine the app downloads on first use
[ ! -e "$APP/nexonic-video-engine" ] || fail "the video engine is shipped again (the app downloads it on first use)"
"$ENGINE" --version
"$APP/qjs" -e 'console.log("qjs ok")'
SYS="$OUT/app-system"
[ ! -e "$SYS/libcoreclr.so" ] || fail "the .deb/.rpm app folder carries its own .NET"

if command -v ffmpeg >/dev/null && [ "$(command -v ffmpeg)" != "$APP/ffmpeg" ]; then
  echo "== The video engine joins picture and sound with the bundled ffmpeg (local test video)"
  SITE="$(mktemp -d)"
  ( cd "$SITE" && ffmpeg -loglevel error -f lavfi -i testsrc=size=640x360:rate=25 -f lavfi -i sine=frequency=440 -t 4 -map 0:v -map 1:a \
      -c:v libx264 -c:a aac -f dash -adaptation_sets "id=0,streams=v id=1,streams=a" dash.mpd )
  ( cd "$SITE" && exec python3 -m http.server 8777 >/dev/null 2>&1 ) &
  SERVER=$!
  sleep 1
  "$ENGINE" --ignore-config --ffmpeg-location "$APP" -f "bv*+ba/b" --merge-output-format "mp4/mkv" -P "$SITE/out" \
    -o "test.%(ext)s" http://127.0.0.1:8777/dash.mpd
  kill $SERVER
  "$APP/ffprobe" -v error -show_entries stream=codec_type -of csv "$SITE/out/test.mp4" | sort | tr '\n' ' ' | grep -q "stream,audio stream,video" \
    || fail "picture and sound were not joined"
  echo "== Chapters, tags and subtitles go into the file (what the app asks for with every video)"
  printf 'WEBVTT\n\n00:00.000 --> 00:02.000\nHello\n' > "$SITE/en.vtt"
  python3 - "$SITE" <<'PY'
import json, sys
d = sys.argv[1]
json.dump({"id": "t", "title": "Chapters", "ext": "mp4", "url": "file://" + d + "/out/test.mp4", "protocol": "file",
           "extractor": "generic", "extractor_key": "Generic", "webpage_url": "file://" + d + "/out/test.mp4",
           "chapters": [{"start_time": 0, "end_time": 2, "title": "One"}, {"start_time": 2, "end_time": 4, "title": "Two"}],
           "subtitles": {"en": [{"ext": "vtt", "url": "file://" + d + "/en.vtt"}]}}, open(d + "/info.json", "w"))
PY
  "$ENGINE" --ignore-config --enable-file-urls --load-info-json "$SITE/info.json" --ffmpeg-location "$APP" --embed-metadata \
    --embed-chapters --write-subs --sub-langs en --embed-subs -P "$SITE/emb" -o "x.%(ext)s" || fail "embedding chapters and subtitles failed"
  EMB="$("$APP/ffprobe" -v error -show_streams -show_chapters "$SITE/emb/x.mp4")"
  for want in codec_name=mov_text "TAG:title=One" "TAG:title=Two"; do
    grep -q "$want" <<<"$EMB" || fail "the joined file has no $want"
  done
fi

echo "== AppImage"
AI="$(ls "$OUT"/NexonicDownloadManager-Linux-*.AppImage)"
# The differential update file must describe this exact AppImage, and the AppImage must say where to find it.
[ "$(sed -n 's/^SHA-1: //p' "$AI.zsync" 2>/dev/null)" = "$(sha1sum "$AI" | cut -d' ' -f1)" ] || fail "the .zsync update file does not match the AppImage"
# Packed with large zstd blocks (smaller download, same start-up speed); the debugger files are not shipped.
OFF="$(APPIMAGE_EXTRACT_AND_RUN=1 "$AI" --appimage-offset)"
if command -v unsquashfs >/dev/null; then
  SB="$(unsquashfs -s -o "$OFF" "$AI")"; LIST="$(unsquashfs -l -o "$OFF" "$AI")"
  [[ "$SB" == *"Block size 1048576"* ]] || fail "the AppImage is not packed with 1 MB blocks"
  if grep -qE "libmscordbi|libmscordaccore|/createdump$" <<<"$LIST"; then fail "debugger files are in the AppImage"; fi
fi
grep -aq "gh-releases-zsync|Shahrukhalid|nexonic-download-manager|latest|$(basename "$AI").zsync" "$AI" || fail "the AppImage has no update information"
chmod +x "$AI"
[ "$(APPIMAGE_EXTRACT_AND_RUN=1 "$AI" --version)" = "$V" ] || fail "the AppImage does not start"
APPIMAGE_EXTRACT_AND_RUN=1 "$AI" --host help | grep -q "ndm batch" || fail "the AppImage's ndm command"

echo "== .deb"
DEB="$(ls "$OUT"/nexonic-download-manager_*.deb)"
dpkg-deb --info "$DEB"
# .NET 10 comes from the distribution (Ubuntu 24.04+) or, on older Ubuntu, from the .NET backports archive.
if ! apt-cache show dotnet-runtime-10.0 >/dev/null 2>&1; then
  sudo add-apt-repository -y ppa:dotnet/backports >/dev/null && sudo apt-get update -qq >/dev/null
fi
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

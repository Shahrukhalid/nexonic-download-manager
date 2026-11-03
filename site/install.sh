#!/bin/bash
# Installs or updates Nexonic Download Manager on a Mac:
#   curl -fsSL https://shahrukhalid.github.io/nexonic-download-manager/install.sh | bash
# Downloads the latest release from GitHub, copies the app into /Applications and opens it.
set -euo pipefail

APP_NAME="Nexonic Download Manager"
DMG_URL="https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest/download/NexonicDownloadManager-Mac.dmg"
ZIP_URL="https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest/download/NexonicDownloadManager-Mac.zip"
TERMS_URL="https://github.com/Shahrukhalid/nexonic-download-manager/blob/main/TERMS.md"
DEST="/Applications"

if [ "$(uname -s)" != "Darwin" ]; then
  echo "This installer is for macOS. Windows: download the Setup file from"
  echo "https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest"
  exit 1
fi

echo "Installing $APP_NAME (free and open source, MIT or Apache-2.0)."
echo "By installing you agree to the Terms of Use: $TERMS_URL"

work="$(mktemp -d)"
mnt="$work/mnt"
cleanup() {
  hdiutil detach "$mnt" -quiet >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

echo "Downloading the latest version…"
src=""
# Newer releases include a plain zip of the app; older ones only the disk image.
if curl -fsIL -o /dev/null "$ZIP_URL" && curl -fL --progress-bar -o "$work/app.zip" "$ZIP_URL"; then
  mkdir -p "$work/zip"
  ditto -x -k "$work/app.zip" "$work/zip"
  src="$work/zip/$APP_NAME.app"
else
  curl -fL --progress-bar -o "$work/app.dmg" "$DMG_URL"
  mkdir -p "$mnt"
  # The disk image asks to accept the Terms of Use (confirmed above); its text goes to the log, not the screen.
  if ! hdiutil attach "$work/app.dmg" -nobrowse -noautoopen -quiet -mountpoint "$mnt" < <(yes) >"$work/attach.log" 2>&1; then
    echo "Could not open the downloaded disk image. Please try again." >&2
    exit 1
  fi
  src="$mnt/$APP_NAME.app"
fi

if [ ! -d "$src" ]; then
  echo "The download did not contain the app. Please try again later." >&2
  exit 1
fi

if pgrep -xq "NexonicDownloadManager"; then
  echo "Closing the running copy…"
  osascript -e "tell application \"$APP_NAME\" to quit" >/dev/null 2>&1 || true
  # Wait until it has really quit, so the new version (not the old one still in memory) opens afterwards.
  for _ in $(seq 1 20); do pgrep -xq "NexonicDownloadManager" || break; sleep 0.5; done
  pkill -x "NexonicDownloadManager" 2>/dev/null || true
  sleep 1
fi

target="$DEST/$APP_NAME.app"
if [ ! -w "$DEST" ]; then
  echo "Administrator password needed to copy the app into $DEST."
  sudo rm -rf "$target"
  sudo ditto "$src" "$target"
  sudo xattr -dr com.apple.quarantine "$target" 2>/dev/null || true
else
  rm -rf "$target"
  ditto "$src" "$target"
  xattr -dr com.apple.quarantine "$target" 2>/dev/null || true
fi

version="$(defaults read "$target/Contents/Info" CFBundleShortVersionString 2>/dev/null || echo "")"
echo "Installed $APP_NAME ${version} in $DEST."
echo "Browser extensions: open the app, then Settings > Browsers & Command Line."
open "$target"

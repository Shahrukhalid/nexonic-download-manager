#!/bin/bash
# Nexonic Download Manager - one-line installer for macOS and Linux.
#
#   macOS and Linux:
#     curl -fsSL https://shahrukhalid.github.io/nexonic-download-manager/install.sh | bash
#   Windows (PowerShell):
#     irm https://shahrukhalid.github.io/nexonic-download-manager/install.ps1 | iex
#
# Installs the latest release from GitHub. Re-running installs the newest
# version over the old one; downloads and settings are kept.
#
# Options (Unix): [--to DIR] [--system] [--no-launch] [--help]
#   --to DIR    install the Linux AppImage into DIR (default: ~/Applications)
#   --system    install system-wide into /opt (needs sudo; Linux only)
#   --no-launch do not open the app after installing
#
# The installer verifies the download against the SHA-256 checksum GitHub
# publishes for the release (latest.json) when possible.
set -euo pipefail

REPO="Shahrukhalid/nexonic-download-manager"
BASE_URL="https://github.com/${REPO}/releases/latest/download"
TERMS_URL="https://github.com/${REPO}/blob/main/TERMS.md"
APP_NAME="Nexonic Download Manager"
DEST_LINUX="$HOME/Applications"
LAUNCH=1
SYSTEM=0
TO=""

usage() {
  echo "Usage: install.sh [--to DIR] [--system] [--no-launch]"
  echo "  Installs $APP_NAME for macOS or Linux."
}

while [ $# -gt 0 ]; do
  case "$1" in
    --to) TO="${2:-}"; [ -n "$TO" ] || { echo "--to needs a directory" >&2; exit 2; }; shift 2 ;;
    --system) SYSTEM=1; shift ;;
    --no-launch) LAUNCH=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

echo "Installing $APP_NAME (free and open source, MIT or Apache-2.0)."
echo "By installing you agree to the Terms of Use: $TERMS_URL"

OS="$(uname -s)"
ARCH="$(uname -m)"

# expected_sha256 <asset-name>: prints the sha256 GitHub publishes in latest.json.
expected_sha256() {
  local name="$1" json digest
  json="$(curl -fsSL "$BASE_URL/latest.json" 2>/dev/null || true)"
  [ -n "$json" ] || return 1
  if command -v python3 >/dev/null 2>&1; then
    digest="$(printf '%s' "$json" | python3 -c \
      'import json,sys
try:
    data = json.load(sys.stdin)
    want = sys.argv[1]
    for a in data.get("assets", []):
        if a.get("name") == want and str(a.get("digest", "")).startswith("sha256:"):
            print(a["digest"].split(":", 1)[1])
            break
except Exception:
    pass' "$name" 2>/dev/null || true)"
  else
    digest="$(printf '%s' "$json" | grep -o "\"name\"[[:space:]]*:[[:space:]]*\"$name\"[^}]*\"digest\"[[:space:]]*:[[:space:]]*\"sha256:[0-9a-fA-F]*\"" \
      | grep -o "sha256:[0-9a-fA-F]*" | cut -d: -f2 | head -1 || true)"
  fi
  [ -n "$digest" ] || return 1
  printf '%s' "$digest"
}

verify_file() {
  # verify_file <path> <asset-name>: check sha256 against latest.json; warn-only on failure.
  local file="$1" name="$2" want="" got=""
  want="$(expected_sha256 "$name" || true)"
  [ -n "$want" ] || { echo "Note: could not fetch checksums; skipping verification."; return 0; }
  if command -v sha256sum >/dev/null 2>&1; then
    got="$(sha256sum "$file" | cut -d' ' -f1)"
  elif command -v shasum >/dev/null 2>&1; then
    got="$(shasum -a 256 "$file" | cut -d' ' -f1)"
  else
    echo "Note: no sha256sum/shasum found; skipping verification."
    return 0
  fi
  if [ "$got" = "$want" ]; then
    echo "Checksum OK ($name)."
  else
    echo "ERROR: checksum mismatch for $name." >&2
    echo "  expected: $want" >&2
    echo "  actual:   $got" >&2
    return 1
  fi
}

install_mac() {
  DMG_URL="$BASE_URL/NexonicDownloadManager-Mac.dmg"
  ZIP_URL="$BASE_URL/NexonicDownloadManager-Mac.zip"
  DEST="/Applications"

  work="$(mktemp -d)"
  mnt="$work/mnt"
  cleanup() {
    hdiutil detach "$mnt" -quiet >/dev/null 2>&1 || true
    rm -rf "$work"
  }
  trap cleanup EXIT

  echo "Downloading the latest version…"
  src=""
  # Newer releases include a plain zip of the app (no disk image to mount).
  if curl -fsIL -o /dev/null "$ZIP_URL" && curl -fL --progress-bar -o "$work/app.zip" "$ZIP_URL"; then
    verify_file "$work/app.zip" "NexonicDownloadManager-Mac.zip"
    mkdir -p "$work/zip"
    ditto -x -k "$work/app.zip" "$work/zip"
    src="$work/zip/$APP_NAME.app"
  else
    curl -fL --progress-bar -o "$work/app.dmg" "$DMG_URL"
    verify_file "$work/app.dmg" "NexonicDownloadManager-Mac.dmg"
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
  if [ "$LAUNCH" = "1" ]; then
    open "$target"
  fi
}

install_linux() {
  case "$ARCH" in
    x86_64) AI="x86_64" ;;
    aarch64|arm64) AI="aarch64" ;;
    *) echo "No Linux build for '$ARCH'. See https://github.com/${REPO}/releases/latest" >&2; exit 1 ;;
  esac
  FILE="NexonicDownloadManager-Linux-$AI.AppImage"

  if [ "$SYSTEM" = "1" ]; then
    [ "$(id -u)" = "0" ] || { echo "Requesting administrator rights for a system-wide install…"; }
    work="$(mktemp -d)"
    trap 'rm -rf "$work"' EXIT
    echo "Downloading the latest version ($FILE)…"
    curl -fL --progress-bar -o "$work/app.AppImage" "$BASE_URL/$FILE"
    verify_file "$work/app.AppImage" "$FILE"
    chmod 755 "$work/app.AppImage"
    echo "Extracting to /opt/nexonic-download-manager (needs sudo)…"
    (cd "$work" && "$work/app.AppImage" --appimage-extract >/dev/null)
    sudo mkdir -p /opt/nexonic-download-manager
    sudo cp -a "$work/squashfs-root/usr/lib/nexonic-download-manager/." /opt/nexonic-download-manager/
    sudo chmod 755 /opt/nexonic-download-manager/NexonicDownloadManager /opt/nexonic-download-manager/nexonic-host
    sudo install -m 755 -D "$work/squashfs-root/usr/lib/nexonic-download-manager/NexonicDownloadManager" /usr/bin/nexonic-download-manager 2>/dev/null || \
      { printf '#!/bin/sh\nexec /opt/nexonic-download-manager/NexonicDownloadManager "$@"\n' | sudo tee /usr/bin/nexonic-download-manager >/dev/null; sudo chmod 755 /usr/bin/nexonic-download-manager; }
    { printf '#!/bin/sh\nexec /opt/nexonic-download-manager/nexonic-host "$@"\n' | sudo tee /usr/bin/ndm >/dev/null; sudo chmod 755 /usr/bin/ndm; }
    sudo install -m 644 -D "$work/squashfs-root/nexonic-download-manager.desktop" /usr/share/applications/nexonic-download-manager.desktop 2>/dev/null || \
      sudo cp "$work/squashfs-root/nexonic-download-manager.desktop" /usr/share/applications/nexonic-download-manager.desktop
    # The AppImage's own entry calls the bare AppRun binary name; point it at the /usr/bin launcher instead.
    sudo sed -i 's|^Exec=NexonicDownloadManager|Exec=nexonic-download-manager|' /usr/share/applications/nexonic-download-manager.desktop
    for size in 16 24 32 48 64 128 256 512; do
      if [ -f "$work/squashfs-root/usr/share/icons/hicolor/${size}x${size}/apps/nexonic-download-manager.png" ]; then
        sudo install -m 644 -D "$work/squashfs-root/usr/share/icons/hicolor/${size}x${size}/apps/nexonic-download-manager.png" \
          "/usr/share/icons/hicolor/${size}x${size}/apps/nexonic-download-manager.png"
      fi
    done
    sudo update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
    sudo gtk-update-icon-cache -f /usr/share/icons/hicolor >/dev/null 2>&1 || true
    echo "Installed $APP_NAME in /opt/nexonic-download-manager."
    echo "Run: nexonic-download-manager (or: ndm <link>). Browser setup: open the app once, then Options > Browsers & Command Line."
    if [ "$LAUNCH" = "1" ] && [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
      /opt/nexonic-download-manager/NexonicDownloadManager >/dev/null 2>&1 &
    fi
    return 0
  fi

  dest="${TO:-$DEST_LINUX}"
  # Expand a leading ~ without depending on the shell's tilde rules for variables.
  case "$dest" in "~"*) dest="$HOME${dest#\~}" ;; esac
  mkdir -p "$dest"
  target="$dest/$FILE"

  if pgrep -xf "$target" >/dev/null 2>&1 || pgrep -xq "NexonicDownloadManager" >/dev/null 2>&1; then
    echo "Closing the running copy…"
    pkill -x "NexonicDownloadManager" 2>/dev/null || true
    sleep 2
  fi

  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  echo "Downloading the latest version ($FILE)…"
  curl -fL --progress-bar -o "$tmp/app.AppImage" "$BASE_URL/$FILE"
  verify_file "$tmp/app.AppImage" "$FILE"
  chmod 755 "$tmp/app.AppImage"
  mv -f "$tmp/app.AppImage" "$target"
  chmod 755 "$target"
  trap - EXIT
  rm -rf "$tmp"

  echo "Installed $APP_NAME in $target."
  echo "First start registers the app menu entry, the browser helper and the 'ndm' command."
  echo "If it does not start, install FUSE (package 'fuse3'), or run it with --appimage-extract-and-run."
  if [ "$LAUNCH" = "1" ] && [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
    echo "Opening the app…"
    "$target" >/dev/null 2>&1 &
  else
    echo "Open it with: $target"
    echo "Tip: Arch Linux users can also run: yay -S nexonic-download-manager-bin"
  fi
}

case "$OS" in
  Darwin) install_mac ;;
  Linux) install_linux ;;
  *) echo "This installer is for macOS and Linux." >&2
     echo "Windows (PowerShell): irm https://shahrukhalid.github.io/nexonic-download-manager/install.ps1 | iex" >&2
     echo "Or download a file from https://github.com/${REPO}/releases/latest" >&2
     exit 1 ;;
esac

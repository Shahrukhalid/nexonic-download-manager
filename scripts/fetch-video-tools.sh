#!/usr/bin/env bash
# Downloads the video engine shipped with the app: the official yt-dlp release and QuickJS (a small JavaScript
# runtime yt-dlp needs for some sites).
#   scripts/fetch-video-tools.sh mac     OUT_DIR   # yt-dlp (universal) + qjs (universal, needs lipo)
#   scripts/fetch-video-tools.sh windows OUT_DIR   # yt-dlp.exe + qjs.exe
#   scripts/fetch-video-tools.sh linux-x64   OUT_DIR   # yt-dlp + qjs for 64-bit Intel/AMD Linux
#   scripts/fetch-video-tools.sh linux-arm64 OUT_DIR   # yt-dlp + qjs for 64-bit ARM Linux
set -euo pipefail
TARGET="$1"
OUT="$(mkdir -p "$2" && cd "$2" && pwd)"
QJS=v0.16.1
# One yt-dlp version for the program and its license list (resolved once, so they always match).
YTTAG="$(curl -fsSLI --retry 4 -o /dev/null -w '%{url_effective}' https://github.com/yt-dlp/yt-dlp/releases/latest | sed 's#.*/tag/##')"
case "$YTTAG" in ""|*/*) echo "Could not find the yt-dlp version" >&2; exit 1 ;; esac
YT="https://github.com/yt-dlp/yt-dlp/releases/download/$YTTAG"
QJ="https://github.com/quickjs-ng/quickjs/releases/download/$QJS"
get() { curl -fsSL --retry 4 -o "$2" "$1"; }
sha() { if command -v sha256sum >/dev/null; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }
# Every downloaded tool is checked before it can end up in an installer: QuickJS against hashes pinned here,
# yt-dlp against the SHA2-256SUMS file of the same yt-dlp release.
check() {
  local got; got="$(sha "$1")"
  [ "$got" = "$2" ] || { echo "Checksum mismatch for $1: got $got, expected $2" >&2; exit 1; }
}
SUMS="$(mktemp)"; get "$YT/SHA2-256SUMS" "$SUMS"
ytsum() { awk -v f="$1" '$2 == f { print $1 }' "$SUMS"; }

case "$TARGET" in
  mac)
    get "$YT/yt-dlp_macos" "$OUT/yt-dlp"
    check "$OUT/yt-dlp" "$(ytsum yt-dlp_macos)"
    get "$QJ/qjs-darwin-arm64" "$OUT/qjs-arm64"
    check "$OUT/qjs-arm64" 9a24e7435036906c098d539daf47bcc8e7e8ad2f3aa084a0bce9313c6c3527e0
    get "$QJ/qjs-darwin-x86_64" "$OUT/qjs-x86_64"
    check "$OUT/qjs-x86_64" 5982a1ebb20e1a9bf6162bafd29d445823616084cfeddee8881f8d69d6e0fd74
    lipo -create "$OUT/qjs-arm64" "$OUT/qjs-x86_64" -output "$OUT/qjs"
    rm "$OUT/qjs-arm64" "$OUT/qjs-x86_64"
    chmod +x "$OUT/yt-dlp" "$OUT/qjs"
    ;;
  windows)
    get "$YT/yt-dlp.exe" "$OUT/yt-dlp.exe"
    check "$OUT/yt-dlp.exe" "$(ytsum yt-dlp.exe)"
    get "$QJ/qjs-windows-x86_64.exe" "$OUT/qjs.exe"
    check "$OUT/qjs.exe" 55a1b69cd4fdb6b0d3f8fdd910d0e89519f5330e408462084140c7b3b964fdae
    ;;
  linux-x64)
    get "$YT/yt-dlp_linux" "$OUT/yt-dlp"
    check "$OUT/yt-dlp" "$(ytsum yt-dlp_linux)"
    get "$QJ/qjs-linux-x86_64" "$OUT/qjs"
    check "$OUT/qjs" aae0d428c88bdd30fb490f54e616ebd4009ec279cc2a16ecebf0c3e17f7e76e7
    chmod +x "$OUT/yt-dlp" "$OUT/qjs"
    ;;
  linux-arm64)
    get "$YT/yt-dlp_linux_aarch64" "$OUT/yt-dlp"
    check "$OUT/yt-dlp" "$(ytsum yt-dlp_linux_aarch64)"
    get "$QJ/qjs-linux-aarch64" "$OUT/qjs"
    check "$OUT/qjs" c1635453aa60a78ebc7f05b2b559e0e9e9eb7d55b4dfc4a6e71a07d9d10b8a89
    chmod +x "$OUT/yt-dlp" "$OUT/qjs"
    ;;
  *) echo "usage: $0 mac|windows|linux-x64|linux-arm64 OUT_DIR" >&2; exit 2 ;;
esac

cat > "$OUT/NOTICE-video-tools-2.txt" <<TXT
- yt-dlp (public domain, The Unlicense): https://github.com/yt-dlp/yt-dlp
  The app keeps its own copy up to date once a day, because video sites change often.
- QuickJS-ng $QJS (MIT License): https://github.com/quickjs-ng/quickjs
They are used unmodified.

QuickJS-ng license:
TXT
get "https://raw.githubusercontent.com/quickjs-ng/quickjs/$QJS/LICENSE" "$OUT/qjs-license.txt"
cat "$OUT/qjs-license.txt" >> "$OUT/NOTICE-video-tools-2.txt" && rm "$OUT/qjs-license.txt"
# The yt-dlp program is a bundle that also contains Python and other libraries (some GPL/LGPL); their licenses,
# and where to get their source, are in yt-dlp's THIRD_PARTY_LICENSES.txt for the same version.
printf '\n\nyt-dlp %s bundled components (from its THIRD_PARTY_LICENSES.txt):\n\n' "$YTTAG" >> "$OUT/NOTICE-video-tools-2.txt"
get "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$YTTAG/THIRD_PARTY_LICENSES.txt" "$OUT/ytdlp-licenses.txt"
cat "$OUT/ytdlp-licenses.txt" >> "$OUT/NOTICE-video-tools-2.txt" && rm "$OUT/ytdlp-licenses.txt"
echo "Fetched the video engine for $TARGET into $OUT"

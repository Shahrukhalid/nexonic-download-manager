#!/usr/bin/env bash
# Builds a small LGPL ffmpeg that only re-packs streamed video into .mp4 (no re-encoding), for bundling with the app.
#   scripts/build-ffmpeg.sh mac      OUT_DIR   # universal (Apple silicon + Intel), run on a Mac
#   scripts/build-ffmpeg.sh windows  OUT_DIR   # 64-bit .exe, cross-built on Linux with mingw-w64
#   scripts/build-ffmpeg.sh linux    OUT_DIR   # this Linux machine's processor, linked statically (runs on any distribution)
#   scripts/build-ffmpeg.sh native   OUT_DIR   # this machine (for testing)
set -euo pipefail
TARGET="$1"
OUT="$(mkdir -p "$2" && cd "$2" && pwd)"
VERSION=n7.1
WORK="$(mktemp -d)"
if [ -n "${FFMPEG_SRC:-}" ]; then cp -R "$FFMPEG_SRC" "$WORK/src"; else git clone -q --depth 1 --branch "$VERSION" https://github.com/FFmpeg/FFmpeg.git "$WORK/src"; fi

COMMON=(
  --disable-everything --disable-autodetect --disable-network --disable-doc --disable-debug
  --disable-ffplay --enable-small --disable-shared --enable-static
  --enable-protocol=file --enable-protocol=pipe
  --enable-demuxer=mpegts,mov,aac,mp3,h264,hevc,matroska,ogg,flv,wav
  --enable-muxer=mp4,mov,ipod,matroska,webm,mp3,adts,ogg,opus
  --enable-parser=h264,hevc,aac,aac_latm,mpegaudio,ac3,av1,vp8,vp9,opus,vorbis
  --enable-decoder=h264,hevc,aac,aac_latm,mp3,ac3,eac3,opus,vorbis,vp8,vp9
  --enable-bsf=aac_adtstoasc,h264_mp4toannexb,hevc_mp4toannexb,extract_extradata,dump_extradata,vp9_superframe,av1_frame_merge
)

build() { # $1 = folder, rest = extra configure flags
  local dir="$WORK/$1"; shift
  mkdir -p "$dir" && cd "$dir"
  "$WORK/src/configure" "${COMMON[@]}" "$@" >/dev/null
  make -j"$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)" >/dev/null
}

case "$TARGET" in
  mac)
    build arm64 --arch=aarch64 --enable-cross-compile --target-os=darwin --cc="clang -arch arm64" \
      --extra-cflags="-mmacosx-version-min=13.0" --extra-ldflags="-mmacosx-version-min=13.0"
    build x86_64 --arch=x86_64 --enable-cross-compile --target-os=darwin --cc="clang -arch x86_64" --disable-x86asm \
      --extra-cflags="-mmacosx-version-min=13.0" --extra-ldflags="-mmacosx-version-min=13.0"
    lipo -create "$WORK/arm64/ffmpeg" "$WORK/x86_64/ffmpeg" -output "$OUT/ffmpeg"
    lipo -create "$WORK/arm64/ffprobe" "$WORK/x86_64/ffprobe" -output "$OUT/ffprobe"
    ;;
  windows)
    build win --arch=x86_64 --target-os=mingw32 --cross-prefix=x86_64-w64-mingw32- --enable-cross-compile \
      --disable-x86asm --extra-ldflags="-static"
    cp "$WORK/win/ffmpeg.exe" "$WORK/win/ffprobe.exe" "$OUT/"
    ;;
  linux)
    build linux --disable-x86asm --extra-ldflags=-static
    cp "$WORK/linux/ffmpeg" "$WORK/linux/ffprobe" "$OUT/"
    strip "$OUT/ffmpeg" "$OUT/ffprobe" 2>/dev/null || true
    ;;
  native)
    build native --disable-x86asm
    cp "$WORK/native/ffmpeg" "$WORK/native/ffprobe" "$OUT/"
    ;;
  *) echo "usage: $0 mac|windows|linux|native OUT_DIR" >&2; exit 2 ;;
esac

cat > "$OUT/NOTICE-video-tools-1.txt" <<TXT
This app includes ffmpeg and ffprobe ($VERSION), used only to re-pack downloaded video (no re-encoding).
ffmpeg is licensed under the GNU Lesser General Public License, version 2.1 or later.
Source: https://github.com/FFmpeg/FFmpeg/tree/$VERSION
It is built from that source, unmodified, by scripts/build-ffmpeg.sh in https://github.com/Shahrukhalid/nexonic-download-manager
License text: https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html (full text below)

TXT
cat "$WORK/src/COPYING.LGPLv2.1" >> "$OUT/NOTICE-video-tools-1.txt"  # the license travels with the program
rm -rf "$WORK"
echo "Built ffmpeg for $TARGET in $OUT"

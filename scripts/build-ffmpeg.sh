#!/usr/bin/env bash
# Builds a small LGPL ffmpeg that only re-packs streamed video into .mp4 (no re-encoding), for bundling with the app.
#   scripts/build-ffmpeg.sh mac      OUT_DIR   # universal (Apple silicon + Intel), run on a Mac
#   scripts/build-ffmpeg.sh windows  OUT_DIR   # 64-bit .exe, cross-built on Linux with mingw-w64
#   scripts/build-ffmpeg.sh linux    OUT_DIR   # this Linux machine's processor, linked statically (runs on any distribution)
#   scripts/build-ffmpeg.sh native   OUT_DIR   # this machine (for testing)
#   scripts/build-ffmpeg.sh android-arm64|android-arm|android-x64 OUT_DIR   # with the Android NDK; writes libffmpeg.so and
#     libffprobe.so (Android runs programs only from the app's library folder, so they are named and packed as libraries)
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
  # Chapters, tags and subtitles go into the file (yt-dlp's --embed-chapters reads an ffmetadata file; --embed-subs
  # turns WebVTT/SRT/ASS into mov_text for .mp4 and copies them into .mkv/.webm). Text formats only: a few hundred KB.
  --enable-demuxer=ffmetadata,webvtt,srt,ass --enable-muxer=ffmetadata,webvtt,srt,ass
  --enable-decoder=webvtt,subrip,ass,ssa,movtext --enable-encoder=movtext,webvtt,subrip,ass,ssa
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
  android-arm64|android-arm|android-x64)
    ndk="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-${ANDROID_NDK_LATEST_HOME:-}}}"
    tc="$(ls -d "$ndk"/toolchains/llvm/prebuilt/*/bin 2>/dev/null | head -1)"
    [ -x "$tc/llvm-ar" ] || { echo "The Android NDK is required (ANDROID_NDK_HOME)" >&2; exit 1; }
    case "$TARGET" in
      android-arm64) cc=aarch64-linux-android29-clang; arch=(--arch=aarch64) ;;
      android-arm)   cc=armv7a-linux-androideabi29-clang; arch=(--arch=arm --cpu=armv7-a --disable-asm) ;;
      android-x64)   cc=x86_64-linux-android29-clang; arch=(--arch=x86_64 --disable-x86asm) ;;
    esac
    # Linked against the system C library only (no other shared libraries), as a position-independent program.
    build android "${arch[@]}" --enable-cross-compile --target-os=android --cc="$tc/$cc" --cxx="$tc/$cc++" \
      --nm="$tc/llvm-nm" --ar="$tc/llvm-ar" --ranlib="$tc/llvm-ranlib" --strip="$tc/llvm-strip" --enable-pic \
      --extra-ldflags="-pie -Wl,-z,max-page-size=16384"
    "$tc/llvm-strip" --strip-all -o "$OUT/libffmpeg.so" "$WORK/android/ffmpeg"
    "$tc/llvm-strip" --strip-all -o "$OUT/libffprobe.so" "$WORK/android/ffprobe"
    ;;
  native)
    build native --disable-x86asm
    cp "$WORK/native/ffmpeg" "$WORK/native/ffprobe" "$OUT/"
    ;;
  *) echo "usage: $0 mac|windows|linux|native|android-arm64|android-arm|android-x64 OUT_DIR" >&2; exit 2 ;;
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

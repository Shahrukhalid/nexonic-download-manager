#!/usr/bin/env bash
# Builds the torrent engine for Android (arm64, arm, x64) and puts it where Gradle packages it, with its notices.
#   bash scripts/android-torrent-libs.sh arm64 arm
set -euo pipefail
cd "$(dirname "$0")/.."
# The NDK compiles with debug information; Gradle does not strip libraries placed in jniLibs, so strip here.
# --strip-unneeded keeps every exported (dynamic) symbol, including the JNI entry points.
ndk="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-${ANDROID_NDK_LATEST_HOME:-}}}"
sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [ -z "$ndk" ] && [ -n "$sdk" ]; then for c in "$sdk"/ndk/*; do [ -d "$c" ] && ndk="$c"; done; fi
strip_tool=""
for c in "$ndk"/toolchains/llvm/prebuilt/*/bin/llvm-strip; do [ -x "$c" ] && { strip_tool="$c"; break; }; done
[ -n "$strip_tool" ] || { echo "llvm-strip from the Android NDK is required (NDK: ${ndk:-not found})" >&2; exit 1; }
nm_tool="$(dirname "$strip_tool")/llvm-nm"
declare -A ABI=([arm64]=arm64-v8a [arm]=armeabi-v7a [x64]=x86_64)
for t in "$@"; do
  python3 scripts/build-torrent-native.py "android-$t"
  dest="android/app/src/main/jniLibs/${ABI[$t]}"
  mkdir -p "$dest"
  src=".native-cache/sdk/android-$t/lib/libnexonic_torrent.so"
  "$strip_tool" --strip-unneeded -o "$dest/libnexonic_torrent.so" "$src"
  # Same exported symbols before and after, and the JNI entry points are there.
  before="$("$nm_tool" -D --defined-only "$src" | sort)"
  after="$("$nm_tool" -D --defined-only "$dest/libnexonic_torrent.so" | sort)"
  [ "$before" = "$after" ] || { echo "Stripping changed the exported symbols of $src" >&2; exit 1; }
  jni="$(printf '%s\n' "$after" | grep -c Java_com_nexonic_downloadmanager_torrent_NativeTorrent_ || true)"
  [ "$jni" -ge 3 ] || { echo "JNI entry points missing in $dest/libnexonic_torrent.so" >&2; exit 1; }
  echo "$dest/libnexonic_torrent.so: $(stat -c %s "$src") -> $(stat -c %s "$dest/libnexonic_torrent.so") bytes, $jni JNI entry points"
done
first="$1"
notices=android/app/src/main/assets/licenses/Torrent-engine-NOTICES.txt
{
  echo "Torrent engine: libtorrent and the libraries it uses (Boost, OpenSSL and others), built from source."
  for f in .native-cache/sdk/android-$first/notices/*.txt; do echo; echo "== $(basename "$f" .txt)"; cat "$f"; done
} > "$notices"
ls -la android/app/src/main/jniLibs/*/libnexonic_torrent.so

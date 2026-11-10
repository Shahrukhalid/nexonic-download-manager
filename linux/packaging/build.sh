#!/usr/bin/env bash
# Builds the Linux app for this machine's processor and packs it as an AppImage, a .deb and a .rpm.
# The AppImage carries everything, its own .NET included; the .deb and .rpm use the system's .NET 10 runtime (a package
# dependency), which makes them much smaller.
#   linux/packaging/build.sh x64|arm64 FFMPEG_DIR TOOLS_DIR
# FFMPEG_DIR holds ffmpeg and ffprobe (scripts/build-ffmpeg.sh linux), TOOLS_DIR yt-dlp and qjs (scripts/fetch-video-tools.sh).
# Needs: .NET 10 SDK, python3, dpkg-deb, rpmbuild. Output: build/linux-<arch>/
set -euo pipefail
ARCH="$1"
FF="$(cd "$2" && pwd)"
TOOLS="$(cd "$3" && pwd)"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PKG="$ROOT/linux/packaging"
VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"
case "$ARCH" in
  x64)   RID=linux-x64;   AI=x86_64;  DEB=amd64; RPM=x86_64 ;;
  arm64) RID=linux-arm64; AI=aarch64; DEB=arm64; RPM=aarch64 ;;
  *) echo "usage: $0 x64|arm64 FFMPEG_DIR TOOLS_DIR" >&2; exit 2 ;;
esac
OUT="$ROOT/build/linux-$ARCH"
APP="$OUT/app"          # self-contained, for the AppImage
SYS="$OUT/app-system"   # uses the system's .NET, for the .deb and .rpm
rm -rf "$OUT"
mkdir -p "$APP" "$SYS"

python3 "$ROOT/scripts/build-torrent-native.py" "$RID"
echo "== App ($RID)"
dotnet publish "$ROOT/linux/App/App.csproj" -c Release -r "$RID" --self-contained -p:Version="$VERSION" -p:DebugType=embedded -p:SatelliteResourceLanguages=en -o "$APP" -nologo
dotnet publish "$ROOT/linux/App/App.csproj" -c Release -r "$RID" --self-contained false -p:Version="$VERSION" -p:DebugType=embedded -p:SatelliteResourceLanguages=en -o "$SYS" -nologo
# The browser helper and ndm command share the app's runtime: only their own files are added.
host() { # SELF_CONTAINED DEST
  dotnet publish "$ROOT/windows/Host/Host.csproj" -c Release -r "$RID" --self-contained "$1" -p:Version="$VERSION" \
    -p:InvariantGlobalization=true -o "$OUT/host" -nologo
  cp "$OUT/host"/nexonic-host "$OUT/host"/nexonic-host.dll "$OUT/host"/nexonic-host.deps.json "$OUT/host"/nexonic-host.runtimeconfig.json "$2/"
  rm -rf "$OUT/host"
}
host true "$APP"
host false "$SYS"
find "$APP" "$SYS" -name "*.pdb" -delete
# Debugger and crash-dump support of the bundled .NET (used only by attached debuggers and dump tools, never by the app;
# its own crash reports are plain text): about 4 MB less to download and install.
rm -f "$APP"/libmscordaccore.so "$APP"/libmscordbi.so "$APP"/createdump

python3 "$ROOT/scripts/build-extensions.py" >/dev/null
extras() { # DEST: everything besides .NET and the app itself
  # Strip the symbol table (exported symbols stay): about 3 MB smaller.
  strip --strip-unneeded -o "$1/libnexonic_torrent.so" "$ROOT/.native-cache/sdk/$RID/lib/libnexonic_torrent.so"
  cp -R "$ROOT/.native-cache/sdk/$RID/notices" "$1/torrent-licenses"
  cp "$ROOT/.native-cache/sdk/$RID/native-components.json" "$1/torrent-components.json"
  cp "$FF/ffmpeg" "$FF/ffprobe" "$TOOLS/qjs" "$1/"
  # The video engine itself is not shipped: the app downloads the official release the first time a video is saved.
  chmod 755 "$1/ffmpeg" "$1/ffprobe" "$1/qjs" "$1/NexonicDownloadManager" "$1/nexonic-host"
  cp -R "$ROOT/build/extension-chrome" "$1/Extension"
  cp "$ROOT/build/NexonicFirefoxAddon-$VERSION.xpi" "$1/Firefox Add-on.xpi"
  cp "$PKG/nexonic-download-manager.png" "$1/"
  mkdir -p "$1/icons"
  cp "$ROOT"/branding/universal/generated/linux/*.png "$1/icons/"
  cp "$ROOT/LICENSE" "$1/LICENSE.txt"
  cp "$ROOT/LICENSE-APACHE" "$1/LICENSE-APACHE.txt"
  cp "$ROOT/TERMS.md" "$1/TERMS.md"
  cat "$ROOT/packaging/THIRD-PARTY-NOTICES.txt" "$PKG/LINUX-NOTICES.txt" > "$1/THIRD-PARTY-NOTICES.txt"
}
echo "== Video tools, torrent engine and browser extension"
extras "$APP"
extras "$SYS"
for d in "$APP" "$SYS"; do cp "$FF"/NOTICE-video-tools-1.txt "$TOOLS"/NOTICE-video-tools-2.txt "$d/" 2>/dev/null || true; done

echo "== AppImage"
AD="$OUT/AppDir"
mkdir -p "$AD/usr/lib"
cp -a "$APP" "$AD/usr/lib/nexonic-download-manager"
cp "$PKG/AppRun" "$AD/AppRun"
chmod 755 "$AD/AppRun"
sed 's|^Exec=.*|Exec=NexonicDownloadManager %U|' "$PKG/nexonic-download-manager.desktop" > "$AD/nexonic-download-manager.desktop"
cp "$PKG/nexonic-download-manager.png" "$AD/nexonic-download-manager.png"
ln -s nexonic-download-manager.png "$AD/.DirIcon"
for size in 16 24 32 48 64 128 256 512; do
  target="$AD/usr/share/icons/hicolor/${size}x${size}/apps"
  mkdir -p "$target"
  cp "$ROOT/branding/universal/generated/linux/$size.png" "$target/nexonic-download-manager.png"
done
TOOLDIR="${APPIMAGE_TOOLS:-$ROOT/.appimage-tools}"
mkdir -p "$TOOLDIR"
fetch() { # url file sha256
  if [ ! -f "$TOOLDIR/$2" ] || [ "$(sha256sum "$TOOLDIR/$2" | cut -d' ' -f1)" != "$3" ]; then
    curl -fsSL --retry 4 -o "$TOOLDIR/$2" "$1"
  fi
  [ "$(sha256sum "$TOOLDIR/$2" | cut -d' ' -f1)" = "$3" ] || { echo "Checksum mismatch for $2" >&2; exit 1; }
  chmod +x "$TOOLDIR/$2"
}
HOST_AI="$(uname -m)"
case "$HOST_AI" in
  x86_64)  fetch https://github.com/AppImage/appimagetool/releases/download/1.9.0/appimagetool-x86_64.AppImage appimagetool-x86_64.AppImage 46fdd785094c7f6e545b61afcfb0f3d98d8eab243f644b4b17698c01d06083d1 ;;
  aarch64) fetch https://github.com/AppImage/appimagetool/releases/download/1.9.0/appimagetool-aarch64.AppImage appimagetool-aarch64.AppImage 04f45ea45b5aa07bb2b071aed9dbf7a5185d3953b11b47358c1311f11ea94a96 ;;
esac
case "$AI" in
  x86_64)  fetch https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64 runtime-x86_64 2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d ;;
  aarch64) fetch https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-aarch64 runtime-aarch64 00cbdfcf917cc6c0ff6d3347d59e0ca1f7f45a6df1a428a0d6d8a78664d87444 ;;
esac
# Update information (read by AppImage update tools) and a .zsync file, so an update downloads only what changed.
ZS="NexonicDownloadManager-Linux-$AI.AppImage.zsync"
APPIMAGE_EXTRACT_AND_RUN=1 ARCH="$AI" "$TOOLDIR/appimagetool-$HOST_AI.AppImage" --no-appstream --runtime-file "$TOOLDIR/runtime-$AI" \
  --comp zstd --mksquashfs-opt -Xcompression-level --mksquashfs-opt 22 --mksquashfs-opt -b --mksquashfs-opt 1M \
  -u "gh-releases-zsync|Shahrukhalid|nexonic-download-manager|latest|$ZS" \
  "$AD" "$OUT/NexonicDownloadManager-Linux-$AI.AppImage"
rm -f "$OUT/$ZS" "$ZS"  # made below from the final file, whatever appimagetool did
command -v zsyncmake >/dev/null || { echo "zsyncmake is needed for the .zsync update file (apt install zsync)" >&2; exit 1; }
( cd "$OUT" && zsyncmake -u "NexonicDownloadManager-Linux-$AI.AppImage" -o "$ZS" "NexonicDownloadManager-Linux-$AI.AppImage" )

echo "== .deb"
D="$OUT/deb"
mkdir -p "$D/DEBIAN" "$D/opt" "$D/usr/bin" "$D/usr/share/applications" "$D/usr/share/icons/hicolor/256x256/apps" "$D/usr/share/doc/nexonic-download-manager"
cp -a "$SYS" "$D/opt/nexonic-download-manager"
printf '#!/bin/sh\nexec /opt/nexonic-download-manager/NexonicDownloadManager "$@"\n' > "$D/usr/bin/nexonic-download-manager"
printf '#!/bin/sh\nexec /opt/nexonic-download-manager/nexonic-host "$@"\n' > "$D/usr/bin/ndm"
chmod 755 "$D/usr/bin/nexonic-download-manager" "$D/usr/bin/ndm"
cp "$PKG/nexonic-download-manager.desktop" "$D/usr/share/applications/"
for size in 16 24 32 48 64 128 256 512; do
  target="$D/usr/share/icons/hicolor/${size}x${size}/apps"
  mkdir -p "$target"
  cp "$ROOT/branding/universal/generated/linux/$size.png" "$target/nexonic-download-manager.png"
done
cp "$SYS/THIRD-PARTY-NOTICES.txt" "$D/usr/share/doc/nexonic-download-manager/copyright"
SIZE="$(du -sk "$D" --exclude=DEBIAN | cut -f1)"
cat > "$D/DEBIAN/control" <<CONTROL
Package: nexonic-download-manager
Version: $VERSION
Section: net
Priority: optional
Architecture: $DEB
Installed-Size: $SIZE
Maintainer: Shahrukh Khalid <90670731+Shahrukhalid@users.noreply.github.com>
Homepage: https://github.com/Shahrukhalid/nexonic-download-manager
Depends: dotnet-runtime-10.0, libc6 (>= 2.31), libstdc++6, zlib1g, libfontconfig1, libx11-6, libice6, libsm6, libssl3 | libssl3t64 | libssl1.1
Recommends: libsecret-tools, xdg-utils, libnotify-bin
Suggests: clamav, gnome-keyring
Description: Fast download manager with queues, a scheduler and video downloads
 Nexonic Download Manager splits each download into parts that arrive at the
 same time, pauses and resumes, runs queues on a timetable, downloads numbered
 series and whole website sections, and saves videos from YouTube and other
 sites. Works with Chrome, Edge, Brave, Vivaldi, Opera and Firefox.
CONTROL
dpkg-deb --root-owner-group -Zxz -z9 --build "$D" "$OUT/nexonic-download-manager_${VERSION}_${DEB}.deb" >/dev/null

echo "== .rpm"
R="$OUT/rpm"
mkdir -p "$R/SPECS" "$R/BUILD" "$R/RPMS" "$R/stage"
cp -a "$D/opt" "$D/usr" "$R/stage/"
rm -rf "$R/stage/usr/share/doc"
cat > "$R/SPECS/nexonic-download-manager.spec" <<SPEC
%global debug_package %{nil}
%define _binary_payload w9.xzdio
%global __strip /bin/true
%global _build_id_links none
%define __os_install_post %{nil}
Name: nexonic-download-manager
Version: $VERSION
Release: 1
Summary: Fast download manager with queues, a scheduler and video downloads
License: MIT OR Apache-2.0
URL: https://github.com/Shahrukhalid/nexonic-download-manager
AutoReqProv: no
Requires: fontconfig
Requires: dotnet-runtime-10.0
Recommends: xdg-utils
Recommends: libnotify

%description
Nexonic Download Manager splits each download into parts that arrive at the same time, pauses and resumes, runs
queues on a timetable, downloads numbered series and whole website sections, and saves videos from YouTube and
other sites. Works with Chrome, Edge, Brave, Vivaldi, Opera and Firefox.

%install
mkdir -p %{buildroot}
cp -a $R/stage/. %{buildroot}/

%files
/opt/nexonic-download-manager
/usr/bin/nexonic-download-manager
/usr/bin/ndm
/usr/share/applications/nexonic-download-manager.desktop
/usr/share/icons/hicolor/*/apps/nexonic-download-manager.png
SPEC
rpmbuild -bb --quiet --define "_topdir $R" --target "$RPM" "$R/SPECS/nexonic-download-manager.spec"
cp "$R/RPMS/$RPM/nexonic-download-manager-$VERSION-1.$RPM.rpm" "$OUT/"

rm -rf "$AD" "$D" "$R"
( cd "$OUT" && sha256sum *.AppImage *.AppImage.zsync *.deb *.rpm > SHA256SUMS-linux-$ARCH.txt )
echo "Built in $OUT:"
ls -la "$OUT"

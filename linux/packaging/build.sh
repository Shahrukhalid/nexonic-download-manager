#!/usr/bin/env bash
# Builds the Linux app for this machine's processor and packs it as an AppImage, a .deb and a .rpm.
#   linux/packaging/build.sh x64|arm64 FFMPEG_DIR TOOLS_DIR
# FFMPEG_DIR holds ffmpeg and ffprobe (scripts/build-ffmpeg.sh linux), TOOLS_DIR yt-dlp and qjs (scripts/fetch-video-tools.sh).
# Needs: .NET 8 SDK, python3, dpkg-deb, rpmbuild. Output: build/linux-<arch>/
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
APP="$OUT/app"
rm -rf "$OUT"
mkdir -p "$APP"

echo "== App ($RID)"
dotnet publish "$ROOT/linux/App/App.csproj" -c Release -r "$RID" --self-contained -p:Version="$VERSION" -p:DebugType=embedded -o "$APP" -nologo
# The browser helper and ndm command share the app's runtime: only its own files are added.
dotnet publish "$ROOT/windows/Host/Host.csproj" -c Release -r "$RID" --self-contained -p:Version="$VERSION" \
  -p:InvariantGlobalization=true -o "$OUT/host" -nologo
cp "$OUT/host"/nexonic-host "$OUT/host"/nexonic-host.dll "$OUT/host"/nexonic-host.deps.json "$OUT/host"/nexonic-host.runtimeconfig.json "$APP/"
rm -rf "$OUT/host"
find "$APP" -name "*.pdb" -delete

echo "== Video engine and browser extension"
cp "$FF/ffmpeg" "$FF/ffprobe" "$TOOLS/yt-dlp" "$TOOLS/qjs" "$APP/"
chmod 755 "$APP/ffmpeg" "$APP/ffprobe" "$APP/yt-dlp" "$APP/qjs" "$APP/NexonicDownloadManager" "$APP/nexonic-host"
python3 "$ROOT/scripts/build-extensions.py" >/dev/null
cp -R "$ROOT/build/extension-chrome" "$APP/Extension"
cp "$ROOT/build/NexonicFirefoxAddon-$VERSION.xpi" "$APP/Firefox Add-on.xpi"
cp "$PKG/nexonic-download-manager.png" "$APP/"
cp "$ROOT/LICENSE" "$APP/LICENSE.txt"
cp "$ROOT/LICENSE-APACHE" "$APP/LICENSE-APACHE.txt"
cp "$ROOT/TERMS.md" "$APP/TERMS.md"
cat "$ROOT/packaging/THIRD-PARTY-NOTICES.txt" "$PKG/LINUX-NOTICES.txt" > "$APP/THIRD-PARTY-NOTICES.txt"
cp "$FF"/NOTICE-video-tools-1.txt "$TOOLS"/NOTICE-video-tools-2.txt "$APP/" 2>/dev/null || true

echo "== AppImage"
AD="$OUT/AppDir"
mkdir -p "$AD/usr/lib"
cp -a "$APP" "$AD/usr/lib/nexonic-download-manager"
cp "$PKG/AppRun" "$AD/AppRun"
chmod 755 "$AD/AppRun"
sed 's|^Exec=.*|Exec=NexonicDownloadManager %U|' "$PKG/nexonic-download-manager.desktop" > "$AD/nexonic-download-manager.desktop"
cp "$PKG/nexonic-download-manager.png" "$AD/nexonic-download-manager.png"
ln -s nexonic-download-manager.png "$AD/.DirIcon"
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
APPIMAGE_EXTRACT_AND_RUN=1 ARCH="$AI" "$TOOLDIR/appimagetool-$HOST_AI.AppImage" --no-appstream --runtime-file "$TOOLDIR/runtime-$AI" \
  "$AD" "$OUT/NexonicDownloadManager-Linux-$AI.AppImage"

echo "== .deb"
D="$OUT/deb"
mkdir -p "$D/DEBIAN" "$D/opt" "$D/usr/bin" "$D/usr/share/applications" "$D/usr/share/icons/hicolor/256x256/apps" "$D/usr/share/doc/nexonic-download-manager"
cp -a "$APP" "$D/opt/nexonic-download-manager"
printf '#!/bin/sh\nexec /opt/nexonic-download-manager/NexonicDownloadManager "$@"\n' > "$D/usr/bin/nexonic-download-manager"
printf '#!/bin/sh\nexec /opt/nexonic-download-manager/nexonic-host "$@"\n' > "$D/usr/bin/ndm"
chmod 755 "$D/usr/bin/nexonic-download-manager" "$D/usr/bin/ndm"
cp "$PKG/nexonic-download-manager.desktop" "$D/usr/share/applications/"
cp "$PKG/nexonic-download-manager.png" "$D/usr/share/icons/hicolor/256x256/apps/"
cp "$APP/THIRD-PARTY-NOTICES.txt" "$D/usr/share/doc/nexonic-download-manager/copyright"
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
Depends: libc6 (>= 2.31), libstdc++6, zlib1g, libfontconfig1, libx11-6, libice6, libsm6, libssl3 | libssl3t64 | libssl1.1
Recommends: libsecret-tools, xdg-utils, libnotify-bin
Suggests: clamav, gnome-keyring
Description: Fast download manager with queues, a scheduler and video downloads
 Nexonic Download Manager splits each download into parts that arrive at the
 same time, pauses and resumes, runs queues on a timetable, downloads numbered
 series and whole website sections, and saves videos from YouTube and other
 sites. Works with Chrome, Edge, Brave, Vivaldi, Opera and Firefox.
CONTROL
dpkg-deb --root-owner-group -Zxz --build "$D" "$OUT/nexonic-download-manager_${VERSION}_${DEB}.deb" >/dev/null

echo "== .rpm"
R="$OUT/rpm"
mkdir -p "$R/SPECS" "$R/BUILD" "$R/RPMS" "$R/stage"
cp -a "$D/opt" "$D/usr" "$R/stage/"
rm -rf "$R/stage/usr/share/doc"
cat > "$R/SPECS/nexonic-download-manager.spec" <<SPEC
%global debug_package %{nil}
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
/usr/share/icons/hicolor/256x256/apps/nexonic-download-manager.png
SPEC
rpmbuild -bb --quiet --define "_topdir $R" --target "$RPM" "$R/SPECS/nexonic-download-manager.spec"
cp "$R/RPMS/$RPM/nexonic-download-manager-$VERSION-1.$RPM.rpm" "$OUT/"

rm -rf "$AD" "$D" "$R"
( cd "$OUT" && sha256sum *.AppImage *.deb *.rpm > SHA256SUMS-linux-$ARCH.txt )
echo "Built in $OUT:"
ls -la "$OUT"

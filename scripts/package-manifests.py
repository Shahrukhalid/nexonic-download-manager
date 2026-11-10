#!/usr/bin/env python3
"""Writes the package-manager files for a published release, with the checksums GitHub lists for its files:
  packaging/winget/   winget manifests (to submit to microsoft/winget-pkgs)
  bucket/             Scoop bucket (scoop bucket add nexonic https://github.com/Shahrukhalid/nexonic-download-manager)
  packaging/homebrew/ Homebrew cask (for a tap repository named homebrew-nexonic)
Usage: scripts/package-manifests.py [VERSION]   (default: the VERSION file; the release must be published)
Nothing is submitted anywhere; the files are only written into this repository for review.
"""
import json, sys, urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REPO = "Shahrukhalid/nexonic-download-manager"
VERSION = sys.argv[1] if len(sys.argv) > 1 else (ROOT / "VERSION").read_text().strip()
BASE = f"https://github.com/{REPO}/releases/download/v{VERSION}"
DESCRIPTION = ("Free, open-source download manager: up to 256 automatic connections per file, every file checked, pause and resume, queues and a scheduler, "
               "batch download, site grabber, browser extension and videos from YouTube and 1,000+ other sites.")

with urllib.request.urlopen(f"{BASE}/latest.json", timeout=60) as r:
    release = json.load(r)
sha = {a["name"]: a["digest"].split(":", 1)[1] for a in release["assets"] if a.get("digest", "").startswith("sha256:")}
for need in ("NexonicDownloadManager-Windows-Setup.exe", "NexonicDownloadManager-Windows-Portable.zip", "NexonicDownloadManager-Mac.dmg"):
    if need not in sha:
        sys.exit(f"{need} has no checksum in v{VERSION}'s latest.json")

ident = "Shahrukhalid.NexonicDownloadManager"
# Windows on Arm files (from 0.28.0): listed when the release has them.
ARM_SETUP, ARM_ZIP = "NexonicDownloadManager-Windows-ARM64-Setup.exe", "NexonicDownloadManager-Windows-ARM64-Portable.zip"
ARM_INSTALLER = (f"  - Architecture: arm64\n    InstallerUrl: {BASE}/{ARM_SETUP}\n    InstallerSha256: {sha[ARM_SETUP].upper()}\n"
                 if ARM_SETUP in sha else "")
w = ROOT / "packaging" / "winget"
w.mkdir(parents=True, exist_ok=True)
head = "# Created with scripts/package-manifests.py\n"
(w / f"{ident}.yaml").write_text(head + f"""PackageIdentifier: {ident}
PackageVersion: {VERSION}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
""")
(w / f"{ident}.installer.yaml").write_text(head + f"""PackageIdentifier: {ident}
PackageVersion: {VERSION}
InstallerType: inno
Scope: user
InstallModes:
  - interactive
  - silent
UpgradeBehavior: install
ProductCode: '{{6F1C2E4B-8B7D-4C55-9E2A-3D9B1F0A7C21}}_is1'
Commands:
  - ndm
Installers:
  - Architecture: x64
    InstallerUrl: {BASE}/NexonicDownloadManager-Windows-Setup.exe
    InstallerSha256: {sha['NexonicDownloadManager-Windows-Setup.exe'].upper()}
{ARM_INSTALLER}ManifestType: installer
ManifestVersion: 1.6.0
""")
(w / f"{ident}.locale.en-US.yaml").write_text(head + f"""PackageIdentifier: {ident}
PackageVersion: {VERSION}
PackageLocale: en-US
Publisher: Shahrukh Khalid
PublisherUrl: https://www.shahrukhalid.com
PublisherSupportUrl: https://github.com/{REPO}/issues
PackageName: Nexonic Download Manager
PackageUrl: https://github.com/{REPO}
License: MIT OR Apache-2.0
LicenseUrl: https://github.com/{REPO}/blob/main/LICENSE
ShortDescription: Free, open-source download manager with video downloads.
Description: {json.dumps(DESCRIPTION)}
Tags:
  - download-manager
  - downloader
  - video-downloader
  - queue
  - resume
ReleaseNotesUrl: https://github.com/{REPO}/releases/tag/v{VERSION}
ManifestType: defaultLocale
ManifestVersion: 1.6.0
""")

bucket = ROOT / "bucket"
bucket.mkdir(exist_ok=True)
scoop = {
    "version": VERSION,
    "description": DESCRIPTION,
    "homepage": f"https://github.com/{REPO}",
    "license": "MIT OR Apache-2.0",
    "architecture": {"64bit": {
        "url": f"{BASE}/NexonicDownloadManager-Windows-Portable.zip",
        "hash": sha["NexonicDownloadManager-Windows-Portable.zip"],
    }, **({"arm64": {"url": f"{BASE}/{ARM_ZIP}", "hash": sha[ARM_ZIP]}} if ARM_ZIP in sha else {})},
    "bin": [["nexonic-host.exe", "ndm"]],
    "shortcuts": [["NexonicDownloadManager.exe", "Nexonic Download Manager"]],
    "checkver": {"github": f"https://github.com/{REPO}"},
    "autoupdate": {"architecture": {"64bit": {
        "url": f"https://github.com/{REPO}/releases/download/v$version/NexonicDownloadManager-Windows-Portable.zip",
    }, **({"arm64": {"url": f"https://github.com/{REPO}/releases/download/v$version/{ARM_ZIP}"}} if ARM_ZIP in sha else {})}},
}
(bucket / "nexonic-download-manager.json").write_text(json.dumps(scoop, indent=4) + "\n")

h = ROOT / "packaging" / "homebrew"
h.mkdir(parents=True, exist_ok=True)
(h / "nexonic-download-manager.rb").write_text(f"""# Created with scripts/package-manifests.py
cask "nexonic-download-manager" do
  version "{VERSION}"
  sha256 "{sha['NexonicDownloadManager-Mac.dmg']}"

  url "https://github.com/{REPO}/releases/download/v#{{version}}/NexonicDownloadManager-Mac.dmg"
  name "Nexonic Download Manager"
  desc "Download manager with queues, a scheduler and video downloads"
  homepage "https://github.com/{REPO}"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on macos: ">= :ventura"

  app "Nexonic Download Manager.app"
  binary "#{{appdir}}/Nexonic Download Manager.app/Contents/MacOS/nexonic-host", target: "ndm"

  zap trash: [
    "~/Library/Application Support/Nexonic Download Manager",
    "~/Library/Preferences/com.nexonic.downloadmanager.plist",
  ]
end
""")
print(f"Wrote the winget, Scoop and Homebrew files for {VERSION}")

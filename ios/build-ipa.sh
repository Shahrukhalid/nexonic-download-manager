#!/usr/bin/env bash
# Builds a signed .ipa on your Mac (Xcode 16 and XcodeGen: brew install xcodegen).
# Usage:  NDM_TEAM_ID=ABCDE12345 bash ios/build-ipa.sh [development|ad-hoc|app-store-connect]
# The team ID is in Xcode → Settings → Accounts, or developer.apple.com → Membership.
# Xcode creates the App ID, the App Group (group.com.nexonic.downloadmanager) and the profiles automatically.
set -euo pipefail
cd "$(dirname "$0")"
: "${NDM_TEAM_ID:?Set NDM_TEAM_ID to your Apple developer team ID}"
METHOD="${1:-development}"
NDM_VERSION="$(cat ../VERSION)" xcodegen generate
rm -rf build && mkdir -p build
xcodebuild archive -project Nexonic.xcodeproj -scheme Nexonic -configuration Release -destination "generic/platform=iOS" \
  -archivePath build/Nexonic.xcarchive -allowProvisioningUpdates DEVELOPMENT_TEAM="$NDM_TEAM_ID"
cat > build/ExportOptions.plist <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>method</key><string>$METHOD</string>
  <key>teamID</key><string>$NDM_TEAM_ID</string>
  <key>signingStyle</key><string>automatic</string>
</dict></plist>
PLIST
xcodebuild -exportArchive -archivePath build/Nexonic.xcarchive -exportOptionsPlist build/ExportOptions.plist \
  -exportPath build/ipa -allowProvisioningUpdates
mv build/ipa/*.ipa "build/NexonicDownloadManager-iOS.ipa"
echo "DONE: ios/build/NexonicDownloadManager-iOS.ipa"

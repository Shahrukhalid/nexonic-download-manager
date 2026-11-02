#!/usr/bin/env bash
# Takes the iPhone and iPad screenshots in the simulator (used by the iOS workflow).
# Needs scripts/demo-server.py on port 8099 and the demo page on port 8098 (the simulator shares this Mac's network).
set -euo pipefail
APP="$1"          # path to Nexonic.app built for the simulator
BUNDLE=com.nexonic.downloadmanager
mkdir -p shots
device() { xcrun simctl list devices available -j | python3 -c "import json,sys;d=json.load(sys.stdin)['devices'];print(next(x['udid'] for r in sorted(d,reverse=True) for x in d[r] if x['name'].startswith('$1')))"; }
shoot() {
  local sim=$1 prefix=$2; shift 2
  xcrun simctl boot "$sim" 2>/dev/null || true
  xcrun simctl bootstatus "$sim" -b >/dev/null
  xcrun simctl status_bar "$sim" override --time 9:41 --dataNetwork wifi --wifiMode active --wifiBars 3 --cellularMode active --cellularBars 4 --batteryState charged --batteryLevel 100
  xcrun simctl install "$sim" "$APP"
  for spec in "$@"; do
    IFS=: read -r name screen look wait <<< "$spec"
    xcrun simctl ui "$sim" appearance "${look:-light}"
    xcrun simctl terminate "$sim" "$BUNDLE" 2>/dev/null || true
    xcrun simctl launch "$sim" "$BUNDLE" -ndm_demo 1 -ndm_screen "$screen" >/dev/null
    sleep "${wait:-7}"
    xcrun simctl io "$sim" screenshot "shots/$prefix-$name.png" >/dev/null
    echo "$prefix-$name.png"
  done
  xcrun simctl ui "$sim" appearance light
  xcrun simctl shutdown "$sim" || true
}
shoot "$(device 'iPhone 16 Pro')" ios \
  main:downloads:light:8 detail:detail:light:8 add:add:light:9 browser:browser:light:12 found:found:light:13 setup:setup:light:6 main-dark:downloads:dark:8
shoot "$(device 'iPad Pro 13')" ipad main:detail:light:9 browser:found:light:13
ls -la shots

#!/usr/bin/env bash
# Takes the iPhone and iPad screenshots in the simulator (used by the iOS workflow).
# Needs scripts/demo-server.py on port 8099 and the demo page on port 8098 (the simulator shares this Mac's network).
set -euo pipefail
APP="$1"          # path to Nexonic.app built for the simulator
BUNDLE=com.nexonic.downloadmanager
mkdir -p shots
# Every simulator command prints what it does and gives up after a time limit, so nothing can hang the run.
run() {
  local t=$1; shift; echo "+ $*"
  # Runs the command in its own process group and kills the whole group (including simctl's helpers) at the time limit.
  perl -e 'my $t=shift; my $pid=fork; if(!$pid){setpgrp(0,0); exec @ARGV; exit 127}
    local $SIG{ALRM}=sub{kill "KILL",-$pid; print "  (timed out after ${t}s)\n"; exit 124}; alarm $t; waitpid($pid,0); exit($?>>8)' "$t" "$@" \
    || echo "  (failed: $?)"
}
device() { xcrun simctl list devices available -j | python3 -c "import json,sys;d=json.load(sys.stdin)['devices'];print(next(x['udid'] for r in sorted(d,reverse=True) for x in d[r] if x['name'].startswith('$1')))"; }
shoot() {
  local sim=$1 prefix=$2; shift 2
  run 120 xcrun simctl boot "$sim"
  run 600 xcrun simctl bootstatus "$sim" -b
  run 60 xcrun simctl status_bar "$sim" override --time 9:41 --dataNetwork wifi --wifiMode active --wifiBars 3 --cellularMode active --cellularBars 4 --batteryState charged --batteryLevel 100
  run 180 xcrun simctl install "$sim" "$APP"
  # The first launch after installing can take minutes on CI; do it once before the pictures.
  run 240 xcrun simctl launch "$sim" "$BUNDLE"
  sleep 5
  for spec in "$@"; do
    IFS=: read -r name screen look wait <<< "$spec"
    run 60 xcrun simctl ui "$sim" appearance "${look:-light}"
    run 60 xcrun simctl terminate "$sim" "$BUNDLE"
    run 60 xcrun simctl launch "$sim" "$BUNDLE" -ndm_demo 1 -ndm_screen "$screen"
    sleep "${wait:-7}"
    run 60 xcrun simctl io "$sim" screenshot "shots/$prefix-$name.png"
    echo "$prefix-$name.png"
  done
  run 60 xcrun simctl ui "$sim" appearance light
  run 120 xcrun simctl shutdown "$sim"
}
# Use the iPhone simulator the tests already started ($SIM from the workflow); a cold first boot takes 10+ minutes on CI.
IPHONE="${SIM:-$(device 'iPhone 16 Pro')}"
IPAD=$(device 'iPad Pro 13' || device 'iPad Air' || true)
shoot "$IPHONE" ios \
  main:downloads:light:8 detail:detail:light:8 add:add:light:9 browser:browser:light:12 found:found:light:13 setup:setup:light:6 main-dark:downloads:dark:8
# The iPad can be slow to boot on CI: give it 8 minutes, and never block the run.
if [ -n "$IPAD" ]; then
  ( shoot "$IPAD" ipad main:detail:light:9 browser:found:light:13 ) &
  pid=$!
  for i in $(seq 1 480); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done
  if kill -0 "$pid" 2>/dev/null; then echo "iPad screenshots took too long; skipped."; kill "$pid" 2>/dev/null || true; fi
fi
ls -la shots

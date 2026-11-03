#!/usr/bin/env bash
# Saves pictures to the "screenshots" branch (used by the README and the website).
# Usage: publish-screenshots.sh <folder with .png files> <message> <old pictures to replace, e.g. 'ios-*.png'>...
# Works in a separate folder, so build files and changes in the main checkout are never touched.
set -euo pipefail
SRC="$(cd "$1" && pwd)"; MSG="$2"; shift 2
PUB="$(mktemp -d)/pub"
git fetch -q origin screenshots:refs/remotes/origin/screenshots
git worktree add -q -B screenshots "$PUB" origin/screenshots
cd "$PUB"
for pattern in "$@"; do rm -f $pattern; done
cp "$SRC"/*.png .
git add -A
git -c user.name="Shahrukh Khalid" -c user.email="90670731+Shahrukhalid@users.noreply.github.com" commit -qm "$MSG" || { echo "No changes."; exit 0; }
# Another workflow may have saved its pictures at the same time: take those and try again.
for i in 1 2 3 4; do
  git push -q origin screenshots && exit 0
  sleep 5; git pull -q --rebase origin screenshots
done
exit 1

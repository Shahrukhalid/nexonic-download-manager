#!/usr/bin/env python3
"""Builds the browser extensions from extension/:
  build/extension-chrome/   + build/NexonicExtension-<version>.zip      (Chrome, Edge, Brave, Vivaldi, Opera)
  build/extension-firefox/  + build/NexonicFirefoxAddon-<version>.xpi   (Firefox)
"""
import json, re, shutil, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "extension"
OUT = ROOT / "build"
VERSION = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
FIREFOX_ID = "{dec29d70-4c6f-43f7-aacd-af2ae04cd4c2}"
SKIP = {"helpers.test.js", "helpers.fuzz.test.js", "package.json", "package-lock.json", "node_modules"}


def copy(dest: Path):
    if dest.exists():
        shutil.rmtree(dest)
    shutil.copytree(SRC, dest, ignore=lambda d, names: [n for n in names if n in SKIP])


def zip_dir(folder: Path, target: Path):
    target.unlink(missing_ok=True)
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(folder.rglob("*")):
            if f.is_file():
                z.write(f, f.relative_to(folder).as_posix())


def main():
    OUT.mkdir(exist_ok=True)
    manifest = json.loads((SRC / "manifest.json").read_text(encoding="utf-8"))
    manifest["version"] = VERSION

    chrome = OUT / "extension-chrome"
    copy(chrome)
    (chrome / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    zip_dir(chrome, OUT / f"NexonicExtension-{VERSION}.zip")

    firefox = OUT / "extension-firefox"
    copy(firefox)
    m = dict(manifest)
    m.pop("key", None)
    m.pop("minimum_chrome_version", None)
    # Firefox runs the background as a page script: join the helpers and the background into one classic script.
    helpers = re.sub(r"^export ", "", (SRC / "helpers.js").read_text(encoding="utf-8"), flags=re.M)
    background = re.sub(r'^import .*?;\n', "", (SRC / "background.js").read_text(encoding="utf-8"), flags=re.S | re.M)
    (firefox / "background.js").write_text("// Built from helpers.js and background.js\n" + helpers + "\n" + background, encoding="utf-8")
    m["background"] = {"scripts": ["background.js"]}
    m["browser_specific_settings"] = {
        "gecko": {"id": FIREFOX_ID, "strict_min_version": "121.0", "data_collection_permissions": {"required": ["none"]}}
    }
    (firefox / "manifest.json").write_text(json.dumps(m, indent=2) + "\n", encoding="utf-8")
    zip_dir(firefox, OUT / f"NexonicFirefoxAddon-{VERSION}.xpi")
    print(f"Built the Chrome extension and the Firefox add-on {VERSION} in {OUT}")


if __name__ == "__main__":
    sys.exit(main())

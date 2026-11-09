#!/usr/bin/env python3
"""Builds the browser extensions from extension/:
  build/extension-chrome/   + build/NexonicExtension-<version>.zip      (Chrome, Edge, Brave, Vivaldi, Opera)
  build/extension-firefox/  + build/NexonicFirefoxAddon-<version>.xpi   (Firefox)
  build/store/              store uploads: Chrome Web Store and Edge Add-ons zips (no "key": the store gives the ID),
                            addons.mozilla.org zip and a source zip for its review (see docs/STORES.md)
"""
import json, re, shutil, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "extension"
OUT = ROOT / "build"
VERSION = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
FIREFOX_ID = "{dec29d70-4c6f-43f7-aacd-af2ae04cd4c2}"
SKIP = {"helpers.test.js", "helpers.fuzz.test.js", "store.test.js", "package.json", "package-lock.json", "node_modules"}


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

    # Store uploads. The stores assign their own extension ID, so the development "key" is left out; the browser helper
    # accepts the store IDs listed in NativeMessaging.AllowedExtensionIds once they are known.
    store = OUT / "store"
    if store.exists():
        shutil.rmtree(store)
    store.mkdir()
    upload = store / "chromium"
    shutil.copytree(chrome, upload)
    m = dict(manifest)
    m.pop("key", None)
    (upload / "manifest.json").write_text(json.dumps(m, indent=2) + "\n", encoding="utf-8")
    zip_dir(upload, store / f"NexonicExtension-chrome-web-store-{VERSION}.zip")
    zip_dir(upload, store / f"NexonicExtension-edge-add-ons-{VERSION}.zip")
    shutil.rmtree(upload)
    zip_dir(firefox, store / f"NexonicFirefoxAddon-amo-{VERSION}.zip")
    # addons.mozilla.org asks for the source when an upload's code was combined: background.js is helpers.js plus
    # background.js, built by this script.
    with zipfile.ZipFile(store / f"NexonicFirefoxAddon-amo-source-{VERSION}.zip", "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(SRC.rglob("*")):
            if f.is_file() and "node_modules" not in f.parts:
                z.write(f, ("extension" / f.relative_to(SRC)).as_posix())
        z.write(Path(__file__), "scripts/build-extensions.py")
        z.write(ROOT / "VERSION", "VERSION")
        z.writestr("BUILD.txt", "Build: python3 scripts/build-extensions.py (Python 3.8+, no other tools). The add-on is build/extension-firefox.\n")
    print(f"Built the Chrome extension and the Firefox add-on {VERSION} in {OUT}")


if __name__ == "__main__":
    sys.exit(main())

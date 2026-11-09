# Layout of the Mac installer window (read by dmgbuild from scripts/build-app.sh).
import os.path

_missing = [k for k in ("app", "extension", "xpi", "icns", "background") if not defines.get(k)]
if _missing:
    raise SystemExit(f"dmg-settings.py: missing -D {', '.join(_missing)} (pass them from scripts/build-app.sh)")

app = defines["app"]
files = [app, defines["extension"], defines["xpi"]]
symlinks = {"Applications": "/Applications"}
icon = defines["icns"]
background = defines["background"]
format = "ULMO"  # LZMA: smaller than UDZO (zlib); opens on macOS 10.15 and later (the app needs 13)
window_rect = ((200, 140), (660, 500))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
icon_size = 96

# Terms of use shown when the disk image is opened; the user must press Agree.
_terms_path = defines.get("terms", "packaging/TERMS.txt")
if not os.path.isfile(_terms_path):
    raise SystemExit(f"dmg-settings.py: terms file not found: {_terms_path}")
with open(_terms_path, encoding="utf-8") as f:
    _terms = f.read()
license = {
    "default-language": "en_US",
    "licenses": {"en_US": _terms},
    "buttons": {
        "en_US": (
            "English",
            "Agree",
            "Disagree",
            "Print",
            "Save",
            "To install Nexonic Download Manager, please read and agree to the Terms of Use and License Agreement. "
            "Click Disagree if you do not agree.",
        )
    },
}
text_size = 13
icon_locations = {
    os.path.basename(app): (180, 165),
    "Applications": (480, 165),
    os.path.basename(defines["extension"]): (250, 360),
    os.path.basename(defines["xpi"]): (410, 360),
}

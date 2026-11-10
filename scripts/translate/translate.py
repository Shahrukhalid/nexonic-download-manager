#!/usr/bin/env python3
"""
Interface translations for every Nexonic app (Windows/Linux, Mac/iPhone, Android, browser extension).

  translate.py sources                       list the English texts of every app (scripts/translate/sources.json)
  translate.py translate --langs de,fr --model DIR --out cache/
                                             machine-translate the texts each language is missing (MADLAD-400 with
                                             CTranslate2); writes cache/<code>.json
  translate.py apply [--cache cache/]        write every app's language files and language lists; texts with no
                                             translation yet stay in English

Reviewed languages keep their translations; only missing texts are added. New languages are marked as machine
translations in the apps. App names, links, file types, shortcuts and placeholders are never translated.
"""
import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
CATALOG = json.loads((HERE / "languages.json").read_text(encoding="utf-8"))["languages"]
BY_CODE = {l["code"]: l for l in CATALOG}
MACHINE = "Machine translation, not reviewed"

DESKTOP = ROOT / "windows/Core/Locales"
ANDROID = ROOT / "android/app/src/main/assets/locales"
APPLE = ROOT / "packaging/Localizations"
EXT = ROOT / "extension/locales"
EXT_STORE = ROOT / "extension/_locales"

# ---------- reading and writing the apps' files ----------

def read_json(path):
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else None

def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

STRINGS_LINE = re.compile(r'^"((?:[^"\\]|\\.)*)"\s*=\s*"((?:[^"\\]|\\.)*)";\s*$')

def unescape(s):
    return re.sub(r'\\(.)', lambda m: {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), s)

def escape(s):
    return s.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n")

def read_strings(path):
    if not path.exists():
        return None, {}
    header, pairs = None, {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if header is None and line.startswith("/*"):
            header = line
        m = STRINGS_LINE.match(line)
        if m:
            pairs[unescape(m.group(1))] = unescape(m.group(2))
    return header, pairs

def write_strings(path, header, pairs, order):
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = [header, ""] + [f'"{escape(k)}" = "{escape(pairs[k])}";' for k in order]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")

def sources():
    """The English texts of each app: {app: [texts]}."""
    desktop = list(read_json(DESKTOP / "en.json")["strings"])
    android = list(read_json(ANDROID / "en.json")["strings"])
    _, apple = read_strings(APPLE / "en.lproj/Localizable.strings")
    ext_files = sorted(EXT.glob("*.json"))
    ext = list(read_json(ext_files[0])["strings"]) if ext_files else []
    store = read_json(EXT_STORE / "en/messages.json")
    return {"desktop": desktop, "android": android, "apple": list(apple), "extension": ext,
            "store": [store["extName"]["message"], store["extDescription"]["message"]]}

def existing(code):
    """What a language already has, for every app: {english: translation} (only real translations)."""
    lang = BY_CODE[code]
    have = {}
    for path in (DESKTOP / f"{code}.json", ANDROID / f"{code}.json", EXT / f"{code}.json"):
        f = read_json(path)
        if f:
            have.update({k: v for k, v in f["strings"].items() if v and (v != k or lang["reviewed"]) and (lang["reviewed"] or good(k, v))})
    if lang["apple"]:
        _, pairs = read_strings(APPLE / f"{lang['apple']}.lproj/Localizable.strings")
        have.update({k: v for k, v in pairs.items() if v and (v != k or lang["reviewed"]) and (lang["reviewed"] or good(k, v))})
    return have

# ---------- keeping names, links and placeholders ----------

KEEP = re.compile(
    r"https?://\S+|nexonic://\S+|\{\w*\}|%(?:\d+\$)?(?:@|lld|ld|d|s|f)|\((?:Ctrl|Cmd|⌘)\+[^)]+\)|Ctrl\+\w+|⌘⇧?\w|\bF\d{1,2}\b"
    r"|Nexonic Download Manager|Downloads/Nexonic|Files › Nexonic|\.(?:zip|7z|rar|tar|torrent|exe|dmg|deb|rpm|AppImage)\b"
    r"|\b(?:Nexonic|ndm|yt-dlp|gallery-dl|PAC|HTTP|HTTPS|SOCKS5|URL|HH:mm|MD5|SHA-\d+|KB/s|MB|GB|Wi-Fi|VPN|QR|Reddit|Pixiv"
    r"|DeviantArt|Imgur|YouTube|Chrome|Firefox|Edge|Safari|Windows|macOS|Linux|Android|iPhone|iPad|GitHub)\b")

def mask(text):
    kept = []
    def sub(m):
        kept.append(m.group(0))
        return f"X{len(kept)}X"
    return KEEP.sub(sub, text), kept

def unmask(text, kept):
    for i, k in enumerate(kept, 1):
        if text.count(f"X{i}X") != 1:
            return None
        text = text.replace(f"X{i}X", k)
    return text

def keeps(english, translated):
    """Every name, link, placeholder and shortcut of the English text is still in the translation."""
    return all(translated.count(k) >= english.count(k) for k in set(KEEP.findall(english)))

def good(english, translated):
    """A machine translation is used only when it keeps every protected part and looks sane."""
    if not translated or not translated.strip():
        return False
    if not keeps(english, translated):
        return False
    if len(translated) > 4 * len(english) + 30:
        return False
    if english.endswith("…") and not translated.rstrip().endswith("…"):
        return False
    return translated.count("\n") == english.count("\n")

# ---------- machine translation ----------

def translate(langs, model_dir, out, batch=24):
    import ctranslate2
    from transformers import AutoTokenizer
    tok = AutoTokenizer.from_pretrained(str(model_dir))
    tr = ctranslate2.Translator(str(model_dir), device="cpu", compute_type="int8", inter_threads=1, intra_threads=0)
    texts = sorted(set(t for app in sources().values() for t in app))
    out.mkdir(parents=True, exist_ok=True)
    for code in langs:
        lang = BY_CODE[code]
        if code == "en":
            continue
        have = existing(code)
        cache_file = out / f"{code}.json"
        done = read_json(cache_file) or {}
        todo = [t for t in texts if t not in have and t not in done]
        print(f"{code}: {len(todo)} texts to translate", flush=True)
        for i in range(0, len(todo), batch):
            chunk = todo[i:i + batch]
            masked = [mask(t) for t in chunk]
            inputs = [tok.convert_ids_to_tokens(tok.encode(f"<2{lang['model']}> {m}")) for m, _ in masked]
            results = tr.translate_batch(inputs, beam_size=2, max_decoding_length=256, max_input_length=512)
            for english, (m, kept), r in zip(chunk, masked, results):
                text = tok.decode(tok.convert_tokens_to_ids(r.hypotheses[0]), skip_special_tokens=True).strip()
                text = unmask(text, kept) if kept else text
                if text is not None and good(english, text):
                    done[english] = text
            write_json(cache_file, done)
        print(f"{code}: {len(done)} in cache", flush=True)

# ---------- writing every app ----------

def apply(cache):
    src = sources()
    for lang in CATALOG:
        code = lang["code"]
        if code == "en":
            continue
        cached = (read_json(cache / f"{code}.json") or {}) if cache else {}
        have = existing(code)
        def pick(english):
            return have.get(english) or cached.get(english) or english
        status = "Reviewed" if lang["reviewed"] else MACHINE
        write_json(DESKTOP / f"{code}.json", {"language": lang["name"], "status": status, "strings": {t: pick(t) for t in src["desktop"]}})
        write_json(ANDROID / f"{code}.json", {"language": lang["name"], "status": status, "strings": {t: pick(t) for t in src["android"]}})
        write_json(EXT / f"{code}.json", {"language": lang["name"], "status": status, "strings": {t: pick(t) for t in src["extension"]}})
        if lang["apple"]:
            header, _ = read_strings(APPLE / f"{lang['apple']}.lproj/Localizable.strings")
            header = header if lang["reviewed"] and header else ("/* Reviewed */" if lang["reviewed"] else f"/* {MACHINE} */")
            write_strings(APPLE / f"{lang['apple']}.lproj/Localizable.strings", header, {t: pick(t) for t in src["apple"]}, src["apple"])
        if lang["chrome"]:
            store = EXT_STORE / lang["chrome"] / "messages.json"
            old = read_json(store)
            name, desc = src["store"]
            n, d = (old["extName"]["message"], old["extDescription"]["message"]) if old else (pick(name), pick(desc))
            write_json(store, {"extName": {"message": n if len(n) <= 45 else name},
                               "extDescription": {"message": d if len(d) <= 132 else desc}})
    write_lists()

def replace_block(path, start, end, body):
    text = path.read_text(encoding="utf-8")
    a, b = text.index(start), text.index(end)
    a = text.index("\n", a) + 1
    b = text.rindex("\n", 0, b) + 1
    path.write_text(text[:a] + body + text[b:], encoding="utf-8")

def write_lists():
    langs = CATALOG
    cs = "".join(f'        new("{l["code"]}", "{l["name"]}", {str(l["rtl"]).lower()}, {str(l["reviewed"]).lower()}),\n' for l in langs)
    replace_block(ROOT / "windows/Core/Localization.cs", "// BEGIN LANGUAGES", "// END LANGUAGES", cs)
    others = [l for l in langs if l["code"] != "en"]
    kt = ("    val languages = listOf(" + ", ".join(f'"{l["code"]}"' for l in others) + ")\n\n"
          "    /** Each language's own name, for the picker. */\n    val names = mapOf(\n"
          + "".join(f'        "{l["code"]}" to "{l["name"]}",\n' for l in langs) + "    )\n\n"
          "    /** Languages written right to left. */\n    val rightToLeft = setOf(" + ", ".join(f'"{l["code"]}"' for l in langs if l["rtl"]) + ")\n")
    replace_block(ROOT / "android/app/src/main/java/com/nexonic/downloadmanager/core/Translations.kt", "// BEGIN LANGUAGES", "// END LANGUAGES", kt)
    apple = [l for l in langs if l["apple"]]
    sw = ("    static let all: [(code: String, name: String)] = [\n"
          + "".join(f'        ("{l["apple"]}", "{l["name"]}"),\n' for l in apple) + "    ]\n\n"
          "    /// Languages written right to left.\n    static let rightToLeft: Set<String> = ["
          + ", ".join(f'"{l["apple"]}"' for l in apple if l["rtl"]) + "]\n")
    for p in ("mac/Sources/NexonicDownloadManager/Language.swift", "ios/App/Language.swift"):
        replace_block(ROOT / p, "// BEGIN LANGUAGES", "// END LANGUAGES", sw)
    js = ("export const LANGUAGES = {\n" + "".join(f'  "{l["code"]}": "{l["name"]}",\n' for l in langs) + "};\n\n"
          "/** Languages written right to left. */\nexport const RIGHT_TO_LEFT = new Set([" + ", ".join(f'"{l["code"]}"' for l in langs if l["rtl"]) + "]);\n")
    replace_block(ROOT / "extension/helpers.js", "// BEGIN LANGUAGES", "// END LANGUAGES", js)
    codes = "".join(f"<string>{l['apple']}</string>" for l in apple)
    for plist in (ROOT / "ios/App/Info.plist", ROOT / "packaging/Info.plist"):
        text = plist.read_text(encoding="utf-8")
        plist.write_text(re.sub(r"(<key>CFBundleLocalizations</key><array>).*?(</array>)", lambda m: m.group(1) + codes + m.group(2), text), encoding="utf-8")
    tags = {"zh-cn": "zh-CN", "zh-tw": "zh-TW", "id": "in", "he": "iw"}
    xml = ('<?xml version="1.0" encoding="utf-8"?>\n<locale-config xmlns:android="http://schemas.android.com/apk/res/android">\n'
           + "".join(f'    <locale android:name="{tags.get(l["code"], l["code"])}" />\n' for l in langs) + "</locale-config>\n")
    (ROOT / "android/app/src/main/res/xml/locales_config.xml").write_text(xml, encoding="utf-8")

def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("sources")
    t = sub.add_parser("translate")
    t.add_argument("--langs", required=True, help="comma-separated codes, or 'all'")
    t.add_argument("--shard", default="0/1", help="this job's part of the languages, e.g. 3/10")
    t.add_argument("--model", required=True, type=Path)
    t.add_argument("--out", required=True, type=Path)
    a = sub.add_parser("apply")
    a.add_argument("--cache", type=Path)
    args = p.parse_args()
    if args.cmd == "sources":
        write_json(HERE / "sources.json", sources())
        print({k: len(v) for k, v in sources().items()})
    elif args.cmd == "translate":
        langs = [l["code"] for l in CATALOG] if args.langs == "all" else args.langs.split(",")
        unknown = [c for c in langs if c not in BY_CODE]
        if unknown:
            sys.exit(f"Unknown languages: {unknown}")
        part, parts = (int(x) for x in args.shard.split("/"))
        langs = [c for c in langs if c != "en"][part::parts]
        translate(langs, args.model, args.out)
    else:
        apply(args.cache)

if __name__ == "__main__":
    main()

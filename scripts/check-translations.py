#!/usr/bin/env python3
"""Checks every app's translations against their English keys.

Errors (CI fails): a placeholder, shortcut or line break that differs from the English text, an empty text, a text in
another writing system than its language's (an unrelated translation), or a broken one (a word repeated over and over,
or far longer than the English). Run with --fix to put the English text back in place of every such text (English is
the fallback when a translation can't be trusted). Untranslated texts are counted, not errors.
"""
import json, re, sys, unicodedata
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LANGS = {l["code"]: l for l in json.loads((ROOT / "scripts/translate/languages.json").read_text())["languages"]}
TOKEN = re.compile(r"\{[^{}\s]*\}|%(?:\d+\$)?(?:l?l?[dfisu@]|\.\d+f)|%%|\((?:Ctrl|Cmd|Shift|Alt|F\d+)[^)]*\)|\\n|\n")
# The writing system each non-Latin language uses (Unicode script names, as unicodedata reports them).
SCRIPTS = {"ar": "ARABIC", "fa": "ARABIC", "ur": "ARABIC", "ps": "ARABIC", "sd": "ARABIC", "ug": "ARABIC", "ckb": "ARABIC",
           "he": "HEBREW", "yi": "HEBREW", "ru": "CYRILLIC", "uk": "CYRILLIC", "be": "CYRILLIC", "bg": "CYRILLIC",
           "mk": "CYRILLIC", "sr": "CYRILLIC", "kk": "CYRILLIC", "ky": "CYRILLIC", "mn": "CYRILLIC", "tg": "CYRILLIC",
           "tt": "CYRILLIC", "el": "GREEK", "hy": "ARMENIAN", "ka": "GEORGIAN", "am": "ETHIOPIC", "hi": "DEVANAGARI",
           "mr": "DEVANAGARI", "ne": "DEVANAGARI", "bn": "BENGALI", "as": "BENGALI", "pa": "GURMUKHI", "gu": "GUJARATI",
           "or": "ORIYA", "ta": "TAMIL", "te": "TELUGU", "kn": "KANNADA", "ml": "MALAYALAM", "si": "SINHALA", "th": "THAI",
           "lo": "LAO", "my": "MYANMAR", "km": "KHMER", "ko": "HANGUL", "ja": "CJK|HIRAGANA|KATAKANA",
           "zh-cn": "CJK", "zh-tw": "CJK"}
OTHER = re.compile(r"[Ѐ-ӿ֐-׿؀-ۿऀ-෿฀-໿ᄀ-ᇿ぀-ヿ一-鿿가-힯]")


def tokens(text):
    # %2$lld and %lld are the same placeholder: a translation may reorder them.
    return sorted(re.sub(r"%\d+\$", "%", t) for t in TOKEN.findall(text))


def script_ok(code, key, value):
    letters = [c for c in value if c.isalpha()]
    if not letters or value == key:
        return True
    want = SCRIPTS.get(code)
    if want is None:  # a Latin-script language: no other writing system that the English doesn't have
        return not OTHER.search(value) or bool(OTHER.search(key))
    names = [unicodedata.name(c, "") for c in letters]
    native = sum(1 for n in names if any(n.startswith(w) or (" " + w) in n for w in want.split("|")))
    return native > 0


def problem(code, key, value):
    if not isinstance(value, str) or not value.strip():
        return "empty"
    if tokens(key) != tokens(value):
        return "placeholders differ"
    if not script_ok(code, key, value):
        return "wrong writing system"
    words = value.split()
    if any(len(w) > 1 and words[i:i + 4] == [w] * 4 for i, w in enumerate(words)):
        return "repeated word"
    if len(value) > max(60, 5 * len(key)):
        return "far longer than the English"
    return None


def files():
    for p in sorted((ROOT / "windows/Core/Locales").glob("*.json")):
        yield p.stem, p, "json"
    for p in sorted((ROOT / "android/app/src/main/assets/locales").glob("*.json")):
        yield p.stem, p, "json"
    for p in sorted((ROOT / "extension/locales").glob("*.json")):
        yield p.stem, p, "json"
    apple = {l["apple"]: c for c, l in LANGS.items()}
    for p in sorted((ROOT / "packaging/Localizations").glob("*.lproj/Localizable.strings")):
        code = apple.get(p.parent.stem)
        if code:
            yield code, p, "strings"


STRING = re.compile(r'^("(?:[^"\\]|\\.)*")\s*=\s*("(?:[^"\\]|\\.)*");\s*$')


def check(fix):
    errors, untranslated, total = [], 0, 0
    for code, path, kind in files():
        if code == "en" or code not in LANGS:
            continue
        if kind == "json":
            doc = json.loads(path.read_text(encoding="utf-8"))
            table = doc.get("strings", doc)
            changed = False
            for k, v in list(table.items()):
                if not isinstance(v, str):
                    continue
                total += 1
                untranslated += v == k
                why = problem(code, k, v)
                if why:
                    errors.append(f"{path.relative_to(ROOT)}: {k!r} → {v!r}: {why}")
                    if fix:
                        table[k] = k; changed = True
            if changed:
                path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        else:
            lines, changed = path.read_text(encoding="utf-8").split("\n"), False
            for i, line in enumerate(lines):
                m = STRING.match(line)
                if not m:
                    continue
                k, v = json.loads(m.group(1)), json.loads(m.group(2))
                total += 1
                untranslated += v == k
                why = problem(code, k, v)
                if why:
                    errors.append(f"{path.relative_to(ROOT)}: {k!r} → {v!r}: {why}")
                    if fix:
                        lines[i] = f"{m.group(1)} = {m.group(1)};"; changed = True
            if changed:
                path.write_text("\n".join(lines), encoding="utf-8")
    # Android's share label and the browser stores' texts: keyed by name, checked against the English file.
    import xml.etree.ElementTree as ET
    res = ROOT / "android/app/src/main/res"
    english = {e.get("name"): e.text or "" for e in ET.parse(res / "values/strings.xml").getroot()}
    folder = {"in": "id", "zh-rCN": "zh-cn", "zh-rTW": "zh-tw"}
    for d in sorted(res.glob("values-*")):
        f = d / "strings.xml"
        if not f.exists():
            continue
        code = folder.get(d.name[7:], d.name[7:])
        for e in ET.parse(f).getroot():
            total += 1
            why = problem(code, english.get(e.get("name"), ""), e.text or "")
            if why:
                errors.append(f"{f.relative_to(ROOT)}: {e.get('name')}: {why}")
    stores = ROOT / "extension/_locales"
    en = json.loads((stores / "en/messages.json").read_text(encoding="utf-8"))
    chrome = {l["chrome"]: c for c, l in LANGS.items() if l["chrome"]}
    for f in sorted(stores.glob("*/messages.json")):
        code = chrome.get(f.parent.name)
        if not code or code == "en":
            continue
        for k, v in json.loads(f.read_text(encoding="utf-8")).items():
            total += 1
            why = problem(code, en.get(k, {}).get("message", ""), v.get("message", ""))
            if why:
                errors.append(f"{f.relative_to(ROOT)}: {k}: {why}")
    return errors, untranslated, total


if __name__ == "__main__":
    fix = "--fix" in sys.argv
    errors, untranslated, total = check(fix)
    for e in errors:
        print(e)
    print(f"{total} translated texts checked, {untranslated} still in English, {len(errors)} {'replaced with English' if fix else 'errors'}.")
    sys.exit(1 if errors and not fix else 0)

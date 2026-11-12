// Rules of the Chrome Web Store, Microsoft Edge Add-ons and addons.mozilla.org that can be checked before submitting.
import assert from "node:assert/strict";
import test from "node:test";
import { existsSync, readFileSync, readdirSync } from "node:fs";

const dir = new URL(".", import.meta.url);
const read = name => readFileSync(new URL(name, dir), "utf8");
const manifest = JSON.parse(read("manifest.json"));
const code = ["background.js", "helpers.js", "content.js", "popup.js"].map(read).join("\n");

test("manifest version 3 with a store-ready version number", () => {
  assert.equal(manifest.manifest_version, 3);
  assert.match(manifest.version, /^\d+(\.\d+){0,3}$/);
});

test("name and description fit every store in every language", () => {
  for (const lang of readdirSync(new URL("_locales/", dir))) {
    const m = JSON.parse(read(`_locales/${lang}/messages.json`));
    assert.ok(m.extName.message.length <= 45, `${lang}: name longer than 45 characters`);
    assert.ok(m.extDescription.message.length <= 132, `${lang}: description longer than 132 characters`);
  }
});

test("icons the stores need are present", () => {
  for (const size of ["16", "48", "128"]) assert.ok(existsSync(new URL(manifest.icons[size], dir)), `icon ${size}`);
});

test("every permission asked for is used (stores reject unused permissions)", () => {
  const uses = {
    downloads: /chrome\.downloads\./, nativeMessaging: /sendNativeMessage|connectNative/, contextMenus: /chrome\.contextMenus\./,
    storage: /chrome\.storage\./, cookies: /chrome\.cookies\./, scripting: /chrome\.scripting\./, webRequest: /chrome\.webRequest\./,
    tabs: /chrome\.tabs\./,
  };
  for (const p of manifest.permissions) {
    assert.ok(uses[p], `${p}: add how it is used to this test and to docs/STORES.md`);
    assert.match(code, uses[p], `${p} is asked for but not used`);
  }
});

test("no remotely hosted or generated code", () => {
  assert.doesNotMatch(code, /\beval\s*\(|new\s+Function\s*\(|importScripts\s*\(\s*["'`]https?:/);
  assert.doesNotMatch(code, /setTimeout\s*\(\s*["'`]|setInterval\s*\(\s*["'`]/);
  assert.doesNotMatch(read("popup.html"), /<script[^>]+src\s*=\s*["']https?:/i);
  assert.doesNotMatch(read("popup.html"), /<script(?![^>]*\bsrc=)[^>]*>\s*\S/i, "inline scripts are not allowed in extension pages");
});

// Property-based (fuzz) tests: the helpers read addresses, headers and playlists from any web page, so they must never
// throw and must keep their promises for every input, not only the examples in helpers.test.js.
import { test } from "node:test";
import assert from "node:assert/strict";
import fc from "fast-check";
import {
  extensionOf, parseTypes, shouldTakeOver, siteOf, siteListed, isWebLink, isMedia, nameFromUrl, filterLinks,
  mayBeMedia, originOf, isStreamPlaylist, summarizePlaylist, titleToName, isVideoSite, formatSize,
} from "./helpers.js";

const RUNS = { numRuns: 2000 };
// Addresses as pages really produce them, plus arbitrary text and odd schemes.
const scheme = fc.constantFrom("http", "https", "HTTP", "ftp", "file", "data", "blob", "javascript", "chrome-extension");
const url = fc.oneof(
  fc.webUrl({ withQueryParameters: true, withFragments: true }),
  fc.tuple(scheme, fc.string()).map(([s, rest]) => `${s}:${rest}`),
  fc.tuple(fc.webUrl(), fc.string({ unit: "binary" })).map(([u, x]) => u + "/" + encodeURIComponent(x)),
  fc.tuple(fc.webUrl(), fc.constantFrom("%2F", "%5C", "%2E%2E", "%00", "%", "%E0%A4%A", "..")).map(([u, x]) => `${u}/${x}`),
  fc.string({ unit: "binary" }),
);
const text = fc.oneof(fc.string(), fc.string({ unit: "binary" }));
const contentType = fc.oneof(text, fc.constantFrom("video/mp4", "audio/mpeg", "application/vnd.apple.mpegurl",
  "application/dash+xml", "video/mp2t", "text/html", ""));

test("no helper throws, whatever the page sends", () => {
  fc.assert(fc.property(url, text, contentType, fc.integer(), (u, t, ct, n) => {
    extensionOf(u); parseTypes(t); siteOf(t); siteOf(u); siteListed(u, t); isWebLink(u); isMedia(u, ct, n);
    nameFromUrl(u); mayBeMedia(u, ct); originOf(u); isStreamPlaylist(u, ct); summarizePlaylist(t, u);
    titleToName(t); isVideoSite(u); formatSize(n); filterLinks([u, t], t);
  }), RUNS);
});

test("only plain web links are handed to the app", () => {
  fc.assert(fc.property(url, (u) => {
    if (isWebLink(u)) assert.match(new URL(u).protocol, /^https?:$/);
    const o = originOf(u);
    assert.ok(o === "" || /^https?:\/\/[^/]+$/.test(o), o);
    if (!/^https?:\/\//i.test(u)) {
      assert.equal(shouldTakeOver({ url: u, totalBytes: 50 * 1024 * 1024 }, {}), false);
      assert.equal(mayBeMedia(u, "video/mp4"), false);
      assert.equal(isMedia(u, "video/mp4", -1), false);
    }
  }), RUNS);
});

test("a site always matches its own links", () => {
  fc.assert(fc.property(fc.webUrl(), (u) => {
    const host = new URL(u).hostname;
    if (host && !host.includes(":") && !/^\.|\.$/.test(host)) assert.equal(siteListed(u, siteOf(u)), true);
  }), RUNS);
});

test("suggested file names are safe file names", () => {
  fc.assert(fc.property(url, text, (u, t) => {
    for (const name of [nameFromUrl(u), titleToName(t)]) {
      assert.ok(name.length > 0 && name.length <= 255, JSON.stringify(name));
      assert.doesNotMatch(name, /[\\/\0]/, JSON.stringify(name));
      assert.ok(name !== "." && name !== "..", JSON.stringify(name));
    }
    assert.doesNotMatch(titleToName(t), /[\\/:*?"<>|]/);
    assert.ok(titleToName(t).length <= 120);
  }), RUNS);
});

const playlistLine = fc.oneof(
  fc.constantFrom("#EXTM3U", "#EXT-X-ENDLIST", "#EXT-X-KEY:METHOD=AES-128,URI=\"k\"", "#EXT-X-KEY:METHOD=NONE"),
  fc.tuple(fc.nat(), fc.nat(), fc.nat()).map(([w, h, b]) => `#EXT-X-STREAM-INF:BANDWIDTH=${b},RESOLUTION=${w}x${h}`),
  url, text,
);

test("playlists are read safely", () => {
  fc.assert(fc.property(fc.array(playlistLine, { maxLength: 40 }), fc.webUrl(), (lines, base) => {
    const out = summarizePlaylist(lines.join("\n"), base);
    assert.ok(out.height >= 0 && out.bandwidth >= 0);
    for (const v of out.variants) assert.doesNotThrow(() => new URL(v));
    if (!out.valid) assert.equal(out.variants.length, 0);
  }), RUNS);
});

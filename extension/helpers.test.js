import assert from "node:assert/strict";
import test from "node:test";
import { cookieHeader, extensionOf, isWebLink, shouldTakeOver } from "./helpers.js";

test("only web links go to the app", () => {
  assert.equal(isWebLink("https://example.com/a.zip"), true);
  assert.equal(isWebLink("HTTP://example.com/a"), true);
  for (const bad of ["file:///etc/passwd", "javascript:alert(1)", "data:text/html,x", "chrome://settings", "ftp://e.com/a", "https://", "not a url"])
    assert.equal(isWebLink(bad), false, bad);
});

test("takes over normal web downloads only", () => {
  const big = { url: "https://example.com/a.zip", filename: "/Users/x/Downloads/a.zip", totalBytes: 50e6 };
  assert.equal(shouldTakeOver(big, {}), true);
  assert.equal(shouldTakeOver({ ...big, url: "blob:https://example.com/1" }, {}), false);
  assert.equal(shouldTakeOver({ ...big, totalBytes: 1000 }, {}), false);
  assert.equal(shouldTakeOver({ ...big, totalBytes: -1 }, {}), true);
  assert.equal(shouldTakeOver(big, { enabled: false }), false);
  assert.equal(shouldTakeOver(big, { skipTypes: ".ZIP, pdf" }), false);
});

test("file extensions", () => {
  assert.equal(extensionOf("https://e.com/x/Movie.MP4?token=1"), "mp4");
  assert.equal(extensionOf("/Users/x/Downloads/archive.tar.gz"), "gz");
  assert.equal(extensionOf("README"), "");
});

test("cookie header", () => {
  assert.equal(cookieHeader([{ name: "a", value: "1" }, { name: "b", value: "2" }]), "a=1; b=2");
  assert.equal(cookieHeader([]), "");
});

import { addMedia, filterLinks, formatSize, isMedia, nameFromUrl } from "./helpers.js";

test("media detection", () => {
  assert.equal(isMedia("https://e.com/v/clip.mp4", "", -1), true);
  assert.equal(isMedia("https://e.com/stream", "video/webm", 5e6), true);
  assert.equal(isMedia("https://e.com/live/index.m3u8", "application/vnd.apple.mpegurl", -1), false);
  assert.equal(isMedia("https://e.com/seg/001.ts", "video/mp2t", 1e6), false);
  assert.equal(isMedia("https://e.com/tiny.mp3", "audio/mpeg", 1000), false);
  assert.equal(isMedia("blob:https://e.com/1", "video/mp4", -1), false);
});

test("link filtering", () => {
  const links = ["https://e.com/a.zip", "https://e.com/a.zip#x", "https://e.com/page.html", "https://e.com/b.PDF", "mailto:x@e.com", "https://e.com/dir/"];
  assert.deepEqual(filterLinks(links, ""), ["https://e.com/a.zip", "https://e.com/b.PDF"]);
  assert.deepEqual(filterLinks(links, "pdf"), ["https://e.com/b.PDF"]);
});

test("small helpers", () => {
  assert.equal(nameFromUrl("https://e.com/x/My%20Song.mp3?a=1"), "My Song.mp3");
  assert.equal(formatSize(1536), "1.5 KB");
  assert.equal(formatSize(-1), "");
  const list = addMedia(addMedia([], { url: "a" }), { url: "b" });
  assert.deepEqual(addMedia(list, { url: "a" }).map((m) => m.url), ["a", "b"]);
});

import { isStreamPlaylist, qualityLabel, sortMedia, summarizePlaylist, titleToName } from "./helpers.js";

test("stream playlists", () => {
  assert.equal(isStreamPlaylist("https://e.com/v/master.m3u8?t=1", ""), true);
  assert.equal(isStreamPlaylist("https://e.com/v/play", "application/vnd.apple.mpegurl"), true);
  assert.equal(isStreamPlaylist("https://e.com/v/a.mp4", "video/mp4"), false);
  const master = summarizePlaylist(
    "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360\nlow.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080\nhd.m3u8\n",
    "https://e.com/v/master.m3u8",
  );
  assert.equal(master.master, true);
  assert.equal(master.height, 1080);
  assert.deepEqual(master.variants, ["https://e.com/v/low.m3u8", "https://e.com/v/hd.m3u8"]);
  const locked = summarizePlaylist('#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI="skd://k"\n#EXTINF:6,\na.ts\n', "https://e.com/");
  assert.equal(locked.protected, true);
  assert.equal(locked.live, true);
  assert.equal(summarizePlaylist("<html>", "https://e.com/").valid, false);
});

test("names, order and labels", () => {
  assert.equal(titleToName("My Talk: Part 1 | Conference"), "My Talk Part 1");
  assert.equal(titleToName(""), "video");
  const sorted = sortMedia([{ url: "a", size: 10 }, { url: "b", kind: "stream", height: 720 }, { url: "c", kind: "stream", height: 1080 }]);
  assert.deepEqual(sorted.map((m) => m.url), ["c", "b", "a"]);
  assert.equal(qualityLabel({ kind: "stream", height: 1080 }), "1080p · stream");
});

import { isVideoSite } from "./helpers.js";

test("video sites", () => {
  assert.equal(isVideoSite("https://www.youtube.com/watch?v=1"), true);
  assert.equal(isVideoSite("https://m.facebook.com/reel/1"), true);
  assert.equal(isVideoSite("https://notyoutube.com/"), false);
  assert.equal(isVideoSite("not a link"), false);
});

test("quality menu lists what the video offers, best first", async () => {
  const { qualityMenu } = await import("./helpers.js");
  const menu = qualityMenu({ ok: true, soundSize: 4 * 1048576, qualities: [
    { height: 2160, size: 900 * 1048576 }, { height: 1080, size: 0 }, { height: 1080, size: 1 }, { height: 720, size: 50 * 1048576 }, { height: 12 },
  ] });
  assert.deepEqual(menu.map((m) => m.label), ["2160p (4K) · best · about 900 MB", "1080p", "720p · about 50 MB", "Sound only · about 4.0 MB"]);
  assert.deepEqual(menu.map((m) => [m.maxHeight, m.audioOnly]), [[0, false], [1080, false], [720, false], [0, true]]);
  // an older app or a failed lookup: the two choices from before
  for (const r of [null, { ok: false }, { ok: true, qualities: "x" }])
    assert.deepEqual(qualityMenu(r).map((m) => m.label), ["Video, best quality", "Sound only"]);
  // an app older than 0.6.3: a note first, so it's clear why there are no qualities
  const old = qualityMenu({ ok: false, error: "Unknown message" });
  assert.equal(old[0].note, true);
  assert.deepEqual(old.slice(1).map((m) => m.label), ["Video, best quality", "Sound only"]);
});

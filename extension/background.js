import {
  DEFAULTS, HOST, TROUBLESHOOT, addMedia, cookieHeader, downloadKey, header, isMedia, isStreamPlaylist, isVideoSite, isWebLink, mayBeMedia, nameFromUrl,
  originOf, shouldTakeOver, sortMedia, summarizePlaylist, titleToName, loadStrings, translator,
} from "./helpers.js";

// ---- Interface language: the popup and the page buttons read the strings kept here ----

let say = translator({});
const MENUS = [
  ["nexonic-download", "Download with Nexonic"],
  ["nexonic-page-video", "Download this page's video with Nexonic"],
  ["nexonic-all", "Download all links with Nexonic"],
];
async function useLanguage() {
  const { language = "" } = await chrome.storage.sync.get("language");
  const { code, strings } = await loadStrings(language);
  say = translator(strings);
  await chrome.storage.local.set({ uiLanguage: code, uiStrings: strings });
  for (const [id, title] of MENUS) chrome.contextMenus.update(id, { title: say(title) }, () => void chrome.runtime.lastError);
}
useLanguage();
chrome.storage.onChanged.addListener((c, area) => {
  if (area === "sync" && c.language) useLanguage();
});

async function settings() {
  return { ...DEFAULTS, ...(await chrome.storage.sync.get(Object.keys(DEFAULTS))) };
}

async function cookiesFor(url) {
  try {
    return cookieHeader(await chrome.cookies.getAll({ url }));
  } catch (_) {
    return ""; // no cookie access for this address
  }
}

async function send(url, extra = {}) {
  if (!isWebLink(url)) return false;  // nothing else is ever sent to the app (or gets cookies looked up)
  const cookies = await cookiesFor(url);
  const message = { type: "download", url, cookies, userAgent: navigator.userAgent, ...extra };
  try {
    // A helper that doesn't answer within a few seconds counts as unreachable, so the browser keeps the download.
    const reply = await Promise.race([
      chrome.runtime.sendNativeMessage(HOST, message),
      new Promise((resolve) => setTimeout(() => resolve(null), 8000)),
    ]);
    const ok = !!(reply && reply.ok);
    await showStatus(ok);
    return ok;
  } catch (_) {
    await showStatus(false);
    return false;
  }
}

/** The toolbar button shows "!" and says what to do while the app can't be reached. */
async function showStatus(ok) {
  await chrome.action.setBadgeText({ text: ok ? "" : "!" });
  if (!ok) await chrome.action.setBadgeBackgroundColor({ color: "#cf222e" }).catch(() => {});
  await chrome.action.setTitle({ title: ok ? "Nexonic Download Manager" : say(TROUBLESHOOT) }).catch(() => {});
}

// ---- Browser downloads go to the app ----

const handedOver = new Map(); // link -> time, so one click is never sent to the app twice

chrome.downloads.onCreated.addListener(async (item) => {
  if (await bypassed(item)) return;
  if (!shouldTakeOver(item, await settings())) return;
  try {
    await chrome.downloads.pause(item.id);
  } catch (_) {
    return; // already finished or not pausable
  }
  const url = item.finalUrl || item.url;
  const key = downloadKey(url);
  const now = Date.now();
  for (const [u, t] of handedOver) if (now - t > 10000) handedOver.delete(u);
  if (handedOver.has(key)) {
    // The same link a moment ago (a redirect or a double click): the app already has it.
    await chrome.downloads.cancel(item.id).catch(() => {});
    await chrome.downloads.erase({ id: item.id }).catch(() => {});
    return;
  }
  handedOver.set(key, now);
  const name = item.filename ? item.filename.split(/[\\/]/).pop() : undefined;
  const size = item.totalBytes > 0 ? item.totalBytes : item.fileSize > 0 ? item.fileSize : undefined;
  const ok = await send(url, { referrer: item.referrer || "", fileName: name, size, browser: true });
  if (!ok) handedOver.delete(key);
  if (ok) {
    await chrome.downloads.cancel(item.id).catch(() => {});
    await chrome.downloads.erase({ id: item.id }).catch(() => {});
  } else {
    await chrome.downloads.resume(item.id).catch(() => {}); // the app is not reachable: let the browser finish
  }
});

// ---- Right-click menu ----

chrome.runtime.onInstalled.addListener(async () => {
  await useLanguage();
  const contexts = { "nexonic-download": ["link", "video", "audio"], "nexonic-page-video": ["page", "video"], "nexonic-all": ["page", "selection"] };
  for (const [id, title] of MENUS) chrome.contextMenus.create({ id, title: say(title), contexts: contexts[id] }, () => void chrome.runtime.lastError);
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  const referrer = info.pageUrl || (tab && tab.url) || "";
  if (info.menuItemId === "nexonic-all" && tab) {
    const links = await pageLinks(tab.id, !!info.selectionText);
    for (const url of links) await send(url, { referrer });
    return;
  }
  if (info.menuItemId === "nexonic-page-video") {
    send(info.pageUrl || (tab && tab.url), { site: true, browser: true });
    return;
  }
  const url = info.linkUrl || info.srcUrl;
  if (url && /^https?:\/\//i.test(url)) send(url, { referrer, browser: true });
});

/** Links on a page (only inside the selection when there is one). */
async function pageLinks(tabId, selectionOnly) {
  const [result] = await chrome.scripting.executeScript({
    target: { tabId },
    args: [selectionOnly],
    func: (onlySelected) => {
      const anchors = [...document.querySelectorAll("a[href]")];
      const sel = window.getSelection();
      const keep = onlySelected && sel && sel.rangeCount ? anchors.filter((a) => sel.containsNode(a, true)) : anchors;
      return keep.map((a) => a.href);
    },
  });
  return (result && result.result) || [];
}

// ---- Audio and video seen on each tab ----

async function mediaOf(tabId) {
  const key = `media-${tabId}`;
  return (await chrome.storage.session.get(key))[key] || [];
}

async function setMedia(tabId, list) {
  await chrome.storage.session.set({ [`media-${tabId}`]: list });
}

// List changes run one after another, so two answers arriving together don't overwrite each other.
let queue = Promise.resolve();
function serial(fn) {
  queue = queue.then(fn, fn);
  return queue;
}

const checked = new Map(); // tab -> playlists already read on the current page

async function tabTitle(tabId) {
  try {
    return (await chrome.tabs.get(tabId)).title || "";
  } catch (_) {
    return "";
  }
}

/** Reads a streamed-video playlist once and lists it (best quality first), hiding the single-quality parts of it. */
async function addStream(tabId, url) {
  if (!checked.has(tabId)) checked.set(tabId, new Set());
  const seen = checked.get(tabId);
  if (seen.has(url)) return;
  seen.add(url);
  let text = "";
  try {
    text = await (await fetch(url, { credentials: "include" })).text();
  } catch (_) {
    return;
  }
  const s = summarizePlaylist(text, url);
  if (!s.valid) return;
  const name = titleToName(await tabTitle(tabId));
  await serial(async () => {
    let list = await mediaOf(tabId);
    const hidden = new Set(list.flatMap((m) => m.variants || []));
    if (hidden.has(url)) return; // one quality of a stream already listed
    list = list.filter((m) => !s.variants.includes(m.url));
    const item = {
      url, kind: "stream", name: `${name}.mp4`, size: -1, height: s.height, variants: s.variants, protected: s.protected, live: s.live,
    };
    await setMedia(tabId, sortMedia(addMedia(list, item)));
  });
}

/**
 * The tab a request belongs to. Players that load the video from a worker (common on streaming sites) make requests
 * that belong to no tab (tabId -1): those go to the tab showing the page that started them, or else the tab in front.
 */
async function tabOf(d) {
  if (d.tabId >= 0) return d.tabId;
  const origin = originOf(d.initiator || d.documentUrl || d.originUrl || "");
  if (!origin) return -1;
  try {
    const tabs = await chrome.tabs.query({});
    const same = tabs.filter((t) => originOf(t.url || "") === origin);
    const pick = same.find((t) => t.active) || same[0] || (await chrome.tabs.query({ active: true, lastFocusedWindow: true }))[0];
    return pick && pick.id !== undefined ? pick.id : -1;
  } catch (_) {
    return -1;
  }
}

async function onMediaResponse(d) {
    if (d.statusCode >= 400) return;
    const type = header(d.responseHeaders, "content-type");
    if ((d.type === "xmlhttprequest" || d.type === "other") && !mayBeMedia(d.url, type)) return; // most requests: cheap no
    // On video sites the app's video engine saves the whole video, so the small pieces the player loads are skipped.
    if (isVideoSite(d.initiator || d.documentUrl || d.originUrl || "")) return;
    const tabId = await tabOf(d);
    if (tabId < 0) return;
    d = { ...d, tabId };
    if (isStreamPlaylist(d.url, type)) return addStream(d.tabId, d.url);
    const range = header(d.responseHeaders, "content-range");
    const total = range.includes("/") ? Number(range.split("/").pop()) : Number(header(d.responseHeaders, "content-length") || -1);
    if (!isMedia(d.url, type, Number.isFinite(total) ? total : -1)) return;
    const item = { url: d.url, name: nameFromUrl(d.url), size: total > 0 ? total : -1, type: type.split(";")[0] };
    await serial(async () => setMedia(d.tabId, sortMedia(addMedia(await mediaOf(d.tabId), item))));
}

// Players' own requests (media, object) and script requests (XHR, fetch, workers). Script requests are checked by
// address and content type first, a cheap test, so streams served from addresses without a file extension are found
// too; everything else returns at once.
chrome.webRequest.onResponseStarted.addListener(onMediaResponse, { urls: ["<all_urls>"], types: ["media", "object"] }, ["responseHeaders"]);
chrome.webRequest.onResponseStarted.addListener(onMediaResponse, { urls: ["<all_urls>"], types: ["xmlhttprequest", "other"] }, ["responseHeaders"]);

chrome.tabs.onUpdated.addListener((tabId, change) => {
  if (change.status === "loading" && change.url) {
    checked.delete(tabId);
    serial(() => setMedia(tabId, []));
  }
});
chrome.tabs.onRemoved.addListener((tabId) => {
  checked.delete(tabId);
  chrome.storage.session.remove(`media-${tabId}`);
});

// ---- Messages from the popup and the page button ----

// Alt+click on a link: that one link downloads in the browser, not in Nexonic. Kept per link for a few seconds, in session
// storage so a restarted background script still knows (the browser may start it again for the download).
const BYPASS_MS = 10000;
const bypassMemory = new Map();
async function bypassList() {
  try { return (await chrome.storage.session.get("bypass")).bypass || {}; } catch (_) { return Object.fromEntries(bypassMemory); }
}
async function saveBypass(list) {
  try { await chrome.storage.session.set({ bypass: list }); } catch (_) { bypassMemory.clear(); for (const [k, v] of Object.entries(list)) bypassMemory.set(k, v); }
}
async function markBypass(url) {
  const now = Date.now(), list = await bypassList();
  for (const [k, until] of Object.entries(list)) if (until < now) delete list[k];
  list[url] = now + BYPASS_MS;
  await saveBypass(list);
}
async function bypassed(item) {
  const list = await bypassList(), now = Date.now();
  const hit = [item.url, item.finalUrl].find((u) => u && list[u] >= now);
  if (!hit) return false;
  delete list[hit];
  await saveBypass(list);
  return true;
}

chrome.runtime.onMessage.addListener((msg, sender, respond) => {
  // A page's own script only ever speaks for its own tab; only the popup (no sender.tab) may name a tab.
  const tabId = sender.tab ? sender.tab.id : msg.tabId;
  const referrer = msg.referrer || (sender.tab && sender.tab.url) || "";
  const run = async () => {
    switch (msg && msg.type) {
      case "bypassInterception": {
        if (typeof msg.url !== "string" || !/^https?:/i.test(msg.url)) return { ok: false };
        await markBypass(msg.url);
        return { ok: true };
      }
      case "send-link": {
        // One link the user picked (video button, popup, page menu): the app shows its download window.
        const extra = { referrer, later: !!msg.later, browser: true };
        if (msg.stream) Object.assign(extra, { stream: true, fileName: msg.fileName });
        else if (msg.site) {
          Object.assign(extra, { site: true, audioOnly: !!msg.audioOnly });
          if (Number.isInteger(msg.maxHeight) && msg.maxHeight >= 100 && !msg.audioOnly) extra.maxHeight = msg.maxHeight;
        }
        else if (msg.fileName) extra.fileName = msg.fileName;
        return { ok: await send(msg.url, extra) };
      }
      case "profile-all": {
        // The popup's "Download all" on an Instagram or Facebook profile the user has open and is signed in to.
        const profile = profileOf(msg.url || "");
        if (!profile || !Number.isInteger(tabId)) return { ok: false };
        return { ok: true, ...(await profileStart(profile, tabId, msg.url, !!msg.later)) };
      }
      case "profile-stop": {
        // The popup's Stop: the reader ends after the current screen and what it found is still sent.
        if (!profileRun) return { ok: false };
        await chrome.scripting.executeScript({ target: { tabId: profileRun.tabId }, func: () => { window.__nexonicProfileStop = true; } }).catch(() => {});
        return { ok: true };
      }
      case "profile-found": {
        // The collector in the profile's tab: progress, or the finished list (only from the tab of the running job).
        if (!sender.tab || !profileRun || profileRun.tabId !== sender.tab.id) return { ok: false };
        if (msg.done) await profileFinish(msg);
        else chrome.runtime.sendMessage({ type: "profile-progress", count: Number(msg.count) || 0 }).catch(() => {});
        return { ok: true };
      }
      case "send-links": {
        let sent = 0;
        for (const url of msg.urls || []) if (await send(url, { referrer, later: !!msg.later })) sent++;
        return { ok: sent > 0, sent };
      }
      case "media-list":
        return { media: await mediaOf(tabId) };
      case "page-links":
        return { links: await pageLinks(tabId, false) };
      case "video-button": {
        // The page's own address when it is a plain file, otherwise what the tab has loaded.
        const seen = (await mediaOf(tabId)).filter((m) => !m.protected);
        if (/^https?:\/\//i.test(msg.src || "") && !seen.some((m) => m.url === msg.src)) {
          seen.push({ url: msg.src, name: nameFromUrl(msg.src), size: -1 });
        }
        return { media: sortMedia(seen), locked: (await mediaOf(tabId)).some((m) => m.protected) };
      }
      case "ping": {
        const r = await chrome.runtime.sendNativeMessage(HOST, { type: "ping" }).catch(() => null);
        await showStatus(!!(r && r.ok));
        return { ok: !!(r && r.ok), version: r && r.version };
      }
      default:
        return { ok: false };
    }
  };
  run().then(respond, () => respond({ ok: false }));
  return true;
});

// ---- Every video and photo of an Instagram or Facebook profile the user has open, with their own sign-in ----
// The popup's "Download all" scrolls the profile tab the user has open, slowly, and collects the posts the page itself
// shows (the sites load more posts only as the page scrolls). Rules, so this never looks or acts like a bot:
//  - read only: it scrolls and reads what the page shows; never a like, follow, comment, message or post;
//  - one profile at a time, one screen every 2-4 seconds, like a person scrolling; Stop in the popup ends it;
//  - at most 150 screens an hour across all profiles (about 1,800 posts);
//  - when the site leaves the profile (a sign-in page, a check of the account, "try again later"), stop at once and
//    leave that site alone for an hour.
// Nexonic never signs in and never sees a password: only what this browser is already signed in to.
const PAGES_PER_HOUR = 150;
let profileRun = null;  // { tabId, site, owner, referrer, later, started }

async function pace() {
  const { profilePace = {} } = await chrome.storage.local.get("profilePace").catch(() => ({}));
  return { pages: (profilePace.pages || []).filter((t) => Date.now() - t < 3600_000), restUntil: profilePace.restUntil || {} };
}
const savePace = (p) => chrome.storage.local.set({ profilePace: p }).catch(() => {});

/** The site and profile of a link: { site, owner, pages }. */
function profileOf(url) {
  let u;
  try { u = new URL(url); } catch (_) { return null; }
  const path = u.pathname.split("/").filter(Boolean);
  if (/(^|\.)instagram\.com$/.test(u.hostname) && path[0] && /^[A-Za-z0-9._]{1,30}$/.test(path[0])
      && !/^(p|reel|reels|tv|stories|explore|accounts|direct)$/.test(path[0])) {
    return { site: "instagram", owner: path[0], pages: [`https://www.instagram.com/${path[0]}/`] };
  }
  if (/(^|\.)facebook\.com$/.test(u.hostname)) {
    const id = u.pathname === "/profile.php" ? u.searchParams.get("id") : path[0] === "people" ? path[2] : null;
    if (id && /^\d+$/.test(id)) return { site: "facebook", owner: id, pages: [`https://www.facebook.com/profile.php?id=${id}&sk=reels_tab`, `https://www.facebook.com/profile.php?id=${id}&sk=videos`] };
    if (path[0] && !/^(watch|reel|videos|groups|events|share|photo|login|marketplace|gaming|pages|search)$/.test(path[0]))
      return { site: "facebook", owner: path[0], pages: [`https://www.facebook.com/${path[0]}/reels/`, `https://www.facebook.com/${path[0]}/videos/`] };
  }
  return null;
}

/** Starts the reader in the tab; { stopped } when it can't start now ("busy", "rest" or "hourly"). */
async function profileStart(profile, tabId, referrer, later) {
  if (profileRun && !(await chrome.tabs.get(profileRun.tabId).catch(() => null))) profileRun = null;  // its tab was closed
  if (profileRun && Date.now() - profileRun.started < 3600_000) return { stopped: "busy" };
  const p = await pace();
  if ((p.restUntil[profile.site] || 0) > Date.now()) return { stopped: "rest" };
  const allowed = PAGES_PER_HOUR - p.pages.length;
  if (allowed <= 0) return { stopped: "hourly" };
  profileRun = { tabId, site: profile.site, owner: profile.owner, referrer, later, started: Date.now() };
  try {
    await chrome.scripting.executeScript({ target: { tabId }, func: profileReader, args: [profile, allowed] });
  } catch (_) {
    profileRun = null;
    return { stopped: "signed-out" };
  }
  return {};
}

/** The reader's result: the pace is recorded, videos go to the app's video engine and photos are downloaded as files. */
async function profileFinish(msg) {
  const run = profileRun;
  profileRun = null;
  const p = await pace();
  for (let i = 0; i < Math.min(Number(msg.pages) || 0, PAGES_PER_HOUR); i++) p.pages.push(Date.now());
  if (msg.stopped === "rest") p.restUntil[run.site] = Date.now() + 3600_000;
  await savePace(p);
  const media = /^https:\/\/([a-z0-9-]+\.)*(instagram\.com|facebook\.com|cdninstagram\.com|fbcdn\.net)\//;
  let sent = 0;
  for (const v of (msg.videos || []).slice(0, 5000))
    if (media.test(v.u || "") && await send(v.u, { referrer: run.referrer, site: true, browser: true, later: run.later })) sent++;
  for (const f of (msg.photos || []).slice(0, 5000))
    if (media.test(f.u || "") && await send(f.u, { referrer: run.referrer, fileName: f.name, browser: true, later: run.later })) sent++;
  chrome.runtime.sendMessage({ type: "profile-sent", sent, stopped: msg.stopped || "" }).catch(() => {});
}

/**
 * Runs in the profile's tab (the extension's isolated world): scrolls one screen at a time and collects the posts the
 * page shows. Posts back with chrome.runtime.sendMessage({ type: "profile-found", … }). Self-contained.
 */
async function profileReader(profile, allowed) {
  if (window.__nexonicProfileRunning) return;
  window.__nexonicProfileRunning = true;
  window.__nexonicProfileStop = false;
  const pause = (ms) => new Promise((r) => setTimeout(r, ms));
  const tell = (m) => chrome.runtime.sendMessage({ type: "profile-found", ...m }).catch(() => {});
  const clean = (t) => (t || "").replace(/[\\/:*?"<>|\u0000-\u001f]+/g, " ").replace(/\s+/g, " ").trim().slice(0, 80);
  const videos = new Map(), photos = new Map();
  // The biggest picture of a tile's image (srcset lists several sizes).
  const biggest = (img) => {
    const set = (img.getAttribute("srcset") || "").split(",").map((x) => x.trim().split(/\s+/))
      .map(([u, w]) => ({ u, w: parseInt(w, 10) || 0 })).filter((x) => /^https:/.test(x.u));
    set.sort((a, b) => b.w - a.w);
    return (set[0] && set[0].u) || img.currentSrc || img.src;
  };
  const collect = () => {
    for (const a of document.querySelectorAll("a[href]")) {
      const u = new URL(a.href, location.href);
      if (profile.site === "instagram") {
        const m = u.pathname.match(/^\/(?:[^/]+\/)?(p|reel|tv)\/([\w-]+)/);
        if (!m || videos.has(m[2]) || photos.has(m[2])) continue;
        const icon = [...a.querySelectorAll("svg[aria-label]")].map((x) => x.getAttribute("aria-label")).join(" ");
        const img = a.querySelector("img");
        if (m[1] !== "p" || /clip|reel|video/i.test(icon)) videos.set(m[2], { u: `https://www.instagram.com/reel/${m[2]}/` });
        else if (img) photos.set(m[2], { u: biggest(img), name: `${clean(img.alt) || profile.owner} [${m[2]}].jpg` });
      } else {
        const m = u.pathname.match(/\/reel\/(\d{6,})/) || u.pathname.match(/\/videos\/(?:[^/]+\/)?(\d{6,})/)
          || (u.pathname === "/watch/" && (u.searchParams.get("v") || "").match(/^(\d{6,})$/));
        if (m && !videos.has(m[1]))
          videos.set(m[1], { u: /\/reel\//.test(u.pathname) ? `https://www.facebook.com/reel/${m[1]}/` : `https://www.facebook.com/watch/?v=${m[1]}` });
      }
    }
  };
  let pages = 0, stopped = "", still = 0;
  try {
    collect();
    while (!window.__nexonicProfileStop) {
      if (pages >= allowed) { stopped = "hourly"; break; }
      if (/\/(accounts\/login|login|checkpoint|challenge)\b/.test(location.pathname)) { stopped = "rest"; break; }
      const before = videos.size + photos.size, height = document.documentElement.scrollHeight;
      window.scrollBy({ top: Math.round(innerHeight * 0.85), behavior: "smooth" });
      pages++;
      await pause(2000 + Math.random() * 2000);
      collect();
      tell({ count: videos.size + photos.size });
      // The end of the profile: nothing new and the page stopped growing for a few screens.
      still = videos.size + photos.size === before && document.documentElement.scrollHeight === height ? still + 1 : 0;
      if (still >= 4) break;
    }
    if (window.__nexonicProfileStop) stopped = "stopped";
  } catch (_) {
    stopped = stopped || "signed-out";
  }
  window.__nexonicProfileRunning = false;
  tell({ done: true, videos: [...videos.values()], photos: [...photos.values()], pages, stopped });
}

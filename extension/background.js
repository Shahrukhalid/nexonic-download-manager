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
        // The popup's "Download all videos of this profile": collected here, without any tab, then sent to Nexonic.
        const profile = profileOf(msg.url || "");
        if (!profile) return { ok: false };
        profileJob(profile, 5000, (n) => chrome.runtime.sendMessage({ type: "profile-progress", count: n }).catch(() => {})).then(async ({ videos, stopped }) => {
          let sent = 0;
          for (const v of videos) if (await send(v.u, { referrer: msg.url, site: true, browser: true, later: !!msg.later })) sent++;
          chrome.runtime.sendMessage({ type: "profile-sent", sent, stopped }).catch(() => {});
        });
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

// ---- Every video of an Instagram or Facebook profile, with this browser's own sign-in and without any tab ----
// Nexonic's Profile Batch leaves requests with the browser helper; they are fetched about once a minute. Rules, so this
// never looks or acts like a bot:
//  - read only: the profile's post list, the way the site's own profile page asks for it when you scroll; never a like,
//    follow, comment, message or post;
//  - one profile at a time, one page (about 33 posts) every 2-4 seconds, like a person scrolling;
//  - at most 60 pages an hour across all profiles (about 2,000 posts);
//  - when the site answers anything unexpected (slow down, check your account, signed out), stop at once and leave that
//    site alone for an hour.
// Nexonic never signs in and never sees a password: only what this browser is already signed in to.
const PROFILE_POLL = "nexonic-profile-requests";
const PAGES_PER_HOUR = 60;
const pause = (ms) => new Promise((r) => setTimeout(r, ms));
let profileBusy = false;

if (chrome.alarms) {
  chrome.alarms.create(PROFILE_POLL, { periodInMinutes: 1 });
  chrome.alarms.onAlarm.addListener((alarm) => { if (alarm.name === PROFILE_POLL) pollProfiles(); });
}
chrome.runtime.onStartup.addListener(() => pollProfiles());

async function pace() {
  const { profilePace = { pages: [], restUntil: {} } } = await chrome.storage.local.get("profilePace").catch(() => ({}));
  profilePace.pages = (profilePace.pages || []).filter((t) => Date.now() - t < 3600_000);
  profilePace.restUntil = profilePace.restUntil || {};
  return profilePace;
}
const savePace = (p) => chrome.storage.local.set({ profilePace: p }).catch(() => {});

async function pollProfiles() {
  if (profileBusy) return;
  const r = await chrome.runtime.sendNativeMessage(HOST, { type: "profile-requests" }).catch(() => null);
  for (const req of (r && r.requests) || []) {
    if (!/^[0-9a-f]{32}$/.test(req.token || "") || !["instagram", "facebook"].includes(req.site)) continue;
    const report = (stage, data) => chrome.runtime.sendNativeMessage(HOST, { type: "profile-links", token: req.token, stage, data }).catch(() => null);
    await report("started", { posts: 0, videos: [] });
    let last = 0;
    const result = await profileJob({ site: req.site, owner: String(req.owner || ""), pages: req.pages || [] }, Number(req.max) || 500, (n) => {
      if (Date.now() - last > 2000) { last = Date.now(); report("started", { posts: n, videos: [] }); }
    });
    await report("done", { posts: result.videos.length, videos: result.videos, stopped: result.stopped });
  }
}

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

/** { videos, stopped }: stopped is "" (finished), "busy", "rest" (the site asked to slow down), "signed-out" or "hourly". */
async function profileJob(profile, max, progress) {
  if (profileBusy) return { videos: [], stopped: "busy" };
  profileBusy = true;
  const p = await pace();
  try {
    if ((p.restUntil[profile.site] || 0) > Date.now()) return { videos: [], stopped: "rest" };
    // One page of posts, if the pace allows; false (and an hour's rest for the site) when the site doesn't answer normally.
    const page = async (url, init) => {
      if (p.pages.length >= PAGES_PER_HOUR) throw "hourly";
      p.pages.push(Date.now());
      await savePace(p);
      const r = await fetch(url, { credentials: "include", ...init }).catch(() => null);
      if (!r || !r.ok || r.redirected && /login|checkpoint|challenge/.test(r.url)) {
        if (r && (r.status === 429 || r.status >= 500 || /checkpoint|challenge/.test(r.url))) { p.restUntil[profile.site] = Date.now() + 3600_000; await savePace(p); throw "rest"; }
        throw "signed-out";
      }
      return r;
    };
    const videos = [];
    try {
      if (profile.site === "instagram") await instagramVideos(profile.owner, max, progress, page, videos);
      else await facebookVideos(profile.pages, max, progress, page, videos);
      return { videos, stopped: "" };
    } catch (why) {
      return { videos, stopped: typeof why === "string" ? why : "signed-out" };
    }
  } finally {
    profileBusy = false;
  }
}

async function instagramVideos(user, max, progress, page, videos) {
  const headers = { "X-IG-App-ID": "936619743392459", "X-ASBD-ID": "129477", "X-Requested-With": "XMLHttpRequest" };
  const csrf = await chrome.cookies.get({ url: "https://www.instagram.com/", name: "csrftoken" }).catch(() => null);
  if (csrf) headers["X-CSRFToken"] = csrf.value;
  const seen = new Set();
  let next = `https://www.instagram.com/api/v1/feed/user/${encodeURIComponent(user)}/username/?count=33`, userId = null;
  while (next && videos.length < max) {
    const j = await (await page(next, { headers })).json().catch(() => null);
    if (!j || j.status === "fail") throw "signed-out";
    for (const it of j.items || []) {
      userId = userId || (it.user && it.user.pk) || (it.owner && it.owner.pk);
      const video = it.media_type === 2 || (it.carousel_media || []).some((c) => c.media_type === 2);
      if (video && it.code && !seen.has(it.code) && videos.length < max) {
        seen.add(it.code);
        videos.push({ u: `https://www.instagram.com/${it.product_type === "clips" ? "reel" : "p"}/${it.code}/`, t: ((it.caption && it.caption.text) || "").slice(0, 200) });
      }
    }
    progress(videos.length);
    next = j.more_available && j.next_max_id && userId
      ? `https://www.instagram.com/api/v1/feed/user/${userId}/?count=33&max_id=${encodeURIComponent(j.next_max_id)}` : null;
    if (next) await pause(2000 + Math.random() * 2000);
  }
}

async function facebookVideos(pages, max, progress, page, videos) {
  const seen = new Set();
  for (const url of pages) {
    const html = await (await page(url)).text();
    for (const m of html.matchAll(/(?:\\?\/reel\\?\/|\\?\/videos\\?\/(?:[^/"\\]+\\?\/)?|"video_id":")(\d{8,})/g)) {
      const id = m[1];
      if (!seen.has(id) && videos.length < max) {
        seen.add(id);
        videos.push({ u: /reel/.test(m[0]) ? `https://www.facebook.com/reel/${id}/` : `https://www.facebook.com/watch/?v=${id}`, t: "" });
      }
    }
    progress(videos.length);
    await pause(2000 + Math.random() * 2000);
  }
}

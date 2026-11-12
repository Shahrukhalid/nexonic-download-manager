import { DEFAULTS, LANGUAGES, TROUBLESHOOT, filterLinks, isVideoSite, loadStrings, qualityLabel, siteOf, translator } from "./helpers.js";

const $ = (id) => document.getElementById(id);
const ask = (msg) => chrome.runtime.sendMessage(msg);
const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });

// Settings
const s = { ...DEFAULTS, language: "", ...(await chrome.storage.sync.get([...Object.keys(DEFAULTS), "language"])) };

// Interface language: translate the page's own texts, then everything set below goes through t().
const { code, strings } = await loadStrings(s.language);
const t = translator(strings);
document.documentElement.lang = code;
document.documentElement.dir = code === "ar" || code === "ur" ? "rtl" : "ltr";
for (const el of document.querySelectorAll("h1 ~ *, h1 ~ * *")) {
  if (el.placeholder) el.placeholder = t(el.placeholder);
  if (el.children.length === 0 && el.textContent.trim() && el.tagName !== "SCRIPT") el.textContent = t(el.textContent.trim());
}
const pick = $("language");
pick.append(new Option(t("Same as the browser"), ""));
for (const [c, name] of Object.entries(LANGUAGES)) pick.append(new Option(name, c));
pick.value = s.language in LANGUAGES ? s.language : "";
pick.onchange = async () => {
  await chrome.storage.sync.set({ language: pick.value });
  location.reload();
};

$("enabled").checked = s.enabled;
$("video").checked = s.videoButton;
$("min").value = s.minSizeMB;
$("skip").value = s.skipTypes;
$("enabled").onchange = () => chrome.storage.sync.set({ enabled: $("enabled").checked });
$("video").onchange = () => chrome.storage.sync.set({ videoButton: $("video").checked });
$("vbCorner").value = s.videoButtonCorner;
$("vbSize").value = s.videoButtonSize;
$("vbColor").value = s.videoButtonColor;
$("vbText").checked = s.videoButtonText;
$("vbCorner").onchange = () => chrome.storage.sync.set({ videoButtonCorner: $("vbCorner").value });
$("vbSize").onchange = () => chrome.storage.sync.set({ videoButtonSize: $("vbSize").value });
$("vbColor").onchange = () => chrome.storage.sync.set({ videoButtonColor: $("vbColor").value });
$("vbText").onchange = () => chrome.storage.sync.set({ videoButtonText: $("vbText").checked });
$("min").onchange = () => chrome.storage.sync.set({ minSizeMB: Math.max(0, Number($("min").value) || 0) });
$("skip").onchange = () => chrome.storage.sync.set({ skipTypes: $("skip").value });
$("sites").value = s.skipSites;
$("sites").onchange = () => chrome.storage.sync.set({ skipSites: $("sites").value });

// Keep downloads in the browser for a while, or always for this site.
function showPause(until) {
  const paused = until > Date.now();
  $("pause").textContent = t(paused ? "Resume taking over" : "Pause for 30 minutes");
  $("paused").hidden = !paused;
  if (paused) $("paused").textContent = t("Downloads stay in the browser until {time}.", { time: new Date(until).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) });
}
showPause(s.pausedUntil);
$("pause").onclick = async () => {
  const { pausedUntil = 0 } = await chrome.storage.sync.get("pausedUntil");
  const until = pausedUntil > Date.now() ? 0 : Date.now() + 30 * 60 * 1000;
  await chrome.storage.sync.set({ pausedUntil: until });
  showPause(until);
};
const site = tab && /^https?:/i.test(tab.url || "") ? siteOf(tab.url) : "";
$("thisSite").hidden = !site;
if (site) $("thisSite").textContent = t("Keep downloads from {site} in the browser", { site });
$("thisSite").onclick = async () => {
  const list = $("sites").value.split(/[\s,]+/).filter(Boolean);
  if (!list.map(siteOf).includes(site)) list.push(site);
  $("sites").value = list.join(", ");
  await chrome.storage.sync.set({ skipSites: $("sites").value });
};

// Site access (Firefox asks for it separately)
const all = { origins: ["<all_urls>"] };
if (chrome.permissions && !(await chrome.permissions.contains(all))) {
  $("access").hidden = false;
  $("grant").onclick = () => chrome.permissions.request(all).then((ok) => ($("access").hidden = ok));
}

// One link
$("send").onclick = async () => {
  const url = $("link").value.trim();
  if (!/^https?:\/\//i.test(url)) return;
  const r = await ask({ type: "send-link", url, referrer: tab && tab.url });
  $("link").value = r && r.ok ? "" : url;
};

function item(url, label, extra) {
  const li = document.createElement("li");
  const span = document.createElement("span");
  span.textContent = label;
  span.title = url;
  li.append(extra, span);
  return li;
}

// YouTube, Instagram, Facebook, X … : the app saves the page's video in the best quality
if (tab && isVideoSite(tab.url)) {
  $("siteBox").hidden = false;
  const go = (audioOnly, b) => async () => {
    const r = await ask({ type: "send-link", url: tab.url, referrer: tab.url, site: true, audioOnly });
    b.textContent = t(r && r.ok ? "Sent" : "Failed");
  };
  $("siteVideo").onclick = go(false, $("siteVideo"));
  $("siteAudio").onclick = go(true, $("siteAudio"));
}

// A profile on Instagram, Facebook, TikTok, X, Threads, Pinterest …: every photo and video of it, saved by the app's
// gallery engine with this browser's sign-in for the site (see "Download all" in background.js). Nothing scrolls.
if (tab && (await ask({ type: "profile-check", url: tab.url }))?.ok) {
  $("profileBox").hidden = false;
  $("profileAll").onclick = async () => {
    const r = await ask({ type: "profile-all", url: tab.url });
    $("profileCount").textContent = t(r && r.ok ? "Opened in Nexonic's Profile Batch" : "Open Nexonic first");
    $("profileAll").disabled = true;
  };
}

// Media seen on this tab
if (tab) {
  const { media = [] } = (await ask({ type: "media-list", tabId: tab.id })) || {};
  $("noMedia").hidden = media.length > 0;
  for (const m of media) {
    const b = document.createElement("button");
    b.textContent = t(m.protected ? "Protected" : "Download");
    b.disabled = !!m.protected;
    b.title = t(m.protected ? "The site protects this video, so it can't be saved" : "Best quality");
    b.onclick = async () => {
      const msg = { type: "send-link", url: m.url, referrer: tab.url };
      if (m.kind === "stream") Object.assign(msg, { stream: true, fileName: m.name });
      const r = await ask(msg);
      b.textContent = t(r && r.ok ? "Sent" : "Failed");
    };
    const li = item(m.url, `${m.name} · ${qualityLabel(m)}`, document.createTextNode(""));
    li.append(b);
    $("media").append(li);
  }
}

// Videos on this page (Instagram, Facebook, TikTok, YouTube, X …): every post or video the page shows, one click for all
// of them. They go to the app as video-site links (Instagram posts to the gallery engine, photos included).
if (tab && isVideoSite(tab.url)) {
  const [{ result: posts = [] } = {}] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: videosOnPage }).catch(() => []);
  if (posts.length) {
    $("postsBox").hidden = false;
    for (const p of posts) {
      const box = document.createElement("input");
      box.type = "checkbox";
      box.checked = true;
      box.value = p.url;
      $("posts").append(item(p.url, p.label, box));
    }
    const postBoxes = () => [...$("posts").querySelectorAll("input")];
    $("postsAll").onclick = () => postBoxes().forEach((b) => (b.checked = true));
    $("postsNone").onclick = () => postBoxes().forEach((b) => (b.checked = false));
    const sendPosts = (later) => async () => {
      const urls = postBoxes().filter((b) => b.checked).map((b) => b.value);
      if (!urls.length) return;
      const r = await ask({ type: "send-videos", urls, later, referrer: tab.url });
      $("postsDownload").textContent = r && r.ok ? t("Sent {count}", { count: r.sent }) : t("Failed");
    };
    $("postsDownload").onclick = sendPosts(false);
    $("postsLater").onclick = sendPosts(true);
  }
}

/** Runs in the page: the links of the posts and videos it shows (self-contained). */
function videosOnPage() {
  const patterns = [
    [/^https:\/\/(www\.)?instagram\.com\/(?:[^/]+\/)?(p|reel|tv)\/([\w-]+)/, (m) => `https://www.instagram.com/${m[2] === "p" ? "p" : "reel"}/${m[3]}/`],
    [/^https:\/\/(www\.|web\.|m\.)?facebook\.com\/(?:[^/]+\/)?(?:reel|videos)\/(?:[^/]+\/)?(\d{6,})/, (m) => `https://www.facebook.com/reel/${m[2]}/`],
    [/^https:\/\/(www\.|web\.|m\.)?facebook\.com\/watch\/?\?v=(\d{6,})/, (m) => `https://www.facebook.com/watch/?v=${m[2]}`],
    [/^https:\/\/(www\.)?tiktok\.com\/@([\w.-]+)\/video\/(\d+)/, (m) => `https://www.tiktok.com/@${m[2]}/video/${m[3]}`],
    [/^https:\/\/(www\.|m\.)?youtube\.com\/(?:watch\?v=|shorts\/)([\w-]{11})/, (m) => `https://www.youtube.com/watch?v=${m[2]}`],
    [/^https:\/\/(x|twitter)\.com\/(\w{1,15})\/status\/(\d+)\/video/, (m) => `https://x.com/${m[2]}/status/${m[3]}`],
  ];
  const seen = new Map();
  for (const href of [location.href, ...[...document.querySelectorAll("a[href]")].map((a) => a.href)]) {
    for (const [re, link] of patterns) {
      const m = href.match(re);
      if (m && !seen.has(link(m))) {
        const a = [...document.querySelectorAll("a[href]")].find((x) => x.href === href);
        const label = ((a && (a.getAttribute("aria-label") || a.textContent || (a.querySelector("img") || {}).alt)) || "").trim().replace(/\s+/g, " ").slice(0, 80);
        seen.set(link(m), label || link(m));
      }
    }
    if (seen.size >= 500) break;
  }
  return [...seen].map(([url, label]) => ({ url, label }));
}

// Links on this page
let found = [];
$("find").onclick = async () => {
  if (!tab) return;
  const { links = [] } = (await ask({ type: "page-links", tabId: tab.id })) || {};
  found = filterLinks(links, $("types").value);
  $("links").textContent = "";
  for (const url of found) {
    const box = document.createElement("input");
    box.type = "checkbox";
    box.checked = true;
    box.value = url;
    $("links").append(item(url, decodeURI(url.split("/").pop() || url), box));
  }
  $("links").hidden = $("linkActions").hidden = found.length === 0;
  if (!found.length) $("find").textContent = t("None found");
};
const boxes = () => [...$("links").querySelectorAll("input")];
$("all").onclick = () => boxes().forEach((b) => (b.checked = true));
$("none").onclick = () => boxes().forEach((b) => (b.checked = false));
async function sendChecked(later) {
  const urls = boxes().filter((b) => b.checked).map((b) => b.value);
  if (!urls.length) return;
  const r = await ask({ type: "send-links", urls, later, referrer: tab && tab.url });
  $("download").textContent = r && r.ok ? t("Sent {count}", { count: r.sent }) : t("Failed");
}
$("download").onclick = () => sendChecked(false);
$("later").onclick = () => sendChecked(true);

// App status
const r = await ask({ type: "ping" });
$("status").textContent = r && r.ok ? t("Connected to the app ({version})", { version: r.version }) : t(TROUBLESHOOT);
$("status").className = r && r.ok ? "ok" : "bad";

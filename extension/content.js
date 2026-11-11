// "Download this video" button over audio and video players on web pages.
(() => {
  if (window.__nexonicVideoButton) return;
  window.__nexonicVideoButton = true;

  // The interface language's strings, kept by the background script ({} = English).
  let strings = {};
  const t = (english) => strings[english] || english;
  chrome.storage.local.get({ uiStrings: {} }, (s) => (strings = s.uiStrings || {}));
  chrome.storage.onChanged.addListener((c, area) => {
    if (area === "local" && c.uiStrings) strings = c.uiStrings.newValue || {};
  });

  let enabled = true;
  // Where and how the button is drawn (extension menu → Settings): corner, size, colour, text or icon only.
  let look = { corner: "top-right", size: "normal", color: "#008f9e", text: true };
  const LOOK = { videoButtonCorner: "top-right", videoButtonSize: "normal", videoButtonColor: "#008f9e", videoButtonText: true };
  function applyLook(s) {
    look = {
      corner: ["top-right", "top-left", "bottom-right", "bottom-left"].includes(s.videoButtonCorner) ? s.videoButtonCorner : "top-right",
      size: ["small", "normal", "large"].includes(s.videoButtonSize) ? s.videoButtonSize : "normal",
      color: /^#[0-9a-fA-F]{6}$/.test(s.videoButtonColor || "") ? s.videoButtonColor : "#008f9e",
      text: s.videoButtonText !== false,
    };
    const font = { small: 11, normal: 12, large: 15 }[look.size];
    const pad = { small: "4px 7px", normal: "6px 10px", large: "8px 14px" }[look.size];
    button.style.cssText = `font-size:${font}px;padding:${pad};background:${look.color}`;
    setLabel();
  }
  chrome.storage.sync.get({ videoButton: true, ...LOOK }, (s) => { enabled = s.videoButton; applyLook(s); });
  chrome.storage.onChanged.addListener((c, area) => {
    if (area !== "sync") return;
    if (c.videoButton) enabled = c.videoButton.newValue;
    if (Object.keys(LOOK).some((k) => c[k])) chrome.storage.sync.get(LOOK, applyLook);
  });

  const host = document.createElement("div");
  host.style.cssText = "position:fixed;z-index:2147483647;top:0;left:0;display:none";
  const root = host.attachShadow({ mode: "closed" });
  root.innerHTML = `
    <style>
      button{font:600 12px system-ui,sans-serif;color:#fff;background:#008f9e;border:0;border-radius:6px;padding:6px 10px;
        cursor:pointer;box-shadow:0 2px 8px rgba(0,0,0,.35)}
      button:hover{filter:brightness(.85)}
      ul{list-style:none;margin:4px 0 0;padding:4px;background:#fff;color:#111;border-radius:6px;max-width:360px;
        box-shadow:0 2px 10px rgba(0,0,0,.35);font:12px system-ui,sans-serif}
      li{padding:5px 8px;border-radius:4px;cursor:pointer;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
      li:hover{background:#e3f4f6}
      .note{color:#666;cursor:default}
    </style>
    <button type="button">Download this video</button>
    <ul hidden></ul>`;
  const button = root.querySelector("button");
  let current = t("Download this video");
  // Text, or only an arrow (the text stays as its tooltip and for screen readers).
  function setLabel() {
    button.textContent = look.text ? current : "\u2B07";
    button.title = current;
    button.setAttribute("aria-label", current);
  }
  const list = root.querySelector("ul");
  let target = null;
  let hideTimer = 0;

  const label = (m) =>
    m.kind === "stream"
      ? ` (${[m.height ? `${m.height}p` : "", m.live ? t("live") : "", t("best quality")].filter(Boolean).join(", ")})`
      : m.size > 0 ? ` (${(m.size / 1048576).toFixed(1)} MB)` : "";

  function place() {
    if (!target || !target.isConnected) return hide();
    const r = target.getBoundingClientRect();
    if (r.width < 160 || r.height < 40 || r.bottom < 0 || r.top > innerHeight) return hide();
    host.style.display = "block";
    const w = button.offsetWidth || 180, h = button.offsetHeight || 28;
    const left = look.corner.endsWith("left") ? r.left + 8 : r.right - w - 8;
    const top = look.corner.startsWith("bottom") ? r.bottom - h - 8 : r.top + 8;
    host.style.left = `${Math.min(innerWidth - w - 4, Math.max(4, left))}px`;
    host.style.top = `${Math.min(innerHeight - h - 4, Math.max(4, top))}px`;
  }

  function hide() {
    host.style.display = "none";
    list.hidden = true;
    target = null;
  }

  function show(el) {
    if (!enabled) return;
    clearTimeout(hideTimer);
    target = el;
    current = t(el.tagName === "AUDIO" ? "Download this audio" : "Download this video");
    setLabel();
    if (!host.isConnected) document.documentElement.appendChild(host);
    place();
  }

  // YouTube, Instagram, Facebook, X, TikTok … : the app's video engine saves the post's video in the best quality.
  const SITES = /(^|\.)(youtube\.com|youtu\.be|youtube-nocookie\.com|instagram\.com|facebook\.com|fb\.watch|x\.com|twitter\.com|tiktok\.com|vimeo\.com|dailymotion\.com|reddit\.com|twitch\.tv|linkedin\.com|pinterest\.com|threads\.net|bilibili\.com|rumble\.com|streamable\.com)$/i;
  const onVideoSite = SITES.test(location.hostname);
  const POST = /\/(watch|shorts|reel|reels|p|tv|videos?|status|post|clip)\b|\/v\//i;

  /** The link of the post a player belongs to (feeds show many videos on one page). */
  function postLink(el) {
    if (/\/(watch|shorts|reel|p|status|videos?)\b/i.test(location.pathname)) return location.href;
    for (let n = el, i = 0; n && i < 25; n = n.parentElement, i++) {
      const a = n.querySelector && [...n.querySelectorAll("a[href]")].find((x) => POST.test(new URL(x.href, location.href).pathname));
      if (a) return new URL(a.href, location.href).href;
    }
    return location.href;
  }

  function send(m) {
    const msg = { type: "send-link", url: m.url, referrer: location.href };
    if (m.kind === "stream") Object.assign(msg, { stream: true, fileName: m.name });
    if (m.kind === "site") Object.assign(msg, { site: true, audioOnly: !!m.audioOnly });
    chrome.runtime.sendMessage(msg, (r) => {
      button.textContent = t(r && r.ok ? "Sent to Nexonic" : "Open Nexonic first");
      setTimeout(hide, 1500);
    });
  }

  button.addEventListener("click", (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (onVideoSite && target) {
      // Shown at once: the app picks the best quality (its download window can choose another one).
      list.textContent = "";
      const link = postLink(target);
      for (const [label, audioOnly] of [["Video, best quality", false], ["Sound only", true]]) {
        const li = document.createElement("li");
        li.textContent = t(label);
        li.onclick = () => send({ url: link, kind: "site", audioOnly });
        list.appendChild(li);
      }
      list.hidden = false;
      return;
    }
    const src = target && (target.currentSrc || target.src);
    chrome.runtime.sendMessage({ type: "video-button", src }, (r) => {
      const media = (r && r.media) || [];
      list.textContent = "";
      if (media.length === 1) return send(media[0]);
      if (!media.length) {
        const li = document.createElement("li");
        li.className = "note";
        li.textContent = t(r && r.locked
          ? "This video is copy-protected by the site, so it can't be saved."
          : "No video found yet. Play the video for a few seconds, then click again.");
        li.style.whiteSpace = "normal";
        list.appendChild(li);
      }
      for (const m of media) {
        const li = document.createElement("li");
        li.textContent = `${m.name}${label(m)}`;
        li.title = m.url;
        li.onclick = () => send(m);
        list.appendChild(li);
      }
      list.hidden = false;
    });
  });

  host.addEventListener("mouseenter", () => clearTimeout(hideTimer));
  host.addEventListener("mouseleave", () => (hideTimer = setTimeout(hide, 800)));
  document.addEventListener(
    "mouseover",
    (e) => {
      if (!enabled) return;
      const el = e.target && e.target.closest ? e.target.closest("video, audio") : null;
      if (el) show(el);
      else if (target && !host.contains(e.target)) {
        const r = target.getBoundingClientRect();
        const inside = e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom;
        if (!inside) {
          clearTimeout(hideTimer);
          hideTimer = setTimeout(hide, 800);
        }
      }
    },
    true,
  );
  // Re-position at most once per frame, and only while the button is showing.
  let queued = false;
  const placeSoon = () => {
    if (!target || queued) return;
    queued = true;
    requestAnimationFrame(() => {
      queued = false;
      place();
    });
  };
  addEventListener("scroll", placeSoon, { capture: true, passive: true });
  addEventListener("resize", placeSoon, { passive: true });

  // Holding Alt while clicking any link tells the extension to let the browser download natively
  window.addEventListener(
    "click",
    (e) => {
      if (e.altKey) {
        try { chrome.runtime.sendMessage({ type: "bypassInterception" }); } catch (_) {}
      }
    },
    true,
  );
})();

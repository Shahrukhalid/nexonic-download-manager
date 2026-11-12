// Every video of an Instagram or Facebook profile, read from the page the user has open, with whatever the user is
// signed in to in this browser (the extension never signs in and never sees a password). The page is scrolled to its
// end, as the user would, and the video links are collected on the way. Two ways in:
//  1. Nexonic's Profile Batch opened this page with a one-time token (#nexonic-collect=token:max): the links go back to
//     the app through the browser helper and the tab closes.
//  2. The popup's "Download all videos of this profile" (only offered when signed in): the videos go to Nexonic.
// Keep the readers in step with InstagramPage.Script and SocialPages.FacebookScript in the apps.
(() => {
  const host = location.hostname;
  const site = /(^|\.)instagram\.com$/.test(host) ? "instagram" : /(^|\.)facebook\.com$/.test(host) ? "facebook" : null;
  if (!site || window.__nexonicCollect) return;
  window.__nexonicCollect = true;

  function instagram() {
    const videos = [];
    for (const a of document.querySelectorAll('a[href*="/reel/"], a[href*="/p/"]')) {
      const m = new URL(a.getAttribute("href"), location.href).pathname.match(/\/(p|reel)\/([A-Za-z0-9_-]+)/);
      if (!m) continue;
      const alt = (a.querySelector("img") || {}).alt || "";
      const video = m[1] === "reel" || /^Video by /.test(alt) || !!a.querySelector('svg[aria-label="Clip"], svg[aria-label="Video"], svg[aria-label="Reel"]');
      if (video) videos.push({ u: "https://www.instagram.com/" + m[1] + "/" + m[2] + "/", t: alt });
    }
    return videos;
  }

  function facebook() {
    const videos = [];
    for (const a of document.querySelectorAll('a[href*="/reel/"], a[href*="/videos/"], a[href*="watch/?v="], a[href*="watch?v="]')) {
      const link = new URL(a.getAttribute("href"), location.href);
      let id = null, reel = false, m;
      if ((m = link.pathname.match(/\/reel\/(\d+)/))) { id = m[1]; reel = true; }
      else if ((m = link.pathname.match(/\/videos\/(?:[^/]+\/)?(\d{6,})/))) id = m[1];
      else if (/^\d{6,}$/.test(link.searchParams.get("v") || "")) id = link.searchParams.get("v");
      if (id) videos.push({ u: reel ? "https://www.facebook.com/reel/" + id + "/" : "https://www.facebook.com/watch/?v=" + id,
                           t: (a.getAttribute("aria-label") || a.innerText || "").trim().slice(0, 200) });
    }
    return videos;
  }

  const read = site === "instagram" ? instagram : facebook;
  const reserved = /^\/(p|reel|reels|tv|stories|explore|accounts|direct|watch|groups|events|marketplace|gaming|login|share|photo|search|notifications|messages|settings)(\/|$)/;
  const isProfile = () => location.pathname.length > 1 && !reserved.test(location.pathname);
  const signedIn = () => (site === "instagram" ? /(^|;\s*)ds_user_id=/ : /(^|;\s*)c_user=/).test(document.cookie);
  let running = false;

  /** Scrolls to the end of the profile (nothing new for ~9 seconds) or until max videos; report(count) as they come. */
  async function collect(max, report) {
    if (running) return null;
    running = true;
    const found = new Map();
    try {
      for (let i = 0, idle = 0; i < 2400 && idle < 6 && found.size < max; i++) {
        await new Promise((r) => setTimeout(r, 1500));
        let grew = false;
        for (const v of read()) if (!found.has(v.u) && found.size < max) { found.set(v.u, v); grew = true; }
        idle = grew || (found.size === 0 && i < 10) ? 0 : idle + 1;
        if (grew) report(found.size);
        window.scrollBy(0, Math.max(window.innerHeight, 800));
      }
    } finally { running = false; }
    return { posts: found.size, videos: [...found.values()] };
  }

  // 1. Opened by the app: the token is kept for the tab, so the site's own redirects don't lose it.
  let job = null;
  const m = location.hash.match(/^#nexonic-collect=([0-9a-f]{32}):(\d{1,4})$/);
  try {
    if (m) {
      job = { token: m[1], max: Number(m[2]) };
      sessionStorage.setItem("nexonic-collect", JSON.stringify(job));
      history.replaceState(history.state, "", location.href.split("#")[0]);
    } else job = JSON.parse(sessionStorage.getItem("nexonic-collect") || "null");
  } catch (_) {}
  if (job && /^[0-9a-f]{32}$/.test(job.token)) {
    const send = (stage, data) => chrome.runtime.sendMessage({ type: "profile-links", token: job.token, stage, data }).catch(() => {});
    send("started", { posts: 0, videos: [] });
    collect(job.max, (n) => send("started", { posts: n, videos: [] })).then((r) => {
      try { sessionStorage.removeItem("nexonic-collect"); } catch (_) {}
      if (r) send("done", r);
    });
  }

  // 2. The popup asks what this page is, and starts "Download all videos of this profile".
  chrome.runtime.onMessage.addListener((msg, _sender, respond) => {
    if (msg && msg.type === "profile-check") { respond({ site, profile: isProfile(), signedIn: signedIn(), running }); return; }
    if (msg && msg.type === "profile-collect") {
      const free = !running;
      collect(5000, (n) => chrome.runtime.sendMessage({ type: "profile-progress", count: n }).catch(() => {}))
        .then((r) => r && chrome.runtime.sendMessage({ type: "profile-send", videos: r.videos, later: !!msg.later }).catch(() => {}));
      respond({ ok: free });
    }
  });
})();

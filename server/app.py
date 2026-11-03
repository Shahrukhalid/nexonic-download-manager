"""Nexonic video server: the free, open-source video engine (yt-dlp) behind the iPhone shortcut.

The "Nexonic Download" shortcut sends it a page link (YouTube, Instagram, Facebook, TikTok, X and 1,000+ other sites);
it finds the video and sends the file back, so the shortcut can save it to Photos or Files.

  GET /download?url=<page link>&key=<access key>   -> the video file (or a short text message if there is none)
  GET /health                                     -> "ok"

Set NDM_KEY to an access key so only your shortcut can use the server. Optional: NDM_COOKIES (a cookies.txt file) for
videos that need a signed-in account, NDM_MAX_MB (largest file, default 1500).
"""
import ipaddress
import os
import re
import socket
import subprocess
import threading
import time
import urllib.parse
import urllib.request

from flask import Flask, Response, request
import yt_dlp

app = Flask(__name__)

KEY = os.environ.get("NDM_KEY", "")
COOKIES = os.environ.get("NDM_COOKIES") or None
MAX_BYTES = int(os.environ.get("NDM_MAX_MB", "1500")) * 1024 * 1024
PER_HOUR = int(os.environ.get("NDM_PER_HOUR", "60"))
# One file with picture and sound together, MP4 first (Photos needs MP4/MOV), up to 1080p.
FORMAT = "b[ext=mp4][vcodec!=none][acodec!=none][height<=1080]/b[vcodec!=none][acodec!=none][height<=1080]/b"

_hits: dict[str, list[float]] = {}
_lock = threading.Lock()


def message(text: str, status: int = 422) -> Response:
    """A short text the shortcut shows to the user (it arrives as a .txt file)."""
    r = Response(text + "\n", status=status, mimetype="text/plain")
    r.headers["Content-Disposition"] = 'attachment; filename="Nexonic-message.txt"'
    return r


def allowed(ip: str) -> bool:
    now = time.time()
    with _lock:
        recent = [t for t in _hits.get(ip, []) if now - t < 3600]
        if len(recent) >= PER_HOUR:
            _hits[ip] = recent
            return False
        recent.append(now)
        _hits[ip] = recent
        return True


def public_web_link(url: str) -> bool:
    """Only http(s) links to public internet addresses (never this server's own network)."""
    try:
        u = urllib.parse.urlsplit(url)
    except ValueError:
        return False
    if u.scheme not in ("http", "https") or not u.hostname:
        return False
    try:
        infos = socket.getaddrinfo(u.hostname, None)
    except OSError:
        return False
    for info in infos:
        ip = ipaddress.ip_address(info[4][0])
        if not ip.is_global:
            return False
    return True


def first_link(text: str) -> str | None:
    m = re.search(r"https?://\S+", text or "")
    return m.group(0).rstrip(").,;'\"") if m else None


def file_name(title: str | None, ext: str) -> str:
    base = re.sub(r'[\\/:*?"<>|\r\n\t]+', " ", title or "video").strip()[:100] or "video"
    return f"{base}.{ext}"


def friendly(err: str) -> str:
    e = err.lower()
    if "unsupported url" in e:
        return "No video found on this page."
    if "login" in e or "sign in" in e or "cookies" in e or "private" in e:
        return "This video needs a signed-in account or is private, so it can't be saved."
    if "not available" in e or "unavailable" in e or "removed" in e:
        return "This video is not available."
    return "The video could not be read. Try again later."


def ydl_options() -> dict:
    o = {"format": FORMAT, "quiet": True, "no_warnings": True, "noplaylist": True, "socket_timeout": 30}
    if COOKIES:
        o["cookiefile"] = COOKIES
    return o


def stream_url(url: str, headers: dict, size_limit: int):
    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, timeout=60) as r:
        sent = 0
        while True:
            chunk = r.read(256 * 1024)
            if not chunk:
                return
            sent += len(chunk)
            if sent > size_limit:
                return
            yield chunk


def stream_tool(page: str, size_limit: int):
    """Streams (HLS and others) that need yt-dlp itself: it writes the file to its output."""
    cmd = ["yt-dlp", "-q", "--no-playlist", "-f", FORMAT, "-o", "-", page]
    if COOKIES:
        cmd[1:1] = ["--cookies", COOKIES]
    p = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    sent = 0
    try:
        while True:
            chunk = p.stdout.read(256 * 1024)
            if not chunk:
                return
            sent += len(chunk)
            if sent > size_limit:
                return
            yield chunk
    finally:
        p.kill()


@app.get("/health")
def health():
    return "ok"


@app.get("/download")
def download():
    if KEY and request.args.get("key") != KEY:
        return message("This shortcut is not allowed to use the server (wrong access key).", 403)
    client = request.headers.get("X-Forwarded-For", request.remote_addr or "").split(",")[0].strip()
    if not allowed(client):
        return message("Too many downloads in the last hour. Please wait a little.", 429)
    page = first_link(request.args.get("url", ""))
    if not page or not public_web_link(page):
        return message("Share a link to a video page (it starts with http:// or https://).", 400)
    try:
        with yt_dlp.YoutubeDL(ydl_options()) as ydl:
            info = ydl.extract_info(page, download=False)
    except Exception as e:  # yt-dlp reports every problem as an exception with a readable text
        return message(friendly(str(e)))
    if info.get("_type") == "playlist":
        entries = [e for e in info.get("entries") or [] if e]
        if not entries:
            return message("No video found on this page.")
        info = entries[0]
    ext = info.get("ext") or "mp4"
    size = info.get("filesize") or info.get("filesize_approx")
    if size and size > MAX_BYTES:
        return message("This video is too large for the shortcut. Use the Nexonic app for big files.")
    name = file_name(info.get("title"), ext)
    if info.get("url") and info.get("protocol") in ("http", "https"):
        body = stream_url(info["url"], info.get("http_headers") or {}, MAX_BYTES)
    else:
        body = stream_tool(page, MAX_BYTES)
    r = Response(body, mimetype="video/mp4" if ext == "mp4" else "application/octet-stream")
    r.headers["Content-Disposition"] = "attachment; filename*=UTF-8''" + urllib.parse.quote(name)
    if size:
        r.headers["X-Nexonic-Size"] = str(size)
    return r

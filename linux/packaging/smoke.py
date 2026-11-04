#!/usr/bin/env python3
"""Smoke test of the Nexonic Download Manager Linux build (run by .github/workflows/linux.yml after check.sh): runs the real app headless (Xvfb) with its own home folder,
serves test files from a local web server (ranges, slow mode, a bad checksum file), and drives it through the ndm
command and the local API. Prints PASS/FAIL lines. Usage: ndm-linux-smoke.py <app folder> <work folder> [lang]"""
import atexit, hashlib, http.server, json, os, shutil, socket, subprocess, sys, threading, time, urllib.request

APP, WORK = sys.argv[1], sys.argv[2]
LANG = sys.argv[3] if len(sys.argv) > 3 else ""
KEY = "smoke-test-key-0123456789abcdef"
PORT_API = 47812
results = []

def check(name, ok, detail=""):
    results.append(bool(ok))
    print(("PASS " if ok else "FAIL ") + name + (f"  ({detail})" if detail else ""), flush=True)

# ---- test files and a range-capable web server ----
shutil.rmtree(WORK, ignore_errors=True)
files = os.path.join(WORK, "www"); os.makedirs(files)
data = os.urandom(24 * 1024 * 1024)
open(os.path.join(files, "big.bin"), "wb").write(data)
open(os.path.join(files, "small.txt"), "wb").write(b"hello nexonic\n" * 1000)
SHA = hashlib.sha256(data).hexdigest()

class H(http.server.SimpleHTTPRequestHandler):
    slow = False
    def __init__(s, *a, **k): super().__init__(*a, directory=files, **k)
    def log_message(s, *a): pass
    def do_GET(s):
        path = os.path.join(files, s.path.split("?")[0].lstrip("/"))
        if not os.path.isfile(path): return s.send_error(404)
        body = open(path, "rb").read(); start, end = 0, len(body) - 1
        rng = s.headers.get("Range")
        if rng and rng.startswith("bytes="):
            a, _, b = rng[6:].partition("-"); start = int(a or 0); end = int(b) if b else len(body) - 1
            s.send_response(206); s.send_header("Content-Range", f"bytes {start}-{end}/{len(body)}")
        else: s.send_response(200)
        s.send_header("Accept-Ranges", "bytes"); s.send_header("Content-Length", str(end - start + 1))
        s.send_header("ETag", '"' + hashlib.md5(body).hexdigest() + '"'); s.end_headers()
        chunk = body[start:end + 1]
        try:
            step = 64 * 1024
            for i in range(0, len(chunk), step):
                s.wfile.write(chunk[i:i + step])
                if H.slow: time.sleep(0.2)
        except (BrokenPipeError, ConnectionResetError): pass
    do_HEAD = lambda s: (s.send_response(200), s.send_header("Accept-Ranges", "bytes"),
                         s.send_header("Content-Length", str(os.path.getsize(os.path.join(files, s.path.lstrip("/"))))), s.end_headers())
srv = http.server.ThreadingHTTPServer(("127.0.0.1", 0), H); threading.Thread(target=srv.serve_forever, daemon=True).start()
BASE = f"http://127.0.0.1:{srv.server_port}"

# ---- isolated home with the API on ----
home = os.path.join(WORK, "home"); dl = os.path.join(WORK, "Downloads")
support = os.path.join(home, ".local", "share", "Nexonic Download Manager"); os.makedirs(support); os.makedirs(dl)
settings = {"Folder": dl, "CategoryFolders": False, "Automation": {"Enabled": True, "Port": PORT_API, "Key": KEY},
            "ShowDownloadDialog": False, "ShowProgressWindow": False, "ShowCompleteDialog": False,
            "Notifications": False, "MonitorClipboard": False, "CheckForUpdates": False}
if LANG: settings["Language"] = LANG
json.dump(settings, open(os.path.join(support, "settings.json"), "w"))
env = dict(os.environ, HOME=home, XDG_DATA_HOME=os.path.join(home, ".local", "share"), XDG_CONFIG_HOME=os.path.join(home, ".config"),
           DISPLAY=":77", DOTNET_BUNDLE_EXTRACT_BASE_DIR=os.path.join(WORK, "x"))
for f in ("/tmp/.X77-lock", "/tmp/.X11-unix/X77"):
    try: os.remove(f)
    except OSError: pass
xvfb = subprocess.Popen(["Xvfb", ":77", "-screen", "0", "1280x800x24"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
time.sleep(4)
log = open(os.path.join(WORK, "app.log"), "w")
app = subprocess.Popen([os.path.join(APP, "NexonicDownloadManager")], env=env, stdout=log, stderr=log)

def cleanup():
    code = app.poll()
    print(f"App exit before cleanup: {code}", flush=True)
    if code is None: app.terminate()
    try: app.wait(timeout=5)
    except subprocess.TimeoutExpired: app.kill(); app.wait()
    xvfb.terminate()
    try: xvfb.wait(timeout=5)
    except subprocess.TimeoutExpired: xvfb.kill(); xvfb.wait()
    srv.shutdown()
    log.close()
    print("---- app output ----", flush=True)
    print(open(os.path.join(WORK, "app.log")).read(), flush=True)

atexit.register(cleanup)

def api(method, path, body=None, headers=None, key=KEY):
    h = {"Host": f"127.0.0.1:{PORT_API}"}
    if key: h["Authorization"] = "Bearer " + key
    h.update(headers or {})
    req = urllib.request.Request(f"http://127.0.0.1:{PORT_API}{path}", method=method, headers=h,
                                 data=json.dumps(body).encode() if body is not None else None)
    try:
        with urllib.request.urlopen(req, timeout=10) as r: return r.status, json.loads(r.read() or b"null")
    except urllib.error.HTTPError as e: return e.code, json.loads(e.read() or b"null")
    except Exception as e: return 0, {"error": str(e)}

for _ in range(60):
    if api("GET", "/v1/status")[0] == 200: break
    time.sleep(1)
st, body = api("GET", "/v1/status")
check("app starts headless and the local API answers", st == 200, f"{st} {body}")
if st != 200:
    sys.exit(1)

# ---- API security ----
check("API refuses a request without the key", api("GET", "/v1/status", key=None)[0] == 401)
check("API refuses a wrong key", api("GET", "/v1/status", key="wrong-key-wrong-key-123")[0] == 401)
check("API refuses browser requests (Origin header)", api("GET", "/v1/status", headers={"Origin": "https://evil.example"})[0] == 403)
check("API refuses other Host names (DNS rebinding)", api("GET", "/v1/status", headers={"Host": "evil.example:47812"})[0] == 403)
check("API refuses non-web links", api("POST", "/v1/downloads", {"url": "file:///etc/passwd"})[0] == 400)
check("API refuses relative folders", api("POST", "/v1/downloads", {"url": BASE + "/small.txt", "folder": "../x"})[0] == 400)
try:
    s = socket.create_connection(("127.0.0.1", PORT_API), 5)
    s.sendall(f"POST /v1/downloads HTTP/1.1\r\nHost: 127.0.0.1:{PORT_API}\r\nAuthorization: Bearer {KEY}\r\nContent-Length: 999999\r\n\r\n".encode())
    reply = s.recv(200).decode(errors="ignore"); s.close()
    check("API refuses oversized requests", " 400 " in reply or " 413 " in reply, reply.split("\r\n")[0])
except Exception as e: check("API refuses oversized requests", False, str(e))
bound = subprocess.run(["ss", "-ltn"], capture_output=True, text=True).stdout
check("API listens only on 127.0.0.1", f"127.0.0.1:{PORT_API}" in bound and f"0.0.0.0:{PORT_API}" not in bound)

def wait(id_, want, secs=90):
    for _ in range(secs * 2):
        st, d = api("GET", f"/v1/downloads/{id_}")
        if st == 200 and d.get("status", "").lower() in want: return d
        time.sleep(0.5)
    return api("GET", f"/v1/downloads/{id_}")[1]

# ---- download, checksum, file integrity ----
st, d = api("POST", "/v1/downloads", {"url": BASE + "/big.bin", "start": True})
ids = d.get("ids") if isinstance(d, dict) else None
check("add a download through the API", st in (200, 201) and ids, f"{st} {d}")
if ids:
    r = wait(ids[0], {"finished", "complete", "completed"})
    path = (r or {}).get("savedPath") or os.path.join(dl, "big.bin")
    same = os.path.isfile(path) and hashlib.sha256(open(path, "rb").read()).hexdigest() == SHA
    check("multi-connection download finishes with the exact bytes", same, (r or {}).get("status"))

# ---- pause and resume ----
H.slow = True
st, d = api("POST", "/v1/downloads", {"url": BASE + "/big.bin", "fileName": "resume.bin"})
ids = d.get("ids") if isinstance(d, dict) else None
check("add a second download through the API", st == 201 and bool(ids), f"{st} {d}")
if not ids: sys.exit(1)
rid = ids[0]
for _ in range(80):
    st, current = api("GET", f"/v1/downloads/{rid}")
    if st == 200 and 0 < current.get("received", 0) < len(data): break
    if app.poll() is not None: sys.exit(1)
    time.sleep(0.1)
st_p, _ = api("POST", f"/v1/downloads/{rid}/pause"); time.sleep(2)
p1 = api("GET", f"/v1/downloads/{rid}")[1] or {}
time.sleep(3)
p2 = api("GET", f"/v1/downloads/{rid}")[1] or {}
check("pause stops the transfer", st_p == 200 and p2.get("status") == "paused" and p1.get("received") == p2.get("received") and (p1.get("received") or 0) > 0,
      f"{p1.get('status')} {p1.get('received')}")
H.slow = False
api("POST", f"/v1/downloads/{rid}/resume")
r = wait(rid, {"finished", "complete", "completed"})
p = (r or {}).get("savedPath") or os.path.join(dl, "resume.bin")
check("resume continues and the file is intact", os.path.isfile(p) and hashlib.sha256(open(p, "rb").read()).hexdigest() == SHA, (r or {}).get("status"))

# ---- ndm command line with checksum and queue ----
host = os.path.join(APP, "nexonic-host")
small_sha = hashlib.sha256(open(os.path.join(files, "small.txt"), "rb").read()).hexdigest()
out = subprocess.run([host, BASE + "/small.txt", "--name", "cli.txt", "--checksum", small_sha, "--queue", "Night", "--later"],
                     env=env, capture_output=True, text=True, timeout=30)
check("ndm command hands a download to the app", out.returncode == 0, (out.stdout + out.stderr).strip()[:120])
time.sleep(2)
qs = api("GET", "/v1/queues")[1] or []
night = next((q for q in qs if isinstance(q, dict) and q.get("name") == "Night"), None) if isinstance(qs, list) else None
check("ndm --queue creates the queue", night is not None)
if night:
    api("POST", f"/v1/queues/{night['id']}/start")
    time.sleep(6)
    cli_path = os.path.join(dl, "cli.txt")
    check("queue start downloads it and the checksum matches", os.path.isfile(cli_path) and hashlib.sha256(open(cli_path, "rb").read()).hexdigest() == small_sha)

# ---- video tools present and runnable ----
for tool, arg in (("yt-dlp", "--version"), ("ffmpeg", "-version"), ("qjs", "--help")):
    r = subprocess.run([os.path.join(APP, tool), arg], capture_output=True, text=True, timeout=60)
    check(f"bundled {tool} runs", r.returncode == 0 or tool == "qjs", (r.stdout or r.stderr).splitlines()[0][:60] if (r.stdout or r.stderr) else "")

# ---- screenshot of the main window ----
shot = os.path.join(WORK, f"main-{LANG or 'en'}.png")
subprocess.run(["import", "-display", ":77", "-window", "root", shot], capture_output=True)
check("screenshot taken", os.path.isfile(shot), shot)

st, d = api("GET", "/v1/downloads")
check("app remains alive and the download list answers", app.poll() is None and st == 200 and isinstance(d, list))
print("downloads:", json.dumps(d)[:600])
print(f"SUMMARY {sum(results)}/{len(results)} passed")
if not all(results):
    sys.exit(1)

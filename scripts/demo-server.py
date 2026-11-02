#!/usr/bin/env python3
"""Serves one large made-up file with byte ranges on 127.0.0.1:<port>, so screenshots can show a real running download."""
import http.server, re, sys, time

SIZE = 900 * 1024 * 1024
CHUNK = b"\0" * 65536
# Optional speed limit in MB/s (second argument), so a download is still running when the pictures are taken.
PAUSE = len(CHUNK) / (float(sys.argv[2]) * 1024 * 1024) if len(sys.argv) > 2 else 0


class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):
        pass

    def do_GET(self):
        start, end = 0, SIZE - 1
        m = re.match(r"bytes=(\d+)-(\d*)", self.headers.get("Range", ""))
        if m:
            start = int(m.group(1))
            end = min(int(m.group(2)) if m.group(2) else SIZE - 1, SIZE - 1)
            self.send_response(206)
            self.send_header("Content-Range", f"bytes {start}-{end}/{SIZE}")
        else:
            self.send_response(200)
        self.send_header("Content-Type", "video/mp4")
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(end - start + 1))
        self.end_headers()
        left = end - start + 1
        try:
            while left > 0:
                n = min(left, len(CHUNK))
                self.wfile.write(CHUNK[:n])
                if PAUSE:
                    time.sleep(PAUSE)
                left -= n
        except (BrokenPipeError, ConnectionResetError):
            pass


http.server.ThreadingHTTPServer(("127.0.0.1", int(sys.argv[1]) if len(sys.argv) > 1 else 8099), Handler).serve_forever()

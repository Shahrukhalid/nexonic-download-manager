import unittest
from unittest import mock

import app as server


class ServerTest(unittest.TestCase):
    def setUp(self):
        server.KEY = "k"
        server._hits.clear()
        self.c = server.app.test_client()

    def test_health(self):
        self.assertEqual(self.c.get("/health").data, b"ok")

    def test_key_required(self):
        r = self.c.get("/download?url=https://example.com/v&key=bad")
        self.assertEqual(r.status_code, 403)
        self.assertIn(b"access key", r.data)

    def test_links_only_to_the_public_internet(self):
        for u in ["", "ftp://x", "http://127.0.0.1/x", "http://localhost/x", "http://169.254.169.254/latest", "http://10.0.0.1/"]:
            r = self.c.get("/download", query_string={"url": u, "key": "k"})
            self.assertEqual(r.status_code, 400, u)
            self.assertEqual(r.mimetype, "text/plain")

    def test_link_inside_shared_text(self):
        self.assertEqual(server.first_link("Look at this https://www.instagram.com/reel/abc/ wow"), "https://www.instagram.com/reel/abc/")

    def test_readable_errors(self):
        with mock.patch.object(server, "public_web_link", return_value=True), \
             mock.patch.object(server.yt_dlp.YoutubeDL, "extract_info", side_effect=Exception("ERROR: Unsupported URL: x")):
            r = self.c.get("/download", query_string={"url": "https://example.com/page", "key": "k"})
        self.assertEqual(r.data.strip(), b"No video found on this page.")
        self.assertIn("Nexonic-message.txt", r.headers["Content-Disposition"])

    def test_streams_the_video_with_its_name(self):
        info = {"title": "My: clip", "ext": "mp4", "url": "https://cdn.example.com/v.mp4", "protocol": "https", "http_headers": {}}
        with mock.patch.object(server, "public_web_link", return_value=True), \
             mock.patch.object(server.yt_dlp.YoutubeDL, "extract_info", return_value=info), \
             mock.patch.object(server, "stream_url", return_value=iter([b"abc"])):
            r = self.c.get("/download", query_string={"url": "https://example.com/page", "key": "k"})
        self.assertEqual(r.data, b"abc")
        self.assertEqual(r.mimetype, "video/mp4")
        self.assertIn("My%20%20clip.mp4", r.headers["Content-Disposition"])

    def test_rate_limit(self):
        server.PER_HOUR = 2
        try:
            codes = [self.c.get("/download", query_string={"url": "", "key": "k"}).status_code for _ in range(3)]
        finally:
            server.PER_HOUR = 60
        self.assertEqual(codes, [400, 400, 429])


if __name__ == "__main__":
    unittest.main()

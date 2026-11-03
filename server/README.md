# Nexonic video server

The free, open-source video engine (yt-dlp) as a small web service for the **Nexonic Download** iPhone shortcut.
The shortcut sends it a page link (YouTube, Instagram, Facebook, TikTok, X and 1,000+ other sites); the server finds
the video and sends the file back, and the shortcut saves it to Photos or Files.

## Run it on Render (free)

1. Sign in at [render.com](https://render.com) with your GitHub account.
2. **New → Blueprint**, pick this repository. Render reads `render.yaml` (in the repository root), builds `server/` and creates
   **nexonic-video** with a random access key (`NDM_KEY`).
3. When it shows *Live*, copy its address (like `https://nexonic-video.onrender.com`) and, under **Environment**, the
   value of `NDM_KEY`. Both go into the shortcut.

The free plan sleeps after 15 minutes without use: the first download after that waits about 30 seconds while it wakes.

## What it does

- `GET /download?url=<page>&key=<NDM_KEY>` sends the video (one file with picture and sound, MP4 first, up to 1080p),
  or a short text message (a `.txt` file) when there is no video, it's private, or the key is wrong.
- Only links to the public internet are accepted, and each address can download at most `NDM_PER_HOUR` (60) videos an
  hour. Files larger than `NDM_MAX_MB` (1500) are refused.
- yt-dlp updates itself every time the server starts, because sites change often.
- Optional: `NDM_COOKIES` points to a cookies.txt file for videos that need a signed-in account.

Only download what you have the right to save.

## Test locally

```bash
cd server && pip install -r requirements.txt && python3 -m unittest -q test_app
NDM_KEY=test flask --app app run -p 8080   # then: http://localhost:8080/download?key=test&url=<video page>
```

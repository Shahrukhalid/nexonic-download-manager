# Local automation API

*Windows and Linux.* Lets scripts and other programs on the same computer add and control downloads. It is **off by
default**: turn it on in **Options → Browsers & Command Line → Automation**, where you also find the port and the key.

- Listens only on `127.0.0.1` (other computers can't connect).
- Every request needs the key: `Authorization: Bearer <key>`. **New Key** replaces it; the old one stops working.
- Requests from web pages are refused (any `Origin` header, or a `Host` other than `127.0.0.1:<port>` / `localhost:<port>`).
- Answers never include cookies, passwords or the key. The key is saved encrypted and is not put in backups.
- Bodies are JSON, at most 64 KB and 200 links per request.

For simple jobs the `ndm` command is often easier (`ndm help`).

## Endpoints

| Method and address | What it does |
|---|---|
| `GET /v1/status` | Counts of running, queued and all downloads |
| `GET /v1/downloads` | Every download: id, url, fileName, folder, status, size, received, progress (%), queue, added, finished, savedPath, error |
| `GET /v1/downloads/{id}` | One download |
| `POST /v1/downloads` | Adds links: `{"url": "…"}` or `{"urls": ["…", "…"]}`, optional `fileName` (one link only), `folder` (full path), `queue` (name), `start` (default true). Answers `201 {"ids": [...]}` |
| `POST /v1/downloads/{id}/pause` | Stops a download (it resumes later where it stopped) |
| `POST /v1/downloads/{id}/resume` | Starts it again |
| `DELETE /v1/downloads/{id}` | Removes it from the list (a finished file stays on disk) |
| `GET /v1/queues` | Queues: id, name, running, maxActive |
| `POST /v1/queues/{id}/start` · `/stop` | Starts or stops a queue |

Errors answer with a status code and `{"error": "…"}`: 400 (bad input), 401 (missing or wrong key), 403 (refused
origin or host), 404, 405, 413.

## Examples

```bash
KEY=paste-the-key-here
curl -s http://127.0.0.1:47812/v1/status -H "Authorization: Bearer $KEY"
curl -s -X POST http://127.0.0.1:47812/v1/downloads -H "Authorization: Bearer $KEY" \
     -H "Content-Type: application/json" -d '{"url":"https://example.com/file.zip","queue":"Night","start":false}'
curl -s http://127.0.0.1:47812/v1/downloads -H "Authorization: Bearer $KEY"
```

PowerShell:

```powershell
$h = @{ Authorization = "Bearer $env:NDM_KEY" }
Invoke-RestMethod http://127.0.0.1:47812/v1/downloads -Method Post -Headers $h -ContentType application/json `
  -Body '{"urls":["https://example.com/a.zip","https://example.com/b.zip"]}'
```

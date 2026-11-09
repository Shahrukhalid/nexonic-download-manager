# Server, NAS and Docker

`nexonic-server` is Nexonic Download Manager without a window, for a NAS box, a home server or any always-on Linux
machine. You manage it from your phone or a browser on the same network (remote access), and scripts can use the
[local API](API.md). It uses the same download engine, queues and settings files as the desktop app.

## Docker

```sh
docker build -f docker/Dockerfile -t nexonic-server .
docker run -d --name nexonic -p 47813:47813 \
  -e NDM_HOST=192.168.1.10 \
  -v nexonic-data:/data -v /path/to/downloads:/downloads nexonic-server
docker logs nexonic          # shows the pairing code and address
```

Or with Compose: edit `NDM_HOST` in `docker/compose.yaml`, then `docker compose -f docker/compose.yaml up -d`.

- `/data` keeps settings, the downloads list, queues and the key that encrypts secrets. Keep it on a volume.
- `/downloads` is where files are saved. The container runs as the user `app` (UID 1654): give it write access to the
  folder (`chown 1654 /path/to/downloads`), or run it as your own user with `--user "$(id -u):$(id -g)"`.
- `NDM_HOST` is this machine's address on your network. Inside Docker the server can't see it, so set it to get a
  pairing code phones can use.

The image includes the video engine and ffmpeg (Debian's package). Torrents need the native torrent engine, which
the image does not include yet; use the desktop app for torrents.

## Without Docker

Install the .NET 8 runtime, then:

```sh
dotnet publish linux/Server/Server.csproj -c Release -o ~/nexonic-server
~/nexonic-server/nexonic-server --downloads ~/Downloads
```

| Option | Environment | Default |
|---|---|---|
| `--downloads FOLDER` | `NDM_DOWNLOADS` | the folder in settings |
| `--port PORT` | `NDM_PORT` | 47813 |
| `--new-key` | | unpairs every device |
| | `NDM_HOST` | first local address |
| | `XDG_DATA_HOME` | `~/.local/share` |

Stop it with Ctrl+C or `SIGTERM` (`docker stop`); unfinished downloads continue when it starts again.

## Pairing and security

1. Start the server; it prints a QR code and an address like `http://192.168.1.10:47813/#k=…`.
2. Scan the code with the phone's camera (or open the address). The page remembers the pairing; add it to the home
   screen to open it like an app.

- Every API call needs the pairing key. The key is in the address's `#fragment`, which browsers never send to the
  server or keep in logs; the page sends it as `Authorization: Bearer`.
- After 10 wrong keys from one address, that address waits a minute.
- The connection inside your network is plain HTTP. Use it on your home or office network only, never expose the port
  to the internet (no port forwarding). For access from outside, use a VPN to your home network.
- `--new-key` (or Unpair All Devices in the desktop app) makes a new key: every paired device has to scan again.
- Secrets (the key, download passwords, cookies) are saved encrypted with a key in `/data/Nexonic Download Manager/server.key`,
  readable only by the server's user.

## API

The phone page uses the same endpoints as the local API ([API.md](API.md)) at `http://HOST:47813/v1/…` with the
pairing key. Example:

```sh
curl -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/file.zip"}' http://192.168.1.10:47813/v1/downloads
```

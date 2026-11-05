# Torrent downloads

Since **v0.10.0**, Windows and Linux use a bundled libtorrent 2.0.11 engine, and Mac since **v0.11.0**; the earlier optional Python bridge of v0.9.0 is no longer needed. Android and iPhone/iPad integration is still in development.

## Mac

Double-click a `.torrent` file, click a magnet link in your browser, paste one into **Add URL**, or click **Torrents** in the toolbar. The Add Torrent window previews the files before anything downloads. Select a torrent in the list for its details, like a BitTorrent client: **General** (pieces, downloaded/uploaded, speeds, seeds and peers connected and in the swarm, ratio, availability, connections), **Trackers** (status, seeds, peers, Update Tracker), **Peers** (IP, client, flags, progress, speeds) and **Files**. Right-click → **Torrent Settings…** changes download/upload limits, connections per torrent, upload slots, download in order, seeding, trackers and file priorities; a running torrent takes the changes right away. **Settings → Torrents** has router port forwarding (UPnP and NAT-PMP, off by default) and encryption (prefer, require or off).

## Using the Windows/Linux Torrent Manager

Choose **Torrents** in the toolbar, then **Add Torrent**. Enable torrents explicitly (existing settings remain off), select a local `.torrent`, paste an HTTP(S) torrent link or paste a magnet. Load metadata, review the name, hash and individual files, choose Skip/Low/Normal/High priorities, a destination and a queue, then Start or Download Later. Metadata previews cannot download payloads or become completed content. Ordinary HTTP entry points detect `.torrent` links and resolve their metadata before downloading their contents; they never report a downloaded descriptor as completed torrent contents.

The manager displays progress, peers, seeds, download/upload rates, ETA and ratio. Pause, resume, file priorities, per-torrent download/upload caps, DHT, tracker edits, seeding ratio/time limits and history use the native engine. **Files and Settings** pauses the task before applying changes; resume when ready. Private torrents disable DHT/PEX regardless of the DHT checkbox. Seeding is off by default; enabling it shares verified pieces until the chosen ratio or time limit (0 means no limit). Stop/Pause All and queue schedules also stop seeding. Completed seeders do not occupy download slots. Completion notification occurs once after payload verification, even if seeding continues.

Peers and trackers can see your IP address. System/custom proxies, the shared global rate limiter and DNS over HTTPS remain unsupported by this frontend: choose **No proxy** and disable those settings first. Unsupported settings pause running torrents rather than silently bypassing them. Per-torrent caps work. No website cookies, credentials or host authentication are copied into torrent sessions. Web seeds, local discovery and private/local/multicast peer/tracker addresses are disabled in production. Router port forwarding (UPnP and NAT-PMP) is off by default; on Mac it can be turned on in **Settings → Torrents**, elsewhere manage it outside the app.

## Data, recovery and integrity

Each task writes to `Save to/Nexonic torrents/<task-id>/`. Metadata and destination paths are validated before libtorrent receives them, including v1/v2/hybrid file trees, traversal, duplicate paths, symbolic links, existing hard links, metadata size and file counts. The engine checks pieces during transfer, forces another piece check before completion, then reports SHA-256 for every selected real file. Skipped files can still require shared boundary pieces.

Native manifests and raw resume checkpoints live under application data in `Parts/<task-id>/native/`, use atomic private writes and flush on graceful pause/shutdown. Earlier Linux Base64 checkpoints are imported once and checked against current metadata and paths. Corrupt, mismatched or missing checkpoints trigger piece checking; they cannot override the current destination, file selection or safe flags. A quick resume waits for the previous worker to finish saving. Abrupt termination recovers from periodic checkpoints and verifies remaining pieces.

Removing a task retains payload files, including partial files. Multi-file torrent folder cleanup is manual. Torrent backups remain omitted until portable paths and tracker-secret redaction are implemented. Local metadata/magnets/history can contain private tracker passkeys; do not share them publicly.

## Build and validation

`scripts/build-torrent-native.py` builds pinned libtorrent and its native dependencies; Windows/Linux packaging includes the engine and component notices. No Python interpreter is needed by those application torrent providers. The previous Python bridge remains as a compatibility test fixture, not an embedded application runtime.

`native/tests/engine_test.cpp` transfers generated legal files between real loopback peers, tests metadata-only safeguards, magnets, selection, pause/restart checkpoints, tracker changes, piece checks and SHA-256. `native/tests/dotnet` repeats real transfers through the managed C ABI, UTF-8 paths, concurrent polling and disposal, then tests the actual queue provider resolving an HTTP descriptor into verified payload contents. Native CI covers Windows, macOS, Linux x64 and ARM64; app CI exercises bundled Windows/Linux providers. Device/emulator frontend testing is still required for mobile integration. Tests opt into loopback peers explicitly; production sessions do not.

The engine is [libtorrent-rasterbar](https://www.libtorrent.org/), whose [BSD license and embedded notices](https://github.com/arvidn/libtorrent/blob/v2.0.11/COPYING) are included alongside its dependencies' notices. Existing application licenses and Android GPL obligations remain unchanged.

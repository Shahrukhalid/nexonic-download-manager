# Optional desktop torrent engine

Milestone 7 provides the engine and queue integration. The first Milestone 8 increment adds native resume
checkpoints and selectable file priorities; the rest of the full torrent/media manager remains separate.

The first implementation is Linux/Avalonia. Windows and Linux share the provider catalog and the existing
`IDownloader` task interface. The catalog routes ordinary files, HLS, supported video sites and torrents into
their working engines; capabilities identify piece verification, peers and shared rate-limit support.
Detection makes no network requests. The Add Download dialog offers automatic or manual file/HLS/video/torrent
selection. Other protocols remain unsupported. Existing browser, CLI and automation URL validators remain HTTP(S)-only.

## Setup and use

On Ubuntu/Debian install the optional system package `python3-libtorrent` (libtorrent 2.x).
The app does not download, execute or bundle a torrent runtime automatically. Python is fixed at `/usr/bin/python3`,
isolated with `-I`; the bridge is embedded in the trusted app assembly. Other distributions need compatible system
Python bindings; AppImage users also need this optional host dependency. Ordinary downloads need no Python runtime.

Choose **No proxy**, turn off the shared global speed limit and DNS over HTTPS, then choose **Torrent** in the
Linux toolbar. Enable the experimental feature explicitly (off by default), select a local `.torrent` file or paste
one magnet, load metadata, review the name/size/files, choose Skip/Low/Normal/High for each file, choose a folder and queue, then Start or Download Later.
At least one file must be selected. File choices are virtualized for large torrents. Right-click a task and choose
**Torrent Files…** to edit an existing selection; Save and Pause retains existing files and waits for Resume.
Progress and the task size refer to selected files. Skipped files can still need shared boundary pieces.
An HTTP URL ending in `.torrent` must first be downloaded as a file with the File engine and then opened locally.
Metadata lookup of a local file is offline; a magnet lookup contacts peers/trackers and can take 90 seconds.
Torrents share active queue slots and support Stop/Resume, schedules, history and completion/failure notifications.
The existing progress window shows peers, seeds and upload rate. Per-torrent download caps apply in the native
engine; editing a running torrent's cap restarts its worker and rechecks its pieces.

Peers and trackers can see the user's IP address. The initial engine deliberately refuses system/custom proxy,
shared speed-limit and DNS-over-HTTPS settings rather than silently bypassing them. Changing to an unsupported
setting pauses running torrents. There is no inheritance of website cookies, HTTP authentication or host profiles.
Native upload is capped at 32 KiB/s during downloading and stops when complete; this is not a full seeding manager.

## Data and safety

Only v1 torrents are supported initially. V2/hybrid trees need a dedicated validator. Raw metadata is checked
before native path sanitization: traversal, absolute/separator/control-character components, symlinks, duplicate
paths and excessive file counts/metadata are rejected. Existing destination symlinks/hard-linked files are rejected.
Web seeds, UPnP, NAT-PMP and local discovery are disabled; private/local/multicast peer and tracker IP ranges are
blocked. Private torrents disable DHT/PEX/local discovery. Magnet-supplied direct peers and web seeds are discarded.
This is an experimental provider, not an OS sandbox; keep the host native library updated.

Each task writes inside `Save to/Nexonic torrents/<task-id>/`; there is no cross-task overwrite.
Native resume checkpoints are saved every five seconds and on graceful Stop/completion under the app's
`Parts/<task-id>/torrent.resume`, using bounded atomic writes and owner-only directory/file permissions.
Checkpoint paths, renamed files, trackers, peers, web seeds and flags never override validated metadata or current
settings. Missing, corrupt or mismatched checkpoints fall back to native piece checks. Keep data in place between
runs; copied/changed files may trigger a recheck. Normal metadata/history and priorities persist across app restarts.
Stop closes the worker's input, drains its final checkpoint and allows up to eight seconds before forced termination.
Workers for the same record are serialized so a quick Resume cannot race the old worker's writes. Parent stdin EOF
also stops native work; abrupt parent exit retains the last periodic checkpoint rather than guaranteeing a final one. Removing or
cancelling a torrent leaves its files, including partial files, on disk. The existing explicit Trash command can
trash a finished single file. Multi-file torrent cleanup is manual in this milestone.

Export/import backups omit torrents until portable resume
and tracker-secret redaction are implemented; backup files cannot introduce the new protocol or enable it.
Private tracker URLs/passkeys can be present in the local magnet and metadata, just as in the original torrent file.
Do not share that history or torrent file publicly. No browser credentials are copied into it.

## Verification and limits

`tests/torrent_bridge_test.py` requires real libtorrent 2.x and transfers generated data over loopback only,
with DHT/discovery disabled. It tests local and magnet metadata, partial transfer, forced worker termination,
resume, final SHA-256 integrity, raw unsafe paths and destination symlinks. It also exercises selected-file
completion, persisted piece maps/priorities, restart from a checkpoint, malicious checkpoint path overrides and
corrupt-checkpoint fallback. Linux x64/ARM64 CI runs it with the
system runtime. .NET tests check detection, legacy engine selection, opt-in/network policies, history progress,
backup boundaries and the embedded runtime's metadata/verified-piece completion. Existing Linux smoke and
headless list tests continue to exercise downloads and the second-download regression.

Remaining Milestone 8 work includes full seeding/ratio/tracker management, torrent creation, portable torrent
backups, playlists/subtitles/audio conversion, galleries, the media library and expanded torrent UI. No torrent integration is activated in
Windows UI, macOS, Android or iOS by this milestone.

## Dependency

The optional native engine is [libtorrent-rasterbar](https://libtorrent.org/python_binding.html), installed and
maintained separately. Upstream's [COPYING](https://github.com/arvidn/libtorrent/blob/v2.0.11/COPYING) records its
BSD 3-clause terms. No native libtorrent binary or source is redistributed by this change. Existing bundled
component licenses and Android GPL obligations remain unchanged.

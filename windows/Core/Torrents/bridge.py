"""Private JSON-lines bridge to optional, system-installed libtorrent 2.x.
No shell, web seeds, browser credentials or user-provided executable paths.
"""
import base64
import ipaddress
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time

MAX_META = 2 * 1024 * 1024
MAX_LINE = 8 * 1024 * 1024
stop = threading.Event()


def emit(kind, **data):
    print(json.dumps(dict(event=kind, **data), ensure_ascii=True), flush=True)


def component(value):
    if not isinstance(value, bytes):
        raise ValueError('Invalid torrent path')
    value = value.decode('utf-8', errors='strict')
    if not value or value in ('.', '..') or any(c in value for c in '/\\:\x00') or any(ord(c) < 32 for c in value):
        raise ValueError('Unsafe torrent path')
    return value


def validate_info(info):
    """Inspect raw names before libtorrent can sanitize traversal or symlink entries."""
    name = component(info.get(b'name.utf-8', info.get(b'name')))
    files = info.get(b'files')
    # Initial implementation accepts v1 only; BEP 52 trees need separate path validation.
    if b'pieces' not in info or b'file tree' in info:
        raise ValueError('Use a v1 torrent; v2 and hybrid torrents are not supported yet')
    entries = files if files is not None else [info]
    if not isinstance(entries, list) or not 1 <= len(entries) <= 10000:
        raise ValueError('Invalid file count (maximum 10000)')
    result = []
    seen = set()
    for entry in entries:
        if b'l' in entry.get(b'attr', b'') or b'symlink path' in entry:
            raise ValueError('Torrent symlinks are not supported')
        parts = [component(p) for p in entry.get(b'path.utf-8', entry.get(b'path', []))] if files is not None else []
        if files is not None and not parts:
            raise ValueError('Empty torrent file path')
        path = '/'.join([name] + parts)
        if path.casefold() in seen or len(path) > 1024:
            raise ValueError('Duplicate or excessive torrent path')
        seen.add(path.casefold())
        size = entry.get(b'length')
        if not isinstance(size, int) or size < 0 or size > (1 << 50):
            raise ValueError('Invalid torrent file size')
        result.append(dict(path=path, size=size))
    return name, result


def safe_destination(folder, files):
    root = Path(folder)
    if not root.is_absolute():
        raise ValueError('Choose an absolute destination folder')
    for path in [root, *root.parents]:
        if path.is_symlink():
            raise ValueError('Torrent destination must not contain symbolic links')
    for item in files:
        p = root / item['path']
        for part in [p, *p.parents]:
            if part.is_symlink():
                raise ValueError('Torrent destination contains a symbolic link')
        if p.exists() and (not p.is_file() or p.stat().st_nlink > 1):
            raise ValueError('A torrent file conflicts with a directory or hard-linked file')
    root.mkdir(parents=True, exist_ok=True)


def read_commands():
    # EOF (including parent process exit) stops native workers as well as an explicit pause.
    for _ in sys.stdin:
        stop.set()
        return
    stop.set()


def file_priorities(request, count):
    priorities = request.get('priorities')
    if priorities is None:
        return [4] * count
    if not isinstance(priorities, list) or len(priorities) != count or any(type(p) is not int or p not in (0, 1, 4, 7) for p in priorities):
        raise ValueError('Choose Skip, Low, Normal or High for each torrent file')
    if not any(priorities):
        raise ValueError('Select at least one torrent file')
    return priorities


def restore_resume(lt, request, fresh):
    encoded = request.get('resume')
    if not encoded:
        return fresh
    try:
        data = base64.b64decode(encoded, validate=True)
        if len(data) > MAX_META:
            raise ValueError('Checkpoint exceeds the size limit')
        restored = lt.read_resume_data(data)
        if restored.info_hash != fresh.ti.info_hash():
            raise ValueError('Checkpoint belongs to another torrent')
        # Resume data may supply storage paths, renamed files, peers, trackers and unsafe flags.
        # Only validated metadata/current settings define these; never trust a checkpoint's routing.
        restored.ti = fresh.ti
        restored.flags = fresh.flags
        restored.renamed_files = {}
        restored.trackers = fresh.trackers
        restored.tracker_tiers = fresh.tracker_tiers
        restored.url = ''
        restored.piece_priorities = []
        restored.banned_peers = []
        restored.dht_nodes = []
        restored.storage_mode = lt.storage_mode_t.storage_mode_sparse
        emit('resume', used=True)
        return restored
    except Exception:
        emit('resume', used=False)  # Native engine rechecks existing pieces when a checkpoint is unusable.
        return fresh


def checkpoint(lt, session, handle):
    handle.save_resume_data(lt.save_resume_flags_t.flush_disk_cache)
    until = time.monotonic() + 5
    while time.monotonic() < until:
        for alert in session.pop_alerts():
            if isinstance(alert, lt.save_resume_data_alert):
                data = bytes(lt.write_resume_data_buf(alert.params))
                if len(data) <= MAX_META:
                    emit('checkpoint', resume=base64.b64encode(data).decode('ascii'))
                return
            if isinstance(alert, lt.save_resume_data_failed_alert):
                return
        session.wait_for_alert(100)


def main():
    try:
        import libtorrent as lt
    except ImportError:
        raise RuntimeError('Torrent engine unavailable. On Ubuntu/Debian install python3-libtorrent for /usr/bin/python3.')
    if not lt.version.startswith('2.'):
        raise RuntimeError('Torrent engine requires libtorrent 2.x')
    line = sys.stdin.readline(MAX_LINE + 1)
    if len(line) > MAX_LINE:
        raise ValueError('Torrent request is too large')
    req = json.loads(line)
    threading.Thread(target=read_commands, daemon=True).start()
    action = req['action']
    test = '--test' in sys.argv  # C# never enables this; generated-data tests alone use loopback peers.
    settings = dict(listen_interfaces='127.0.0.1:0' if test else '0.0.0.0:0,[::]:0',
                    enable_dht=not test, enable_lsd=False, enable_upnp=False, enable_natpmp=False,
                    allow_multiple_connections_per_ip=test, announce_to_all_trackers=False,
                    apply_ip_filter_to_trackers=True, metadata_token_limit=1000000,
                    max_metadata_size=MAX_META)
    params = lt.add_torrent_params()
    source = req.get('source', '')
    raw = None
    if req.get('metainfo'):
        raw = base64.b64decode(req['metainfo'], validate=True)
    elif source.lower().startswith('magnet:?'):
        if len(source) > 16384:
            raise ValueError('Magnet link is too long')
        params = lt.parse_magnet_uri(source)
    else:
        p = Path(source)
        if not p.is_absolute() or not p.name.lower().endswith('.torrent'):
            raise ValueError('Choose a local .torrent file or paste a magnet link')
        with p.open('rb') as f:
            raw = f.read(MAX_META + 1)
    if raw is not None:
        if len(raw) > MAX_META:
            raise ValueError('Torrent metadata exceeds 2 MiB')
        validate_info(lt.bdecode(raw)[b'info'])
        params.ti = lt.torrent_info(raw)
        params.ti.set_web_seeds([])
        if action == 'metadata':
            info = lt.bdecode(raw)[b'info']
            name, files = validate_info(info)
            # Local previews need no native session, listening sockets or tracker/DHT contact.
            emit('metadata', name=name, size=sum(f['size'] for f in files), files=files,
                 info_hash=str(params.ti.info_hash()), metainfo=base64.b64encode(raw).decode('ascii'))
            return
    if action == 'download':
        if params.ti is None:
            raise ValueError('Preview torrent metadata before downloading')
        params = restore_resume(lt, req, params)
        params.file_priorities = file_priorities(req, params.ti.num_files())
    # Magnet web seeds, explicit peers and external metadata URLs are never inherited.
    params.url_seeds = []
    params.http_seeds = []
    params.peers = []
    # Ubuntu 22.04 bindings omit the newer optional flag. upload_mode is the required no-payload guard.
    dont_download = getattr(lt.torrent_flags, 'default_dont_download', 0)
    params.flags |= lt.torrent_flags.upload_mode | dont_download
    params.flags &= ~lt.torrent_flags.auto_managed
    params.flags &= ~lt.torrent_flags.paused
    if params.ti is not None and params.ti.priv():
        params.flags |= lt.torrent_flags.disable_dht | lt.torrent_flags.disable_pex | lt.torrent_flags.disable_lsd
    if action == 'download':
        if raw is None:
            raise ValueError('Preview torrent metadata before downloading')
        _, checked_files = validate_info(lt.bdecode(raw)[b'info'])
        safe_destination(req['folder'], checked_files)
    if params.ti is not None and params.ti.priv():
        settings['enable_dht'] = False
    ses = lt.session(settings)
    if not test:
        filt = lt.ip_filter()
        for network in ['0.0.0.0/8', '10.0.0.0/8', '100.64.0.0/10', '127.0.0.0/8', '169.254.0.0/16',
                        '172.16.0.0/12', '192.168.0.0/16', '224.0.0.0/4', '240.0.0.0/4', '::/128',
                        '::1/128', '::ffff:0:0/96', 'fc00::/7', 'fe80::/10', 'ff00::/8']:
            n = ipaddress.ip_network(network)
            filt.add_rule(str(n.network_address), str(n.broadcast_address), 1)
        ses.set_ip_filter(filt)
    with tempfile.TemporaryDirectory(prefix='nexonic-metadata-') as temp:
        params.save_path = temp if action == 'metadata' else req['folder']
        handle = ses.add_torrent(params)
        peer = req.get('test_peer') if test else None
        deadline = time.monotonic() + 90
        while not handle.status().has_metadata:
            if stop.wait(.1):
                return
            if time.monotonic() > deadline:
                raise TimeoutError('No torrent metadata received within 90 seconds')
            if peer:
                handle.connect_peer(('127.0.0.1', int(peer)))
        ti = handle.torrent_file()
        info = lt.bdecode(ti.metadata())
        name, files = validate_info(info)
        ti.set_web_seeds([])
        handle.set_flags(lt.torrent_flags.disable_dht | lt.torrent_flags.disable_pex | lt.torrent_flags.disable_lsd) if ti.priv() else None
        trackers = [t.url for t in handle.trackers()]
        data = {b'info': info}
        if trackers:
            data[b'announce-list'] = [[t.encode('utf-8')] for t in trackers]
        cached = lt.bencode(data)
        if len(cached) > MAX_META:
            raise ValueError('Torrent metadata exceeds 2 MiB')
        emit('metadata', name=name, size=sum(f['size'] for f in files), files=files,
             info_hash=str(ti.info_hash()), metainfo=base64.b64encode(cached).decode('ascii'))
        if action == 'metadata':
            ses.remove_torrent(handle)
            return
        if action != 'download':
            raise ValueError('Unknown torrent action')
        safe_destination(req['folder'], files)
        handle.set_download_limit(max(0, min(int(req.get('limit', 0)), 2**31 - 1)))
        handle.set_upload_limit(32 * 1024)
        priorities = file_priorities(req, ti.num_files())
        selected_size = sum(f['size'] for f, priority in zip(files, priorities) if priority > 0)
        handle.prioritize_files(priorities)
        handle.unset_flags(lt.torrent_flags.upload_mode | dont_download)
        last_checkpoint = time.monotonic()
        completed = False
        while not stop.is_set():
            if peer:
                handle.connect_peer(('127.0.0.1', int(peer)))
            s = handle.status()
            if s.errc.value():
                raise RuntimeError(s.errc.message())
            emit('progress', received=s.total_wanted_done, peers=s.num_peers, seeds=s.num_seeds,
                 download_rate=s.download_payload_rate, upload_rate=s.upload_payload_rate)
            if s.is_finished and s.total_wanted == selected_size:
                completed = True
                break
            if time.monotonic() - last_checkpoint >= 5:
                checkpoint(lt, ses, handle)
                last_checkpoint = time.monotonic()
            stop.wait(.25)
        handle.pause()
        checkpoint(lt, ses, handle)
        ses.remove_torrent(handle)
        if not completed:
            return
    del handle
    del ses
    emit('finished')


if __name__ == '__main__':
    try:
        main()
    except Exception as e:
        emit('error', message=str(e))
        sys.exit(1)

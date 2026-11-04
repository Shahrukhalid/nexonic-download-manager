#!/usr/bin/env python3
"""Real libtorrent loopback tests using generated data only; no DHT, trackers or internet peers."""
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading
import time
import unittest

import libtorrent as lt  # Required: failing import must not silently skip native integration.

BRIDGE = Path(__file__).resolve().parents[1] / 'windows/Core/Torrents/bridge.py'
spec = importlib.util.spec_from_file_location('bridge', BRIDGE)
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


class Worker:
    def __init__(self, request):
        self.p = subprocess.Popen([sys.executable, '-I', '-u', str(BRIDGE), '--test'],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.p.stdin.write(json.dumps(request) + '\n'); self.p.stdin.flush()
        self.events = queue.Queue()
        def read():
            for line in self.p.stdout:
                try: self.events.put(json.loads(line))
                except Exception as e: self.events.put(dict(event='error', message=str(e)))
            self.events.put(dict(event='eof'))
        threading.Thread(target=read, daemon=True).start()

    def event(self, kind, predicate=lambda e: True, timeout=90):
        until = time.monotonic() + timeout
        while time.monotonic() < until:
            event = self.events.get(timeout=max(.01, until - time.monotonic()))
            if event['event'] == 'error': raise AssertionError(event['message'])
            if event['event'] == 'eof': raise AssertionError('Worker exited: ' + self.p.stderr.read())
            if event['event'] == kind and predicate(event): return event
        raise AssertionError('Timed out waiting for ' + kind)

    def close(self, force=False):
        if self.p.poll() is None:
            if force: self.p.kill()
            self.p.stdin.close()  # Parent EOF must terminate the session, preserving partial data.
            try: self.p.wait(timeout=10)
            except subprocess.TimeoutExpired: self.p.kill(); self.p.wait(); raise
        if not self.p.stdin.closed: self.p.stdin.close()
        self.p.stdout.close(); self.p.stderr.close()


class TorrentBridgeTests(unittest.TestCase):
    def test_paths_and_symlinks_rejected(self):
        base = {b'name': b'safe', b'piece length': 16384, b'pieces': b'0' * 20}
        for part in [b'..', b'/tmp/escape', b'a\\b', b'c:bad', b'a\x00b']:
            with self.subTest(part=part), self.assertRaises(ValueError):
                bridge.validate_info({**base, b'files': [{b'path': [part], b'length': 1}]})
        with self.assertRaises(ValueError):
            bridge.validate_info({**base, b'files': [{b'path': [b'link'], b'length': 1, b'attr': b'l'}]})
        self.assertEqual(bridge.validate_info({**base, b'files': [{b'path': [b'valid.bin'], b'length': 1}]})[1][0]['path'], 'safe/valid.bin')
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp).resolve(); (root/'link').symlink_to(root, target_is_directory=True)
            with self.assertRaises(ValueError):
                bridge.safe_destination(str(root), [dict(path='link/escape', size=1)])

    def test_native_worker_rejects_traversal_before_destination_creation(self):
        with tempfile.TemporaryDirectory() as tmp:
            dest = Path(tmp).resolve()/'untouched'
            info = {b'name': b'..', b'length': 1, b'piece length': 16384, b'pieces': b'0' * 20}
            encoded = base64.b64encode(lt.bencode({b'info': info})).decode('ascii')
            worker = Worker(dict(action='download', metainfo=encoded, folder=str(dest)))
            try:
                with self.assertRaisesRegex(AssertionError, 'Unsafe torrent path'):
                    worker.event('finished')
                self.assertFalse(dest.exists())
            finally: worker.close()

    def test_real_torrent_magnet_pause_resume_and_integrity(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp).resolve(); seed = root/'seed'; seed.mkdir(); dest = root/'download'
            payload = seed/'fixture.bin'
            # Deterministic generated payload, no external or copyrighted content.
            with payload.open('wb') as f:
                for i in range(192):
                    f.write(hashlib.sha256(str(i).encode()).digest() * 2048)
            expected = hashlib.sha256(payload.read_bytes()).hexdigest()
            fs = lt.file_storage(); fs.add_file('fixture.bin', payload.stat().st_size)
            creator = lt.create_torrent(fs, 16384, lt.create_torrent.v1_only)
            lt.set_piece_hashes(creator, str(seed)); raw = lt.bencode(creator.generate()); ti = lt.torrent_info(raw)
            ses = lt.session(dict(listen_interfaces='127.0.0.1:0', enable_dht=False, enable_lsd=False,
                                  enable_upnp=False, enable_natpmp=False, allow_multiple_connections_per_ip=True))
            params = lt.add_torrent_params(); params.ti = ti; params.save_path = str(seed)
            params.flags &= ~lt.torrent_flags.paused; params.flags &= ~lt.torrent_flags.auto_managed
            handle = ses.add_torrent(params)
            deadline = time.monotonic() + 20
            while not handle.status().is_seeding:
                if time.monotonic() > deadline: self.fail('Seed did not verify fixture')
                time.sleep(.05)
            port = ses.listen_port(); magnet = lt.make_magnet_uri(ti)
            torrent = root/'fixture.torrent'; torrent.write_bytes(raw)
            for source in [str(torrent), magnet]:
                worker = Worker(dict(action='metadata', source=source, test_peer=port))
                try:
                    meta = worker.event('metadata')
                    self.assertEqual(meta['name'], 'fixture.bin'); self.assertEqual(meta['size'], payload.stat().st_size)
                    self.assertFalse(dest.exists(), 'Preview must not create destination files')
                    worker.p.wait(timeout=10); self.assertEqual(worker.p.returncode, 0)
                finally: worker.close()
            self.assertEqual(handle.status().total_payload_upload, 0, 'Metadata preview must not fetch file pieces')
            req = dict(action='download', metainfo=meta['metainfo'], folder=str(dest), test_peer=port, limit=512*1024)
            first = Worker(req)
            try:
                progress = first.event('progress', lambda e: 0 < e['received'] < payload.stat().st_size)
                self.assertGreater(progress['received'], 0)
            finally: first.close(force=True)
            self.assertTrue((dest/'fixture.bin').exists())
            second = Worker(dict(req, limit=0))
            try:
                second.event('finished'); second.p.wait(timeout=10); self.assertEqual(second.p.returncode, 0)
            finally: second.close()
            self.assertEqual(hashlib.sha256((dest/'fixture.bin').read_bytes()).hexdigest(), expected)
            print('Verified torrent + magnet metadata, partial stop, resume and SHA-256:', expected)
            ses.remove_torrent(handle)


if __name__ == '__main__': unittest.main()

#!/usr/bin/env python3
"""Prepare the pinned native engine as a local Swift Package XCFramework (never downloaded binaries)."""
import argparse
import importlib.util
import os
from pathlib import Path
import platform
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def run(*args):
    subprocess.run([str(x) for x in args], cwd=ROOT, check=True)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ios', action='store_true')
    parser.add_argument('--universal-mac', action='store_true')
    args = parser.parse_args()
    if 'DEVELOPER_DIR' not in os.environ and Path('/Applications/Xcode.app/Contents/Developer').is_dir():
        os.environ['DEVELOPER_DIR'] = '/Applications/Xcode.app/Contents/Developer'
    spec = importlib.util.spec_from_file_location('native_builder', ROOT/'scripts/build-torrent-native.py')
    builder = importlib.util.module_from_spec(spec); spec.loader.exec_module(builder)
    work = ROOT/'.native-cache/apple'; work.mkdir(parents=True, exist_ok=True)
    headers = work/'include'; headers.mkdir(exist_ok=True)
    shutil.copyfile(ROOT/'native/include/nexonic_torrent.h', headers/'nexonic_torrent.h')
    (headers/'module.modulemap').write_text('module CNexonicTorrent { header "nexonic_torrent.h" export * }\n')
    command = ['xcodebuild', '-create-xcframework']
    targets = []
    if args.ios:
        sim = 'ios-sim-arm64' if platform.machine() == 'arm64' else 'ios-sim-x64'
        for target in ['ios-arm64', sim]:
            sdk = builder.build(target, False); targets.append(target)
            command += ['-library', sdk/'lib/libCNexonicTorrent.a', '-headers', headers]
    else:
        architectures = ['mac-arm64', 'mac-x64'] if args.universal_mac else ['mac-arm64' if platform.machine() == 'arm64' else 'mac-x64']
        archives = []
        for target in architectures:
            archives.append(builder.build(target, False)/'lib/libCNexonicTorrent.a'); targets.append(target)
        archive = work/'libCNexonicTorrent.a'
        if len(archives) > 1: run('xcrun', 'lipo', '-create', *archives, '-output', archive)
        else: shutil.copyfile(archives[0], archive)
        command += ['-library', archive, '-headers', headers]
    output = ROOT/'mac/Native/CNexonicTorrent.xcframework'
    if output.exists(): shutil.rmtree(output)
    output.parent.mkdir(exist_ok=True)
    run(*command, '-output', output)
    notices = ROOT/'mac/Native/notices'
    if notices.exists(): shutil.rmtree(notices)
    shutil.copytree(ROOT/'.native-cache/sdk'/targets[0]/'notices', notices)
    print('APPLE TORRENT SDK READY:', output)

if __name__ == '__main__': main()

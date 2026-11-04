#!/usr/bin/env python3
"""Fail before release builds when source versions or a pushed tag disagree."""
import argparse
import json
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag', default=os.environ.get('GITHUB_REF_NAME') if os.environ.get('GITHUB_REF_TYPE') == 'tag' else None)
    args = parser.parse_args()
    version = (ROOT/'VERSION').read_text().strip()
    if not re.fullmatch(r'\d+\.\d+\.\d+', version):
        raise ValueError('VERSION must contain a stable major.minor.patch value')
    versions = {
        'windows/Directory.Build.props': ET.parse(ROOT/'windows/Directory.Build.props').findtext('.//Version'),
        'extension/manifest.json': json.loads((ROOT/'extension/manifest.json').read_text())['version'],
    }
    for path, pattern in [('windows/Host/Program.cs', r'const string Version = "([^"]+)"'),
                          ('mac/Sources/NexonicHost/main.swift', r'let version = "([^"]+)"')]:
        match = re.search(pattern, (ROOT/path).read_text())
        versions[path] = match.group(1) if match else None
    for path, found in versions.items():
        if found != version:
            raise ValueError(f'{path}: {found!r} does not match VERSION {version}')
    if not (ROOT/'CHANGELOG.md').read_text().startswith(f'## {version}\n'):
        raise ValueError('The first changelog entry must match VERSION')
    if args.tag is not None and args.tag != f'v{version}':
        raise ValueError(f'Tag {args.tag!r} does not match VERSION {version}')
    # Mobile builders consume VERSION; keep their shared version source explicit.
    if 'rootProject.file("../VERSION")' not in (ROOT/'android/app/build.gradle.kts').read_text():
        raise ValueError('Android must consume the root VERSION file')
    if 'MARKETING_VERSION: ${NDM_VERSION}' not in (ROOT/'ios/project.yml').read_text():
        raise ValueError('iOS must consume the generated NDM_VERSION')
    print(f'Release sources agree on {version}' + (f' and tag {args.tag}' if args.tag else ''))


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError, ET.ParseError) as error:
        print(f'Release check failed: {error}', file=sys.stderr)
        sys.exit(1)

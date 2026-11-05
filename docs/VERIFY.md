# Check that a download is genuine

Nexonic Download Manager is free and is not sold through Apple or Microsoft, so macOS and Windows show a warning the
first time you open it. You can check yourself that the file you downloaded is the one this repository built.

## 1. Compare the checksum (quick)

Each release has `SHA256SUMS.txt` (all files) and, for Linux, `SHA256SUMS-linux-x64.txt` and
`SHA256SUMS-linux-arm64.txt`.

- **Mac / Linux:** in Terminal, in the folder with the download:
  `shasum -a 256 -c --ignore-missing SHA256SUMS.txt` (Linux: `sha256sum -c --ignore-missing SHA256SUMS.txt`)
- **Windows:** in PowerShell: `Get-FileHash .\NexonicDownloadManager-Windows-Setup.exe` and compare the value with the line
  for that file in `SHA256SUMS.txt`.

"OK" (or the same value) means the file is exactly the released one.

## 2. Check the signature of the checksum list (strong)

`SHA256SUMS.txt` is signed with [Sigstore](https://www.sigstore.dev/) by this repository's release workflow. No key
to download: the signature names the workflow that made it. With [cosign](https://docs.sigstore.dev/cosign/system_config/installation/):

```sh
cosign verify-blob SHA256SUMS.txt \
  --bundle SHA256SUMS.txt.sigstore.json \
  --certificate-identity-regexp '^https://github.com/Shahrukhalid/nexonic-download-manager/' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

"Verified OK" means the list came from this repository's GitHub Actions and was not changed.

## 3. Check the build provenance (strongest)

Every release file has a GitHub build attestation. With the [GitHub CLI](https://cli.github.com/):

```sh
gh attestation verify NexonicDownloadManager-Mac.dmg --repo Shahrukhalid/nexonic-download-manager
```

It shows the workflow, commit and run that produced the file.

## What is in each release

`sbom.spdx.json` lists the components and versions the source contains (a software bill of materials). Third-party
notices are inside each app, and the Android and torrent-engine source archives are attached to every release.

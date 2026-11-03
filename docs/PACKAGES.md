# Package managers

The files for winget, Scoop and Homebrew are made from each published release by
`scripts/package-manifests.py <version>`, with the SHA-256 checksums GitHub lists for the release files.

| Package manager | File in this repository | How people install | Status |
|---|---|---|---|
| **Scoop** (Windows) | `bucket/nexonic-download-manager.json` | `scoop bucket add nexonic https://github.com/Shahrukhalid/nexonic-download-manager` then `scoop install nexonic-download-manager` | works from this repository |
| **winget** (Windows) | `packaging/winget/` | `winget install Shahrukhalid.NexonicDownloadManager` | after the manifests are accepted into [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) |
| **Homebrew** (Mac) | `packaging/homebrew/nexonic-download-manager.rb` | `brew install --cask shahrukhalid/nexonic/nexonic-download-manager` | after the cask is put in a repository named `homebrew-nexonic` |

All three add the `ndm` command. Scoop installs the portable version (updates come through `scoop update`); winget
installs with the regular setup; Homebrew installs the .dmg's app into Applications.

## For a new release

1. Publish the release (its files and `latest.json` must be on GitHub).
2. Run `python3 scripts/package-manifests.py <version>` and commit the changed files in a pull request.
3. winget: submit `packaging/winget/` to microsoft/winget-pkgs (`wingetcreate submit packaging/winget`).
   Homebrew: copy the cask into the tap repository (`Casks/nexonic-download-manager.rb`).

Submitting to winget-pkgs and creating the tap are done by the owner; nothing is submitted automatically.

On Mac the app is not notarized by Apple yet, so macOS asks once before opening it after a Homebrew install, the same as
with the .dmg (System Settings → Privacy & Security → Open Anyway).

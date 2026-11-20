# Package managers

The files for winget, Scoop and Homebrew are made from each published release by
`scripts/package-manifests.py <version>`, with the SHA-256 checksums GitHub lists for the release files.

| Package manager | File in this repository | How people install | Status |
|---|---|---|---|
| **Scoop** (Windows) | `bucket/nexonic-download-manager.json` | `scoop bucket add nexonic https://github.com/nexonic-technologies/nexonic-download-manager` then `scoop install nexonic-download-manager` | works from this repository |
| **winget** (Windows) | `packaging/winget/` | `winget install NexonicTechnologies.NexonicDownloadManager` | after the manifests are accepted into [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) |
| **Homebrew** (Mac) | `Casks/nexonic-download-manager.rb` | `brew tap nexonic-technologies/nexonic https://github.com/nexonic-technologies/nexonic-download-manager` then `brew install --cask nexonic-download-manager` | works from this repository (it is its own tap) |

All three add the `ndm` command. Scoop installs the portable version (updates come through `scoop update`); winget
installs with the regular setup; Homebrew installs the .dmg's app into Applications.

## For a new release

1. Publish the release (its files and `latest.json` must be on GitHub).
2. Run `python3 scripts/package-manifests.py <version>` and commit the changed files in a pull request.
3. winget: after the first version is accepted, the `Winget` workflow opens the pull request to microsoft/winget-pkgs
   by itself when a release is published (it needs the secret `WINGET_TOKEN`, a classic token with the `public_repo`
   scope, and a fork of microsoft/winget-pkgs under the owner's account). The first version is submitted by hand from
   `packaging/winget/`.
   Homebrew and Scoop read their files straight from this repository, so merging that pull request updates them.

Microsoft reviews every winget pull request; Scoop and Homebrew change only when the files here change.

On Mac the app is not notarized by Apple yet, so macOS asks once before opening it after a Homebrew install, the same as
with the .dmg (System Settings → Privacy & Security → Open Anyway).

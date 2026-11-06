# Package managers

The files for winget, Scoop, Homebrew and the AUR are made from each published release by
`scripts/package-manifests.py <version>`, with the SHA-256 checksums GitHub lists for the release files.

| Package manager | File in this repository | How people install | Status |
|---|---|---|---|
| **Scoop** (Windows) | `bucket/nexonic-download-manager.json` | `scoop bucket add nexonic https://github.com/Shahrukhalid/nexonic-download-manager` then `scoop install nexonic-download-manager` | works from this repository |
| **winget** (Windows) | `packaging/winget/` | `winget install Shahrukhalid.NexonicDownloadManager` | after the manifests are accepted into [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) |
| **Homebrew** (Mac) | `packaging/homebrew/nexonic-download-manager.rb` | `brew install --cask shahrukhalid/nexonic/nexonic-download-manager` | after the cask is put in a repository named `homebrew-nexonic` |
| **AUR** (Arch Linux) | `packaging/aur/nexonic-download-manager-bin/` | `yay -S nexonic-download-manager-bin` (or `paru`) | published on the [AUR](https://aur.archlinux.org/packages/nexonic-download-manager-bin) |
| **AUR, latest git** (Arch Linux) | `packaging/aur/nexonic-download-manager-git/` | `yay -S nexonic-download-manager-git` (or `paru`) | published on the [AUR](https://aur.archlinux.org/packages/nexonic-download-manager-git) |

All five add the `ndm` command. Scoop installs the portable version (updates come through `scoop update`); winget
installs with the regular setup; Homebrew installs the .dmg's app into Applications; the AUR `-bin` package
installs the extracted AppImage contents into `/opt` (no FUSE needed at runtime).

One-line installs (no package manager needed):

```bash
# Mac or Linux (installs or updates to the latest release)
curl -fsSL https://shahrukhalid.github.io/nexonic-download-manager/install.sh | bash
```

```powershell
# Windows, in PowerShell (installs or updates to the latest release)
irm https://shahrukhalid.github.io/nexonic-download-manager/install.ps1 | iex
```

## How the AUR always has the latest version

- `nexonic-download-manager-bin` is versioned. The `.github/workflows/aur.yml` workflow runs on every published
  release (and on demand): it runs `scripts/package-manifests.py <version>` to refresh the `PKGBUILD` checksums
  and `.SRCINFO`, then pushes them to `aur.archlinux.org`. So every `yay -Syu` picks up the newest release.
- `nexonic-download-manager-git` builds the latest `main` from source on every install: its `pkgver()` function
  derives the version from `VERSION` plus the git revision (`0.17.0.r<rev>.g<hash>`), so AUR helpers rebuild it
  whenever `main` moves.

## For a new release

1. Publish the release (its files and `latest.json` must be on GitHub).
2. Run `python3 scripts/package-manifests.py <version>` and commit the changed files in a pull request.
3. winget: submit `packaging/winget/` to microsoft/winget-pkgs (`wingetcreate submit packaging/winget`).
   Homebrew: copy the cask into the tap repository (`Casks/nexonic-download-manager.rb`).
4. AUR: nothing to do by hand. The `aur.yml` workflow pushes the updated `-bin` package automatically once the
   release is published (it needs the `AUR_SSH_PRIVATE_KEY` repository secret: an SSH private key whose public
   key is registered on https://aur.archlinux.org/account/). To push by hand:
   `git clone ssh://aur@aur.archlinux.org/nexonic-download-manager-bin.git`,
   copy in `packaging/aur/nexonic-download-manager-bin/PKGBUILD` and `.SRCINFO`, commit and push.

Submitting to winget-pkgs and creating the tap are done by the owner; nothing is submitted automatically.
The AUR push is automatic; winget/Homebrew submissions are manual.

On Mac the app is not notarized by Apple yet, so macOS asks once before opening it after a Homebrew install, the same as
with the .dmg (System Settings → Privacy & Security → Open Anyway).

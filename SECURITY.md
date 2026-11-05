# Security

## Reporting a problem

Please report security problems privately, not in a public issue:

- Use **Report a vulnerability** on the [Security tab](https://github.com/Shahrukhalid/nexonic-download-manager/security)
  of this repository (GitHub's private vulnerability reporting), or
- email **sk@shahrukhalid.com** with "Security" in the subject.

Include the app and version (Help › About), your system, and the steps that show the problem. You will get a reply
within a few days. Please give us a reasonable time to fix it before you publish details.

## Supported versions

Only the latest release gets security fixes. The app checks for updates itself and installs them only when the file
matches the SHA-256 that the release publishes.

## What is in scope

- The apps for Mac, Windows, Linux, Android and iPhone/iPad, the browser extension and the `ndm` command.
- The optional local automation API (off by default, only on this computer, protected by a key).
- The release files and the update check.

Downloads of third-party sites, and the sites themselves, are out of scope. The app never removes copy protection.

## Checking a download

Every release lists checksums for its files, and releases from 0.14.0 on also carry a signed checksum list and
build provenance. See [docs/VERIFY.md](docs/VERIFY.md).

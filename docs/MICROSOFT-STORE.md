# Microsoft Store edition

The Windows app is also packaged for Microsoft Store as an MSIX. The Store signs the package and installs its
updates, so this edition costs nothing to sign and its own updater stays off. Everything else is the same app.

## One-time setup (owner)

1. Register as an individual developer in [Partner Center](https://partner.microsoft.com/dashboard) with your
   Microsoft account (free; a quick ID check).
2. **Apps and games → New product → MSIX or PWA app**, reserve the name **Nexonic Download Manager**.
3. Open the app → **Product management → Product identity** and copy:
   - Package/Identity/Name → repository variable `MSIX_IDENTITY_NAME`
   - Package/Identity/Publisher (`CN=…`) → `MSIX_PUBLISHER`
   - Package/Properties/PublisherDisplayName → `MSIX_PUBLISHER_NAME`

   (GitHub → Settings → Secrets and variables → Actions → **Variables**.)

## Each release

1. Run **Actions → Microsoft Store package** (empty tag = the latest release). It packs the release's verified
   portable files for x64 and Arm64.
2. Download the `microsoft-store-packages` artifact and upload both `.msix` files in Partner Center → the submission →
   **Packages**.
3. Submit. Certification usually takes one to three days.

## Notes for certification

- **runFullTrust** and **unvirtualizedResources** are needed: it is a desktop app, and browsers must find the
  helper's registration in the real per-user registry, as with the installer edition. Explain this in the
  submission's *Notes for certification*.
- The video engine is fetched from its official release on first use (as in every edition); say so in the notes.
- Browser integration: the helper is reached through the package's alias in `%LOCALAPPDATA%\Microsoft\WindowsApps`.

# Application Updates

RB4 shadPS4 Installer checks this project's GitHub Releases for newer versions. It never downloads an application update without confirmation.

## Automatic checks

Automatic checks run when an already-configured installer starts. When a new release is available, a branded prompt shows the version and release notes.

To disable this behavior, open **Settings → Application updates** and clear **Automatically check GitHub Releases when the app starts**. The manual **Check for updates** button remains available.

## Setup edition

After confirmation, the app downloads `RB4ShadPS4Installer-Setup-vX.Y.Z.exe`, verifies it against the release's `SHA256SUMS.txt`, closes the app, and opens the Windows installer. Saved paths and settings remain in Local AppData.

## Portable edition

After confirmation, the app downloads and verifies `RB4ShadPS4Installer-Portable-vX.Y.Z.zip`, then prepares the new executable.

A hidden, one-time helper is launched only for the final handoff. It:

1. Waits for the current Portable executable to close.
2. Places the new versioned executable in the same folder.
3. Starts the new version.
4. Removes the previous executable and its own temporary script.

No updater helper runs while the app is idle or when an update is declined.

## Verification and scope

Every application update is SHA-256 verified before launch. Self-updating does not modify Rock Band 4, RB4 Deluxe, DLC, custom songs, shadPS4, or saved path choices.

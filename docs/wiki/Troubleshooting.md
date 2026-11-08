# Troubleshooting

## Start with Setup health

Open **Setup health** in the installer's left sidebar. It checks the selected emulator, portable profile, RB4 2.21, RB4DX, installed-content metadata and conflicts, firmware modules, fonts, content path, disk space, and the latest shadPS4 log without changing anything.

Warnings include a suggested next action. Use **Copy report** when asking for help; the copied report can contain local folder paths. **Open latest log** opens the exact log inspected by the check.

**Export support ZIP** creates a small troubleshooting archive containing the health report—including its content-audit summary—the latest shadPS4 log, latest installer log, and basic runtime information. Configured paths, the Windows user name, email addresses, and IPv4 addresses are redacted. Keys, saves, firmware, configuration files, games, PKGs, add-ons, and backup contents are excluded. Always review the text files before sharing the ZIP.

## The portable app does not open

Extract the entire ZIP before running it. Do not launch the executable from inside the archive. Windows may show a SmartScreen prompt because community builds are not code-signed; inspect the downloaded file and choose the appropriate option only if it came from this repository.

## My existing game is not detected

Open **Settings**, verify the user folder and game library, then select **Detect installed RB4**. The library must directly contain the supported title folder (`CUSA02084` or `CUSA02901`).

## The wrong shadPS4 path appears

The installer remembers paths you explicitly saved. Use **Reset saved paths** in Settings to return to managed defaults, or select the correct existing application folder.

## A PKG is rejected

Confirm that the PKG matches the installed game's region and content type. Ensure the package extractor is configured. Corrupt, encrypted, unsupported, or mismatched packages cannot be installed.

The **Add PKG folder** picker displays folders rather than individual files. Select the collection folder itself; after confirmation, the installer scans it and all subfolders for `.pkg` files and reports how many were queued.

## An update or add-on does not appear in shadPS4

Open **Content → Audit library** first. The read-only audit checks package metadata, region, duplicate content IDs, empty folders, and unsafe linked directories; use **Copy report** to retain the result. Then restart shadPS4 so it can rescan the library and recheck the game library, user folder, and additional-content directory in Settings.

## RB4DX installs but the game still looks unmodified

The installer-managed LlysiX build must use its isolated portable profile. In Settings, the user folder should be `user` beside `shadPS4QtLauncher.exe`, and RB4DX should be under `data\GoldHEN\RB4DX` inside it. The fork uses the launcher's working directory—not the nested emulator executable directory—to find portable data. The installer migrates older layouts automatically without deleting the original AppData profile.

Standard user-selected shadPS4 installations keep their detected layout. The installer also checks both layouts when reporting whether Deluxe is installed.

## Installation failed

Read the status message and installation log. Keep the backup option enabled so the installer can roll back overwritten files. Ensure shadPS4 is closed and that the destination drive has enough free space.

If a restore reports an integrity error, do not replace files from that backup manually. Use **Settings → Recovery backups → Verify integrity** to confirm the selected backup. A missing file, size mismatch, or SHA-256 mismatch means the retained backup is incomplete or has changed; choose another backup instead.

If the problem persists, open a [GitHub issue](https://github.com/thyaguster/rb4-shadps4-installer/issues) with the installer version, game region, source type, and relevant log lines. Do not upload copyrighted game files or keys.

# Troubleshooting

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

Restart shadPS4 after installation so it can rescan the library. Recheck the game library, user folder, and additional-content directory in Settings.

## Installation failed

Read the status message and installation log. Keep the backup option enabled so the installer can roll back overwritten files. Ensure shadPS4 is closed and that the destination drive has enough free space.

If the problem persists, open a [GitHub issue](https://github.com/thyaguster/rb4-shadps4-installer/issues) with the installer version, game region, source type, and relevant log lines. Do not upload copyrighted game files or keys.

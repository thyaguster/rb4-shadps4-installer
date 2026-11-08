# Managing an Existing Installation

When the selected shadPS4 paths contain Rock Band 4, the installer detects the title ID, installed game version, RB4 Deluxe status, and add-on count. Base-game fields are hidden because they are no longer needed.

## Update the game

Choose **Install game update**, then select or drag the update PKG or extracted update folder. The update must match the detected region.

## Install or repair RB4 Deluxe

Choose **Repair RB4 Deluxe** or **Update or reinstall Deluxe**. You can select a ZIP or extracted folder, or download the current official RB4 Deluxe package offered by the installer.

## Add DLC or custom songs

Content imports unlock after RB4DX is detected. Custom songs do not work with standard RB4, and a mixed PKG folder cannot be reliably separated into official DLC and customs from filenames alone. If only standard RB4 is detected, choose **Install RB4 Deluxe** first. Game updates remain available independently.

Choose **Add content later**, then use one of these source options:

- **Add PKGs:** select one or more individual package files.
- **Add PKG folder:** select a collection folder; the installer recursively finds and queues every `.pkg` inside it.
- **Add extracted folder:** select one already-unpacked add-on folder.

Every item must match the installed game's region. The installer can process multiple queued items in one run.

Queued sources show the filename while preserving the complete path in a tooltip. Choose **Content** in the header to open the searchable installed-content page. It reads each installed package's metadata and lists its title, content ID, version, type, and folder location without modifying any files.

## Backups and rollback

With backups enabled, overwritten files are preserved before installation. If an operation fails after changes begin, the installer attempts to restore the previous state and records details in the installation log.

Close shadPS4 before changing game files, restoring a backup, or updating a managed emulator build.

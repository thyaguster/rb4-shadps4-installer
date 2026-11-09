# Emulator and Paths

Open **Settings** from the top-right corner to change these locations. Use **Back to installer** when finished.

## shadPS4 application folder

The folder containing the shadPS4 launcher executable.

- **Select existing** keeps your current installation and remembers its location.
- **Use managed** switches to the installer's Local AppData location.
- **Download LlysiX RB4 build** installs or updates the recommended RB4-focused build in its own managed folder. It uses an isolated portable profile and does not replace an existing shadPS4 installation.
- **Download latest official** installs the current official shadPS4 release separately.

## User folder

The shadPS4 data folder containing emulator configuration and metadata. The normal Windows default is under `%APPDATA%\shadPS4`.

The installer-managed LlysiX build instead uses a portable `user` folder beside `shadPS4QtLauncher.exe`. The RB4 fork resolves portable data from this launcher working directory, rather than from its nested `emulator` folder. This keeps its configuration, saves, sysmodules, and RB4DX files separate from other emulator versions. On first use, the installer safely copies relevant data from the previously selected user folder; it does not delete the original files.

When the managed LlysiX build is updated, this portable profile is copied into the staged new build before the emulator folders are swapped. The preceding build and its original profile remain in the managed `-previous` recovery folder.

## Emulator rollback

Successful managed updates retain one previous emulator build for both the LlysiX RB4 and official channels. When one is available, Settings shows its version and enables **Restore previous build**.

Restoring swaps the current and previous folders, so the replaced build remains available and the action can be undone by restoring again. For the LlysiX channel, only the emulator build is rolled back: the newest portable `user` profile remains active so settings, saves, firmware modules, and RB4DX data are not reverted. Custom emulator folders are never modified by this feature.

## Firmware modules

Open **Settings → PS4 firmware modules** to see how many currently recognized modules are installed. **Import my dump** recursively scans a user-selected folder, accepts decrypted ELF `.sprx` files, and copies them into the active user's `sys_modules` folder after confirmation. Unrecognized `.sprx` files are retained for possible future emulator support; invalid or encrypted files are skipped.

Firmware must be dumped from a PlayStation 4 owned by the user. The installer does not download or provide Sony firmware. Use the [official shadPS4 dumping instructions](https://github.com/shadps4-emu/shadPS4/wiki/I.-Quick-start-%5BUsers%5D#4-dumping-firmware-modules).

## PS4 system fonts

Open **Settings → PS4 system fonts** and choose **Import font folders**. You can select a dumped `font` folder, a dumped `font2` folder, or a parent containing both. The importer searches nested dump paths, shows the detected file counts, asks for confirmation, and merges them into the active profile's `fonts\font` and `fonts\font2` directories. Existing same-named files are replaced; unrelated files are preserved.

Dump `font` from `/preinst/common` and `font2` from `/system/common` on a PS4 you own. The installer does not download or provide these proprietary files. See the [official shadPS4 font guide](https://github.com/shadps4-emu/shadPS4/wiki/I.-Quick-start-%5BUsers%5D#6-dumping-firmware-fonts).

## Custom firmware paths

Modern shadPS4 profiles can store `addon_install_dir`, `sys_modules_dir`, and `font_dir` in `config.json`. The installer resolves absolute and user-folder-relative values and uses those active directories for detection, imports, folder shortcuts, and Setup health. Settings marks non-default module/font locations as a **custom path** and shows the full resolved path in a tooltip.

For the installer-managed LlysiX build, these three paths are aligned to the isolated portable profile whenever settings are saved. Other configuration sections and unknown keys are preserved. Existing standard shadPS4 profiles retain their configured custom paths.

RB4DX uses `user\data\GoldHEN` in the LlysiX profile. Existing standard shadPS4 installations retain their detected title-specific layout.

## Game library

The parent folder containing installed title directories such as `CUSA02084` or `CUSA02901`. The installer writes the base game and patch folders here.

## DLC install location

The destination where the installer places extracted DLC and custom songs. It is automatically derived from shadPS4's `addon_install_dir` configuration or its normal user-folder fallback. This destination is edited in Settings; it is separate from the PKG source folder selected with **Add PKG folder**.

## Package extractor

Required only for PKG sources. The installer can download and verify the supported extractor, or you can select an existing copy.

## Shortcuts

Settings can maintain a desktop shortcut for the selected shadPS4 launcher and, when an installed game is detected, a shortcut that launches Rock Band 4 directly.

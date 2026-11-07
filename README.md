# RB4 shadPS4 Installer

A Windows desktop installer for setting up **Rock Band 4** and **Rock Band 4 Deluxe** with shadPS4.

## What it does

- Unpacks a user-supplied RB4 base-game `.pkg`, or installs an already extracted dump.
- Unpacks a user-supplied RB4 2.21 update `.pkg` into shadPS4's `CUSAxxxxx-patch` folder.
- Supports the USA (`CUSA02084`) and EU (`CUSA02901`) releases.
- Installs RB4 Deluxe from its ZIP archive or an extracted folder.
- Places Deluxe's `GoldHEN` data and `RB4DX-Plugin.prx` in the locations expected by shadPS4.
- Installs multiple official DLC or custom-song PKGs into shadPS4's configured additional-content directory.
- Accepts already-extracted DLC/custom-song folders and validates their title and content IDs.
- Provides an **Add content later** mode for installing updates, DLC, and custom songs without reinstalling the base game or Deluxe.
- Remembers installer paths and automatically detects previously installed USA/EU RB4 games on later launches.
- Downloads and extracts either the latest official Windows x64 shadPS4 Qt launcher or a separately managed, pinned RB4-compatible build containing the verified RB4 shader fix.
- Downloads and extracts the official PS4/ShadPS4 RB4 Deluxe artifact from the MiloHax download source.
- Validates `param.sfo`, warns about incompatible versions, and backs up overwritten content.
- Runs preflight checks, logs installation details, automatically rolls back failed installs, and restores retained recovery backups.
- Creates maintained Desktop shortcuts for the selected emulator and direct RB4 launching.

This program does **not** download or include Rock Band 4, updates, DLC, PlayStation firmware, keys, or other copyrighted game content. Use dumps made from software you own.

## Build

Requires the .NET 8 SDK on Windows:

```powershell
dotnet build -c Release
```

Create a single-file build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The framework-dependent build will be under `bin/Release/net8.0-windows/`. The explicit single-file publish command uses the SDK's default `bin/Release/net8.0-windows/win-x64/publish/` output unless `-o` is supplied.

Create both the portable ZIP and Windows Setup.exe (requires Inno Setup 6):

```powershell
.\packaging\build-release.ps1
```

### Automated GitHub releases

The repository's Release workflow builds the portable ZIP and Setup executable, generates `SHA256SUMS.txt`, and publishes all three files whenever a version tag is pushed. For example:

```powershell
git tag v0.8.3
git push origin v0.8.3
```

The tag determines the executable and package version, so a release build does not depend on manually editing the project file first. Normal pushes and pull requests are compiled by the separate Build workflow.

## Official downloads

The shadPS4 download button queries the official `shadps4-emu/shadps4-qtlauncher` GitHub releases, selects the newest Windows x64 ZIP (preferring the Qt build), verifies the GitHub-provided SHA-256 digest when present, and extracts it into the installer's managed Local AppData folder.

The primary **Download / update LlysiX RB4 build** button queries LlysiX's latest release and accepts only its combined `shadPS4-RB4-win64-*.zip` build with a GitHub-provided SHA-256 digest. It records the installed tag and digest, skips the download when already current, stages and validates updates before replacing the managed build, and retains the preceding build in `shadPS4-RB4-previous`. If the newest release cannot be resolved or verified, it falls back to the known-good `rb4-2026-11-01` build (`cc7f732`, SHA-256 `601c8080d4912bfca5717afb5f084a4865d9b240b36ae874deeaed494568e771`). The custom RB4 channel uses its own managed folder and never silently replaces the official channel, which remains available as the secondary download choice.

The RB4 Deluxe download button uses the official PS4/ShadPS4 artifact linked by `rb4dx.milohax.org`, extracts it under the current user's local application data, validates that it contains the expected `GoldHEN` layout, and selects it as the Deluxe source.

## First-run setup and detection

On first launch, the app shows only environment setup. The user can select an existing shadPS4 installation or choose between the recommended LlysiX RB4 build and the latest official build. Downloads use separate managed Local AppData folders. Recommended user-data, game-library, add-on, and extractor locations are prefilled. **Save paths & continue** stores the choices in `%LOCALAPPDATA%\RB4ShadPS4Installer\settings.json`.

On later launches, these fields are hidden behind **Settings**. The installer scans the remembered library and shadPS4-configured game directories for `CUSA02084` and `CUSA02901`. When found, it selects the matching region and opens directly in **Add content later** mode. Standard base-game installation is then removed from the available choices; only content management and Install/Repair RB4 Deluxe remain, with known base-game and region fields hidden. It also checks for both the RB4DX GoldHEN data and plugin, identifying the installation as Rock Band 4 Deluxe only when both are present.

Settings also offers maintained Desktop shortcuts for the selected shadPS4 launcher and for launching the detected Rock Band 4 installation directly. The direct shortcut uses the LlysiX/Qt launcher's supported `-d -g "<eboot.bin>"` syntax, or the core emulator's `-g "<eboot.bin>"` syntax when only `shadPS4.exe` is available. Enabled shortcuts are refreshed after managed emulator and game updates.

## Credits

The in-app **Credits** view thanks and links to the original teams and contributors behind shadPS4, LlysiX's RB4 launcher and emulator fork, mothership-q's upstream RB4 shader fix, MiloHax/Rock Band 4 Deluxe, shadPS4Plus PKG Extractor, LibForge/ForgeTool, Onyx Music Game Toolkit, and Harmonix/Rock Band 4. These projects remain independent and retain their respective names, licenses, and ownership.

## Expected source layouts

Base game and update sources may be `.pkg` files or extracted PS4 dumps containing at least:

```text
eboot.bin
sce_sys/param.sfo
```

The Deluxe source can be the official download ZIP or an extracted folder. It must contain a `GoldHEN` folder and `RB4DX-Plugin.prx`.

## DLC and custom songs

Use **Add PKGs** to queue one or more dumped DLC packages or custom-song packages, or **Add folder** for already-extracted content. The installer reads each add-on's `param.sfo`, confirms that it targets the selected RB4 region, and installs it under:

```text
<additional-content directory>/<CUSA>/<CONTENT_ID>/
```

The additional-content path is read from shadPS4's `config.json` when available and otherwise defaults to `<shadPS4 user folder>/addcont`.

Reopen the application at any time and select **Add content later**. This mode checks that the selected region is already installed, hides the base-game source, and only processes the chosen update and add-on queue.

## PKG extractor

Automatic `.pkg` support uses the GPL-2.0 `pkg_extractor.exe` command-line utility from [AzaharPlus/shadPS4Plus](https://github.com/AzaharPlus/shadPS4Plus). Use **Download verified 1.1** in the app, select an existing copy, or place it at:

```text
tools/pkg_extractor.exe
```

beside the installer. PKG files are extracted into a temporary staging directory on the game-library drive, validated, copied into the live installation with backup protection, and then removed from staging. The original `.pkg` is never changed or deleted.

The download button retrieves extractor version 1.1 from the project's official GitHub release and verifies the publisher-provided SHA-256 (`a6a6dee84c1283949245c868e580ff8d0049d0e73df9de3096460ee26c6f04d7`) before extracting it.

`PKGInstall.exe` is not used for automation because its current release is a GUI application and does not expose a command-line extraction interface.

## Notes

- RB4 Deluxe currently targets RB4 version 2.21 (shown in-game as 2.3.7).
- Close shadPS4 before installing, then restart it afterward so the launcher rescans the files.
- Backups are stored under `user/rb4-installer-backups/<title-id>/`.

## Repository and licensing

Generated builds are intentionally excluded from source control. Attach the portable ZIP and Setup executable from `release-v<version>/` to a GitHub Release instead of committing them to the repository.

The installer source is available under the [MIT License](LICENSE). It does not bundle shadPS4, RB4 Deluxe, the PKG extractor, or copyrighted game content. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for project links, attribution, and the separate terms that apply to downloaded third-party software.

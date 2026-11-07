<p align="center">
  <img src="Assets/app-icon.png" width="112" alt="RB4 shadPS4 Installer icon">
</p>

<h1 align="center">RB4 shadPS4 Installer</h1>

<p align="center">
  Install and maintain Rock Band 4, RB4 Deluxe, updates, DLC, and custom songs for shadPS4 on Windows.
</p>

<p align="center">
  <a href="https://github.com/thyaguster/rb4-shadps4-installer/releases/latest"><strong>Download the latest release</strong></a>
  ·
  <a href="#quick-start">Quick start</a>
  ·
  <a href="THIRD_PARTY_NOTICES.md">Credits</a>
</p>

<p align="center">
  <img alt="Latest release" src="https://img.shields.io/github/v/release/thyaguster/rb4-shadps4-installer?style=flat-square&color=ef315d">
  <img alt="Build status" src="https://img.shields.io/github/actions/workflow/status/thyaguster/rb4-shadps4-installer/build.yml?branch=main&style=flat-square">
  <img alt="Windows" src="https://img.shields.io/badge/Windows-x64-53a7ff?style=flat-square">
  <img alt="License" src="https://img.shields.io/github/license/thyaguster/rb4-shadps4-installer?style=flat-square">
</p>

![Installer setup screen](docs/screenshots/installer.png)

> [!IMPORTANT]
> This project does not provide Rock Band 4, updates, DLC, firmware, keys, or other copyrighted game content. Use lawful dumps made from software you own.

## What it handles

| | |
|---|---|
| **Game setup** | Base game and updates from user-provided PKGs or extracted folders |
| **RB4 Deluxe** | Download, install, detect, repair, and update |
| **Extra content** | Multiple DLC and custom-song PKGs or extracted folders |
| **shadPS4** | Recommended LlysiX RB4 build or the latest official release |
| **Existing installs** | Detects region, game version, Deluxe, and installed add-ons |
| **Safety** | SHA-256 checks, validation, backups, rollback, and install logs |
| **Convenience** | Drag-and-drop, remembered paths, and desktop shortcuts |

Supported game regions:

- USA — `CUSA02084`
- Europe — `CUSA02901`

## Quick start

1. Download either the **Setup** or **Portable** package from [Releases](https://github.com/thyaguster/rb4-shadps4-installer/releases/latest).
2. Select an existing shadPS4 installation, or download the recommended **LlysiX RB4 build**.
3. Choose your game library and user folder.
4. Add your own RB4 PKGs or extracted folders.
5. Review the summary and install.

Already have RB4 installed? The app detects it and opens directly in content-management mode for updates, Deluxe, DLC, and custom songs.

## Screenshots

<table>
  <tr>
    <td width="50%"><img src="docs/screenshots/settings.png" alt="Installer settings"><br><strong>Managed emulator and path settings</strong></td>
    <td width="50%"><img src="docs/screenshots/credits.png" alt="Credits page"><br><strong>Credits and original-project links</strong></td>
  </tr>
</table>

## Emulator choices

- **LlysiX RB4 build — recommended:** an RB4-focused shadPS4 build containing the verified shader fix. The installer checks GitHub for updates and verifies the release digest.
- **Latest official shadPS4:** the standard official Windows release, kept in a separate managed folder.

The previous managed RB4 build is retained during updates so it can be recovered if necessary.

## Build from source

Requires Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet build -c Release
```

To produce the portable ZIP and Setup executable, install Inno Setup 6 and run:

```powershell
.\packaging\build-release.ps1
```

GitHub Actions builds every push. Pushing a version tag such as `v0.8.4` automatically publishes a release with the portable ZIP, Setup executable, release notes, and SHA-256 checksums.

## Credits and license

This installer builds on work by the shadPS4, LlysiX, mothership-q, MiloHax/RB4DX, AzaharPlus, LibForge, Onyx, and wider rhythm-game communities. See [Third-party notices](THIRD_PARTY_NOTICES.md) and the in-app Credits page for links and attribution.

Released under the [MIT License](LICENSE). Third-party downloads retain their own licenses and ownership.

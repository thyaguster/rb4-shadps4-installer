# Contributing

Thanks for helping improve RB4 shadPS4 Installer.

## Development setup

1. Use Windows with the .NET 8 SDK installed.
2. Clone the repository.
3. Run `dotnet build -c Release` from the repository root.
4. For packaged releases, install Inno Setup 6 and run `./packaging/build-release.ps1`.

## Pull requests

- Keep changes focused and explain the user-facing behavior.
- Build the project in Release configuration before opening a pull request.
- Do not commit `bin`, `obj`, `publish-*`, `release-*`, downloaded tools, PKGs, game files, firmware, keys, logs, or user settings.
- Do not add download endpoints without identifying the publisher, selecting the intended asset narrowly, and verifying a publisher-provided SHA-256 digest where available.
- Preserve the separation between user-supplied copyrighted content and freely redistributable installer code.
- Update the README and third-party notices when adding integrations or attribution.

## Reporting bugs

Include the installer version, Windows version, operation attempted, visible error, and the relevant installer log. Remove personal paths, usernames, keys, and copyrighted content before posting logs publicly.

## Legal

Only contribute code and assets you have the right to submit. Do not upload game files, DLC, firmware, encryption keys, proprietary SDK files, or other copyrighted material.

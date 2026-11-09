# Contributing

Thanks for helping improve RB4 shadPS4 Installer.

## Development setup

1. Use Windows with the .NET 8 SDK installed.
2. Clone the repository.
3. Run `dotnet build -c Release` from the repository root.
4. Run the smoke checks before submitting a change:
   - `dotnet run --project TestResults/BackupCleanupSmoke/BackupCleanupSmoke.csproj -c Release`
   - `dotnet run --project TestResults/ContentCatalogSmoke/ContentCatalogSmoke.csproj -c Release`
   - `dotnet run --project TestResults/ContentAuditSmoke/ContentAuditSmoke.csproj -c Release`
   - `dotnet run --project TestResults/CrashReportSmoke/CrashReportSmoke.csproj -c Release`
   - `./tools/test-duplicate-guard.ps1`
5. For packaged releases, install Inno Setup 6 and run `./packaging/build-release.ps1`.

## Pull requests

- Keep changes focused and explain the user-facing behavior.
- Build the project and run all smoke checks before opening a pull request. GitHub runs the same checks for pull requests, pushes to `main`, and tagged releases.
- Do not commit `bin`, `obj`, `publish-*`, `release-*`, downloaded tools, PKGs, game files, firmware, keys, logs, or user settings.
- Do not add download endpoints without identifying the publisher, selecting the intended asset narrowly, and verifying a publisher-provided SHA-256 digest where available.
- Preserve the separation between user-supplied copyrighted content and freely redistributable installer code.
- Update the README and third-party notices when adding integrations or attribution.

## Reporting bugs

Include the installer version, Windows version, operation attempted, visible error, and the relevant installer log. Remove personal paths, usernames, keys, and copyrighted content before posting logs publicly.

## Legal

Only contribute code and assets you have the right to submit. Do not upload game files, DLC, firmware, encryption keys, proprietary SDK files, or other copyrighted material.

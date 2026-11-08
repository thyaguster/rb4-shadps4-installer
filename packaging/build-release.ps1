param(
    [string]$Version = "0.8.23"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot "publish-v$Version"
$releaseDirectory = Join-Path $projectRoot "release-v$Version"
$portableZip = Join-Path $releaseDirectory "RB4ShadPS4Installer-Portable-v$Version.zip"
$portableStageDirectory = Join-Path $projectRoot "portable-v$Version"
$portableExecutable = Join-Path $portableStageDirectory "RB4ShadPS4Installer-Portable-v$Version.exe"

New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
dotnet publish (Join-Path $projectRoot "RB4ShadPS4Installer.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:Version=$Version -p:InformationalVersion=$Version `
    -p:FileVersion="$Version.0" -p:AssemblyVersion="$Version.0" `
    -o $publishDirectory

New-Item -ItemType Directory -Force -Path $portableStageDirectory | Out-Null
Get-ChildItem -LiteralPath $portableStageDirectory -File | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $publishDirectory "RB4ShadPS4Installer.exe") `
    -Destination $portableExecutable -Force

Compress-Archive -Path (Join-Path $portableStageDirectory "*") `
    -DestinationPath $portableZip -CompressionLevel Optimal -Force

$compilerCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw "Inno Setup 6 was not found. The portable ZIP was created, but Setup.exe could not be built."
}

& $compiler "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot "RB4ShadPS4Installer.iss")
Write-Host "Release created in $releaseDirectory"

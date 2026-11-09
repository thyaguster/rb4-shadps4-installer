param([string]$AssemblyPath)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $assemblyPath = Get-ChildItem -LiteralPath "$PSScriptRoot\..\bin\Release" -Recurse -File -Filter "RB4ShadPS4Installer.dll" |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($AssemblyPath) -or -not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
    throw "A built RB4ShadPS4Installer.dll was not found. Build or package the installer before running this test."
}
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath))
$root = Join-Path $env:TEMP ("rb4-duplicate-guard-" + [guid]::NewGuid().ToString("N"))
$titleId = "CUSA02084"
$contentId = "UP8802-CUSA02084_00-DUPLICATETEST001"
$source = Join-Path $root "source"
$sfoDirectory = Join-Path $source "sce_sys"
$addonRoot = Join-Path $root "addons"
$destination = Join-Path (Join-Path $addonRoot $titleId) $contentId
$user = Join-Path $root "user"
$games = Join-Path $root "games"
$game = Join-Path $games $titleId

try {
    New-Item -ItemType Directory -Force -Path $sfoDirectory,$destination,$user,$game | Out-Null
    [IO.File]::WriteAllText((Join-Path $destination "existing.txt"), "preserve")

    $keys = [Text.Encoding]::UTF8.GetBytes("TITLE_ID`0CONTENT_ID`0")
    $title = [Text.Encoding]::UTF8.GetBytes("$titleId`0")
    $content = [Text.Encoding]::UTF8.GetBytes("$contentId`0")
    $keyOffset = 52
    $dataOffset = $keyOffset + $keys.Length
    $stream = [IO.File]::Create((Join-Path $sfoDirectory "param.sfo"))
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint32]0x46535000); $writer.Write([uint32]0x00000101)
        $writer.Write([uint32]$keyOffset); $writer.Write([uint32]$dataOffset); $writer.Write([uint32]2)
        $writer.Write([uint16]0); $writer.Write([uint16]0x0204); $writer.Write([uint32]$title.Length); $writer.Write([uint32]$title.Length); $writer.Write([uint32]0)
        $writer.Write([uint16]9); $writer.Write([uint16]0x0204); $writer.Write([uint32]$content.Length); $writer.Write([uint32]$content.Length); $writer.Write([uint32]$title.Length)
        $writer.Write($keys); $writer.Write($title); $writer.Write($content)
    }
    finally { $writer.Dispose(); $stream.Dispose() }

    $requestType = $assembly.GetType("RB4ShadPS4Installer.Models.InstallRequest", $true)
    $modeType = $assembly.GetType("RB4ShadPS4Installer.Models.InstallMode", $true)
    $profileType = $assembly.GetType("RB4ShadPS4Installer.Models.ShadPs4Profile", $true)
    $stringList = [Collections.Generic.List[string]]::new(); $stringList.Add($source)
    $mode = ([Enum]::Parse($modeType, "Content")).PSObject.BaseObject
    $profile = ([Enum]::Parse($profileType, "Standard")).PSObject.BaseObject
    $requestArguments = [object[]]@(
        $mode, [string]$user, [string]$games, $null, $null, $null, $null,
        [string]$addonRoot, $stringList, [string]$titleId, $true, $profile, $false)
    $request = $requestType.GetConstructors()[0].Invoke($requestArguments)

    $progressValueType = $assembly.GetType("RB4ShadPS4Installer.Models.InstallProgress", $true)
    $progressType = [Progress``1].MakeGenericType($progressValueType)
    $progress = [Activator]::CreateInstance($progressType)
    $serviceType = $assembly.GetType("RB4ShadPS4Installer.Services.InstallerService", $true)
    $service = [Activator]::CreateInstance($serviceType, $true)
    $method = $serviceType.GetMethod("InstallAsync", [Reflection.BindingFlags]"Public, Instance")
    $task = $method.Invoke($service, @($request, $progress, [Threading.CancellationToken]::None))
    $outcome = $task.GetAwaiter().GetResult()
    if ($outcome.InstalledContentCount -ne 0 -or $outcome.SkippedExistingContentCount -ne 1) {
        throw "Unexpected outcome: installed=$($outcome.InstalledContentCount), skipped=$($outcome.SkippedExistingContentCount)"
    }
    if ([IO.File]::ReadAllText((Join-Path $destination "existing.txt")) -ne "preserve") {
        throw "Existing content was modified."
    }
    Write-Output "Runtime duplicate guard smoke test passed."
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}

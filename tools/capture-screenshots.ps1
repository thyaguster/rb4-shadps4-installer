param(
    [string]$Executable = (Join-Path $PSScriptRoot "..\publish-v0.8.30\RB4ShadPS4Installer.exe"),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\docs\screenshots"),
    [switch]$FirstRunOnly
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ScreenshotWindowApi
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr target, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
"@

function Save-WindowScreenshot([Diagnostics.Process]$Process, [string]$Path) {
    [ScreenshotWindowApi+Rect]$rect = New-Object ScreenshotWindowApi+Rect
    $rectSize = [Runtime.InteropServices.Marshal]::SizeOf([type][ScreenshotWindowApi+Rect])
    $dwmResult = [ScreenshotWindowApi]::DwmGetWindowAttribute(
        $Process.MainWindowHandle, 9, [ref]$rect, $rectSize)
    if ($dwmResult -ne 0 -and -not [ScreenshotWindowApi]::GetWindowRect($Process.MainWindowHandle, [ref]$rect)) {
        throw "Could not read the installer window bounds."
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { throw "The installer window has invalid bounds." }
    $bitmap = [Drawing.Bitmap]::new($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        # Render the window into our bitmap instead of copying screen coordinates.
        # This remains correct with display scaling and cannot include nearby windows.
        $target = $graphics.GetHdc()
        try {
            if (-not [ScreenshotWindowApi]::PrintWindow($Process.MainWindowHandle, $target, 2)) {
                throw "Windows could not render the installer window."
            }
        }
        finally { $graphics.ReleaseHdc($target) }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Invoke-AppButton([Diagnostics.Process]$Process, [string]$Name) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::NameProperty, $Name)
    $button = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $button) { throw "Could not find the '$Name' button." }
    $pattern = $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 700
}

function Set-AppTextBox([Diagnostics.Process]$Process, [string]$AutomationId, [string]$Value) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId)
    $textBox = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $textBox) { throw "Could not find the '$AutomationId' field." }
    $pattern = $textBox.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($Value)
}

function Focus-AppElement([Diagnostics.Process]$Process, [string]$Name) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::NameProperty, $Name)
    $element = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $element) { throw "Could not find the '$Name' element." }
    $element.SetFocus()
}

function Select-AppRadio([Diagnostics.Process]$Process, [string]$Name) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::NameProperty, $Name)
    $element = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $element) { throw "Could not find the '$Name' option." }
    $bounds = $element.Current.BoundingRectangle
    $x = [int]($bounds.Left + ($bounds.Width / 2))
    $y = [int]($bounds.Top + ($bounds.Height / 2))
    [ScreenshotWindowApi]::SetForegroundWindow($Process.MainWindowHandle) | Out-Null
    [ScreenshotWindowApi]::SetCursorPos($x, $y) | Out-Null
    [ScreenshotWindowApi]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [ScreenshotWindowApi]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}

function Scroll-AppElementIntoView([Diagnostics.Process]$Process, [string]$Name) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::NameProperty, $Name)
    $element = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $element) { throw "Could not find the '$Name' element." }
    # WPF does not expose ScrollItemPattern for these buttons, but focusing an
    # off-screen control brings its containing ScrollViewer into view.
    $element.SetFocus()
    Start-Sleep -Milliseconds 700
    $root.SetFocus()
    $scrollCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty, "MainScrollViewer")
    $scrollViewer = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $scrollCondition)
    if ($null -ne $scrollViewer) {
        $scrollPattern = $scrollViewer.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        if ($scrollPattern.Current.VerticallyScrollable) {
            1..5 | ForEach-Object {
                $scrollPattern.Scroll(
                    [Windows.Automation.ScrollAmount]::NoAmount,
                    [Windows.Automation.ScrollAmount]::SmallIncrement)
            }
        }
        Start-Sleep -Milliseconds 500
    }
}

function Scroll-AppToBottom([Diagnostics.Process]$Process) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty, "MainScrollViewer")
    $scrollViewer = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $scrollViewer) { throw "Could not find the main scroll area." }
    $scrollPattern = $scrollViewer.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    $scrollPattern.SetScrollPercent(
        [Windows.Automation.ScrollPattern]::NoScroll,
        100)
    Start-Sleep -Milliseconds 700
}

$settingsDirectory = Join-Path $env:LOCALAPPDATA "RB4ShadPS4Installer"
$settingsPath = Join-Path $settingsDirectory "settings.json"
$backupPath = Join-Path $env:TEMP ("rb4-installer-settings-" + [guid]::NewGuid().ToString("N") + ".json")
$hadSettings = Test-Path -LiteralPath $settingsPath
$process = $null

try {
    if ($hadSettings) { Copy-Item -LiteralPath $settingsPath -Destination $backupPath -Force }
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    if ($FirstRunOnly) {
        if (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath -Force }
        $process = Start-Process -FilePath (Resolve-Path $Executable) -PassThru
        $process.WaitForInputIdle(15000) | Out-Null
        Start-Sleep -Seconds 2
        [ScreenshotWindowApi]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
        Set-AppTextBox $process "ShadPs4InstallText" "C:\Users\Player\AppData\Local\RB4ShadPS4Installer\shadPS4"
        Set-AppTextBox $process "UserDirectoryText" "C:\Users\Player\shadPS4"
        Set-AppTextBox $process "GameLibraryText" "D:\Games\shadPS4"
        Focus-AppElement $process "Save paths & continue"
        Save-WindowScreenshot $process (Join-Path $OutputDirectory "installer.png")
        return
    }

    @{
        ShadPs4Directory = "C:\RB4\shadPS4"
        UserDirectory = "C:\Users\Player\AppData\Roaming\shadPS4"
        GameLibrary = "D:\Games\shadPS4"
        AddonDirectory = "C:\Users\Player\AppData\Roaming\shadPS4\addcont"
        PackageExtractor = $null
        TitleId = "CUSA02084"
        MaintainLauncherShortcut = $false
        MaintainGameShortcut = $false
    } | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8

    $process = Start-Process -FilePath (Resolve-Path $Executable) -PassThru
    $process.WaitForInputIdle(15000) | Out-Null
    Start-Sleep -Seconds 2
    [ScreenshotWindowApi]::SetForegroundWindow($process.MainWindowHandle) | Out-Null

    Save-WindowScreenshot $process (Join-Path $OutputDirectory "installer.png")
    Invoke-AppButton $process "⚙  Settings"
    Save-WindowScreenshot $process (Join-Path $OutputDirectory "settings.png")
    Scroll-AppElementIntoView $process "Import font folders"
    Save-WindowScreenshot $process (Join-Path $OutputDirectory "firmware.png")
    Scroll-AppElementIntoView $process "Automatically check GitHub Releases when the app starts"
    Save-WindowScreenshot $process (Join-Path $OutputDirectory "updates.png")
    Scroll-AppElementIntoView $process "Refresh list"
    Scroll-AppToBottom $process
    Save-WindowScreenshot $process (Join-Path $OutputDirectory "backups.png")
    Invoke-AppButton $process "♥  Credits"
    Save-WindowScreenshot $process (Join-Path $OutputDirectory "credits.png")
    Invoke-AppButton $process "✓  SETUP HEALTH"
    Save-WindowScreenshot $process (Join-Path $OutputDirectory "health.png")
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(5000)) { $process.Kill() }
    }

    if ($hadSettings) {
        Copy-Item -LiteralPath $backupPath -Destination $settingsPath -Force
        Remove-Item -LiteralPath $backupPath -Force
    }
    elseif (Test-Path -LiteralPath $settingsPath) {
        Remove-Item -LiteralPath $settingsPath -Force
    }
}

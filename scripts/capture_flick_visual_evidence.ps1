<#
.SYNOPSIS
Manual Windows screenshot helper for Flick's GUI acceptance.
.DESCRIPTION
Captures ONLY the specified visible Flick window rectangle (including whatever
might visually occlude it). All captures require pressing Enter; output stays
local. Does not record the desktop continuously or upload any personal media.
#>
param(
    [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$AppPid,
    [string]$OutputDirectory = "",
    [string[]]$States = @("Clips", "Editor", "Settings-Recording",
                         "Settings-Audio", "Settings-Storage", "Settings-Sharing")
)
$ErrorActionPreference = "Stop"
$proc = Get-Process -Id $AppPid -ErrorAction Stop
if ($proc.ProcessName -notin @("Flick", "LocalClip.App")) {
    throw "Only Flick.exe or legacy LocalClip.App.exe is accepted, not other Windows apps."
}
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $env:TEMP (
        "Flick-Visual-QA-" + (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss"))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Destination already exists. Use a new evidence directory."
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FlickQAScreen {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);
}
'@

Write-Warning "Screenshots may contain clip names, videos or other personal content visible over Flick."
Write-Host "Nothing is uploaded. Inspect each PNG locally before sharing evidence."
Write-Host "To capture Settings, first bring its modal window to the foreground."
Write-Host "The foreground window must belong to the requested Flick PID; other applications are refused."
$manifest = @()
$index = 0
foreach ($state in $States) {
    $index++
    Write-Host "Position Flick for state [$state], ensure private content is hidden, and press Enter."
    [void][Console]::ReadLine()
    $proc.Refresh()
    $handle = [FlickQAScreen]::GetForegroundWindow()
    $foregroundPid = [uint32]0
    if ($handle -eq [IntPtr]::Zero -or
        [FlickQAScreen]::GetWindowThreadProcessId($handle, [ref]$foregroundPid) -eq 0 -or
        $foregroundPid -ne [uint32]$AppPid) {
        Write-Warning "Flick (or its Settings dialog) must be the foreground window. No other app was captured for $state."
        continue
    }
    $rect = New-Object FlickQAScreen+RECT
    if (-not [FlickQAScreen]::GetWindowRect($handle, [ref]$rect)) {
        Write-Warning "Unable to inspect the Flick window rectangle for $state."
        continue
    }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0 -or $width -gt 10000 -or $height -gt 10000) {
        throw "Window rectangle is invalid: $width x $height"
    }
    $dpi = [FlickQAScreen]::GetDpiForWindow($handle)
    if ($dpi -eq 0) { $dpi = 96 }
    $bitmap = [Drawing.Bitmap]::new($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0,
            ([Drawing.Size]::new($width, $height)))
        $safe = ($state -replace '[^a-zA-Z0-9-]', '-')
        $output = Join-Path $OutputDirectory ("{0:D2}-{1}.png" -f $index, $safe)
        $bitmap.Save($output, [Drawing.Imaging.ImageFormat]::Png)
        $manifest += [pscustomobject]@{
            State=$state; Output=$output; PixelWidth=$width; PixelHeight=$height
            WindowDpi=$dpi; ScalePercent=[Math]::Round(100.0 * $dpi / 96.0, 1)
            ApproximateDipWidth=[Math]::Round($width * 96.0 / $dpi, 1)
            ApproximateDipHeight=[Math]::Round($height * 96.0 / $dpi, 1)
            CapturedLocal=(Get-Date).ToString("o")
        }
        Write-Host "Saved $output"
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 (
    Join-Path $OutputDirectory "manifest.json")
Write-Host "Done. Cropping is HWND-based: validate DPI, occlusion, active modal window and real video separately."

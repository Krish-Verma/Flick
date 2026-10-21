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
if ($proc.ProcessName -ne "LocalClip.App") {
    throw "Only LocalClip.App is accepted, not other Windows apps."
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
}
'@

Write-Warning "Screenshots may contain clip names, videos or other personal content visible over Flick."
Write-Host "Nothing is uploaded. Inspect each PNG locally before sharing evidence."
Write-Host "To capture Settings, first bring its modal window to the foreground."
$manifest = @()
$index = 0
foreach ($state in $States) {
    $index++
    Write-Host "Position Flick for state [$state], ensure private content is hidden, and press Enter."
    [void][Console]::ReadLine()
    $proc.Refresh()
    $handle = $proc.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) {
        Write-Warning "No visible main Flick window handle for $state — marked BLOCKED."
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
    $bitmap = New-Object Drawing.Bitmap($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0,
            (New-Object Drawing.Size($width, $height)))
        $safe = ($state -replace '[^a-zA-Z0-9-]', '-')
        $output = Join-Path $OutputDirectory ("{0:D2}-{1}.png" -f $index, $safe)
        $bitmap.Save($output, [Drawing.Imaging.ImageFormat]::Png)
        $manifest += [pscustomobject]@{
            State=$state; Output=$output; PixelWidth=$width; PixelHeight=$height
            CapturedLocal=(Get-Date).ToString("o")
        }
        Write-Host "Saved $output"
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 (
    Join-Path $OutputDirectory "manifest.json")
Write-Host "Done. Cropping is HWND-based: validate DPI, occlusion, active modal window and real video separately."

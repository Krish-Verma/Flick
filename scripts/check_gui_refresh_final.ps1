# Structural regression gate; hosted WPF staging tests and hardware QA are separate.
$ErrorActionPreference = 'Stop'
$root = (Get-Item $PSScriptRoot).Parent.FullName
$main = Get-Content "$root/src/LocalClip.App/MainWindow.xaml" -Raw
[xml]$xml = $main
$ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
$ns.AddNamespace('w', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
if ($xml.SelectNodes('//w:MediaElement', $ns).Count -ne 1) { throw 'Exactly one preview decoder is permitted' }
foreach ($name in @('Trim', 'Audio', 'Original', 'Share')) {
    $node = $xml.SelectSingleNode("//w:Button[@x:Name='Tool$name']", $ns)
    if (!$node -or $node.GetAttribute('MinHeight') -ne '64') { throw "Missing accessible spaced tool: $name" }
}
foreach ($name in @('TrimPanel', 'AudioPanel', 'SharePanel', 'DialogLayer')) {
    $node = $xml.SelectSingleNode("//*[@x:Name='$name']", $ns)
    if (!$node -or $node.GetAttribute('Visibility') -ne 'Collapsed') { throw "Surface must start closed: $name" }
}
Get-ChildItem "$root/src/LocalClip.App" -Filter '*.cs' -Recurse | ForEach-Object {
    $code = Get-Content $_.FullName -Raw
    if ($code -match '\bMessageBox\.Show\(') { throw "Native app-owned message dialog: $($_.Name)" }
}
$notify = Get-Content "$root/src/LocalClip.App/Services/FlickNotificationService.cs" -Raw
if ($notify -match 'clipTitle|Last Round Clutch|GetFileName') { throw 'Save banner must not accept clip metadata' }
Write-Host 'R3-R8 structural gate PASS; real visual/hardware/performance acceptance remains separate.'

param()

$ErrorActionPreference = 'Stop'
$tepCore = Join-Path $PSScriptRoot 'Core'
$tepAssembly = Join-Path $tepCore 'TrueBIM.App.dll'
if (-not (Test-Path -LiteralPath $tepAssembly)) {
    throw 'Run this script from the extracted TEP package containing Core/TrueBIM.App.dll.'
}

$tepVersion = 'TEP-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$tepTarget = Join-Path $env:APPDATA "TrueBIM/2024/$tepVersion"
if (Test-Path -LiteralPath $tepTarget) { throw "Destination already exists: $tepTarget" }
$tepManifestDirectory = Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2024'
$tepManifest = Join-Path $tepManifestDirectory 'TrueBIM.addin'
$tepBackup = $null
if (Test-Path -LiteralPath $tepManifest) {
    [xml]$tepPrevious = Get-Content -LiteralPath $tepManifest -Raw
    if ($tepPrevious.RevitAddIns.AddIn.FullClassName -ne 'TrueBIM.App.App') {
        throw 'TrueBIM.addin points to another application. No changes were made.'
    }
    $tepBackup = "$tepManifest.$tepVersion.backup"
    Copy-Item -LiteralPath $tepManifest -Destination $tepBackup
}
New-Item -ItemType Directory -Path $tepTarget | Out-Null
foreach ($tepFile in Get-ChildItem -LiteralPath $tepCore) {
    Copy-Item -LiteralPath $tepFile.FullName -Destination $tepTarget -Recurse
}
New-Item -ItemType Directory -Path $tepManifestDirectory -Force | Out-Null
$tepInstalledAssembly = [Security.SecurityElement]::Escape((Join-Path $tepTarget 'TrueBIM.App.dll'))
$tepXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>TrueBIM</Name>
    <Assembly>$tepInstalledAssembly</Assembly>
    <AddInId>8F8E8CC7-D3C9-49BA-8F40-AD0F2F8D32F7</AddInId>
    <FullClassName>TrueBIM.App.App</FullClassName>
    <VendorId>TRBM</VendorId>
    <VendorDescription>TrueBIM</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
Set-Content -LiteralPath $tepManifest -Value $tepXml -Encoding utf8
Write-Host "Installed: $tepTarget"
Write-Host 'Restart Revit 2024 when convenient. Command: TrueBIM > AR > Проверка ТЭП.'
if ($tepBackup) { Write-Host "Rollback: restore the contents of $tepBackup to $tepManifest." }

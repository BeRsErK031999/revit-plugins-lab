param([string] $ApplicationDataRoot = $env:APPDATA)

$ErrorActionPreference = 'Stop'
$fillCore = Join-Path $PSScriptRoot 'Core'
$fillAssembly = Join-Path $fillCore 'TrueBIM.App.dll'
if (-not (Test-Path -LiteralPath $fillAssembly)) { throw 'Extract the complete package before running Install.ps1.' }
if (Get-Process Revit -ErrorAction SilentlyContinue | Where-Object { $_.Path -match '\\Revit 2019\\Revit.exe$' }) {
    throw 'Close Revit 2019 before installing this package.'
}
$fillChecksums = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'checksums.json') -Raw | ConvertFrom-Json
foreach ($entry in $fillChecksums) {
    $checkedPath = [IO.Path]::GetFullPath((Join-Path $fillCore $entry.Path))
    if (-not $checkedPath.StartsWith([IO.Path]::GetFullPath($fillCore).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package path.' }
    if (-not (Test-Path -LiteralPath $checkedPath) -or (Get-FileHash -LiteralPath $checkedPath).Hash -ne $entry.SHA256) { throw "Package checksum failed: $($entry.Path)" }
}
$fillRelease = 'RebarFill-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$fillTarget = Join-Path $ApplicationDataRoot "TrueBIM\2019\$fillRelease"
$fillManifestDirectory = Join-Path $ApplicationDataRoot 'Autodesk\Revit\Addins\2019'
$fillManifest = Join-Path $fillManifestDirectory 'TrueBIM.addin'
if (Test-Path -LiteralPath $fillManifest) {
    [xml] $previous = Get-Content -LiteralPath $fillManifest -Raw
    if (@($previous.RevitAddIns.AddIn).Count -ne 1 -or $previous.RevitAddIns.AddIn.FullClassName -ne 'TrueBIM.App.App') {
        throw 'Existing TrueBIM.addin belongs to another application or contains multiple entries. No changes were made.'
    }
}
New-Item -ItemType Directory -Path $fillTarget -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $fillCore) { Copy-Item -LiteralPath $item.FullName -Destination $fillTarget -Recurse }
New-Item -ItemType Directory -Path $fillManifestDirectory -Force | Out-Null
$fillBackup = $null
if (Test-Path -LiteralPath $fillManifest) {
    $fillBackup = Join-Path $fillTarget 'Previous-TrueBIM.addin.backup'
    Copy-Item -LiteralPath $fillManifest -Destination $fillBackup
}
$fillInstalledAssembly = [Security.SecurityElement]::Escape((Join-Path $fillTarget 'TrueBIM.App.dll'))
$fillXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>TrueBIM</Name>
    <Assembly>$fillInstalledAssembly</Assembly>
    <AddInId>8F8E8CC7-D3C9-49BA-8F40-AD0F2F8D32F7</AddInId>
    <FullClassName>TrueBIM.App.App</FullClassName>
    <VendorId>TRBM</VendorId>
    <VendorDescription>TrueBIM</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
[IO.File]::WriteAllText($fillManifest, $fillXml, [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ InstalledDirectory = $fillTarget; Manifest = $fillManifest; PreviousManifestBackup = $fillBackup } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fillTarget 'installation.json') -Encoding UTF8
Write-Output "Installed for Revit 2019: $fillTarget"
Write-Output 'Command: TrueBIM > KR > Rebar parameter fill (Russian UI).'
if ($fillBackup) { Write-Output "Rollback: restore $fillBackup to $fillManifest with Revit 2019 closed." }

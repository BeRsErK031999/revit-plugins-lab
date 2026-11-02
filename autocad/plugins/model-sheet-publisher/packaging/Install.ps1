$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'ModelSheetPublisher.bundle'
$targetRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$target = Join-Path $targetRoot 'ModelSheetPublisher.bundle'
if (!(Test-Path -LiteralPath (Join-Path $source 'PackageContents.xml'))) { throw 'Extract the complete ZIP before running Install.cmd.' }
if (Get-Process acad,accoreconsole -ErrorAction SilentlyContinue) { throw 'Close all AutoCAD versions and Core Console before installation.' }
& (Join-Path $PSScriptRoot 'Verify-Package.ps1')
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
$targetRoot = [IO.Path]::GetFullPath($targetRoot)
$target = [IO.Path]::GetFullPath($target)
if ([IO.Path]::GetDirectoryName($target) -ne $targetRoot) { throw 'Unexpected installation target.' }
if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Plugin target is a link. Please use a regular folder.' }
$stage = Join-Path $targetRoot ('.msp-' + [guid]::NewGuid().ToString('N'))
$backup = $null
try {
    Copy-Item -LiteralPath $source -Destination $stage -Recurse
    Get-ChildItem -LiteralPath $stage -File -Recurse | Unblock-File
    if (Test-Path -LiteralPath $target) {
        $backupRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'ModelSheetPublisher\Backups'))
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        $backup = [IO.Path]::GetFullPath((Join-Path $backupRoot ('ModelSheetPublisher-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))))
        if ([IO.Path]::GetDirectoryName($backup) -ne $backupRoot) { throw 'Unexpected backup target.' }
        Move-Item -LiteralPath $target -Destination $backup
    }
    Move-Item -LiteralPath $stage -Destination $target
} catch {
    if ($backup -and (Test-Path -LiteralPath $backup) -and !(Test-Path -LiteralPath $target)) { Move-Item -LiteralPath $backup -Destination $target }
    throw
}
Write-Host "Installed: $target"
if ($backup) { Write-Host "Previous version saved: $backup" }
Write-Host 'Installed for AutoCAD 2021-2026 (Windows x64). The matching module is selected automatically.'
Write-Host 'Ribbon: Pechat PDF / Listy v PDF (Russian labels). Command: MSPUBLISH.'

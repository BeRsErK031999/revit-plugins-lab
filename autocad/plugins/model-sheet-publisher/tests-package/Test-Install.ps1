param([Parameter(Mandatory=$true)][string]$PackageRoot, [string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if (!$OutputRoot) { $OutputRoot = Join-Path $root ('..\..\tmp\model-sheet-publisher\install-check-' + [guid]::NewGuid().ToString('N')) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Choose a fresh test directory.' }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$package = Join-Path $OutputRoot 'package'
Copy-Item -LiteralPath $PackageRoot -Destination $package -Recurse
$savedRoaming = $env:APPDATA; $savedLocal = $env:LOCALAPPDATA
try {
    $env:APPDATA = Join-Path $OutputRoot 'roaming'; $env:LOCALAPPDATA = Join-Path $OutputRoot 'local'
    $target = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\ModelSheetPublisher.bundle'
    & (Join-Path $package 'Install.ps1')
    foreach ($framework in @('net48', 'net8.0-windows')) {
        if (!(Test-Path -LiteralPath (Join-Path $target "Contents\Windows\$framework\ModelSheetPublisher.dll"))) { throw 'Clean install missing module.' }
    }
    # Simulate an older flat bundle, then confirm upgrade replaces the whole directory.
    Set-Content -LiteralPath (Join-Path $target 'Contents\Windows\legacy-version.txt') -Value 'old version retained in backup'
    & (Join-Path $package 'Install.ps1')
    $backups = @(Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'ModelSheetPublisher\Backups') -Directory)
    if ($backups.Count -ne 1 -or !(Test-Path -LiteralPath (Join-Path $backups[0].FullName 'Contents\Windows\legacy-version.txt'))) { throw 'Old version backup missing.' }
    if (Test-Path -LiteralPath (Join-Path $target 'Contents\Windows\legacy-version.txt')) { throw 'Old files leaked into upgraded package.' }
    $installedDll = Join-Path $target 'Contents\Windows\net8.0-windows\ModelSheetPublisher.dll'
    $before = (Get-FileHash -LiteralPath $installedDll).Hash
    $damagedDll = Join-Path $package 'ModelSheetPublisher.bundle\Contents\Windows\net8.0-windows\ModelSheetPublisher.dll'
    [IO.File]::WriteAllBytes($damagedDll, [byte[]]@(0, 1, 2))
    $rejected = $false
    try { & (Join-Path $package 'Install.ps1') } catch { $rejected = $_.Exception.Message -like '*incomplete or damaged*' }
    if (!$rejected -or (Get-FileHash -LiteralPath $installedDll).Hash -ne $before) { throw 'Damaged package changed existing installation.' }
    $remainingBackups = @(Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'ModelSheetPublisher\Backups') -Directory)
    if ($remainingBackups.Count -ne 1) { throw 'Invalid package moved the installed bundle.' }
    'PASS: clean installation; full-folder upgrade; previous files backed up; corrupted modern module rejected before installed files change.' | Set-Content (Join-Path $OutputRoot 'PASS.txt')
    Write-Host "Installer checks passed: $OutputRoot"
} finally { $env:APPDATA = $savedRoaming; $env:LOCALAPPDATA = $savedLocal }

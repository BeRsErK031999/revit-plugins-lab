$ErrorActionPreference = 'Stop'
$bundle = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'ModelSheetPublisher.bundle'))
$prefix = $bundle + [IO.Path]::DirectorySeparatorChar
$hashFile = Join-Path $PSScriptRoot 'SHA256SUMS.txt'
if (!(Test-Path -LiteralPath $hashFile)) { throw 'Package checksum file is missing. Extract the complete ZIP.' }
$verified = @{}
foreach ($line in Get-Content -LiteralPath $hashFile) {
    if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid package checksum entry.' }
    $expected = $Matches[1]
    $relative = $Matches[2]
    if ([IO.Path]::IsPathRooted($relative)) { throw 'Invalid absolute path in package checksum list.' }
    $path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relative))
    if (!$path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Checksum path is outside the plugin bundle.' }
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw "Package is incomplete or damaged: $relative" }
    $verified[$path] = $true
}
foreach ($file in Get-ChildItem -LiteralPath $bundle -File -Recurse) {
    if (!$verified.ContainsKey($file.FullName)) { throw "Unexpected file in plugin bundle: $($file.Name)" }
}
foreach ($required in @('PackageContents.xml', 'Contents\Windows\net48\ModelSheetPublisher.dll', 'Contents\Windows\net8.0-windows\ModelSheetPublisher.dll')) {
    if (!$verified.ContainsKey((Join-Path $bundle $required))) { throw "Required package module is missing: $required" }
}
Write-Host 'Package verified: AutoCAD 2021-2024 and 2025-2026 modules.'

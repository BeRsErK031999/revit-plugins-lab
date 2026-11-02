param([string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
[xml]$manifest = Get-Content -LiteralPath (Join-Path $root 'packaging\PackageContents.xml') -Raw
$version = $manifest.ApplicationPackage.AppVersion
if (!$OutputRoot) { $OutputRoot = Join-Path $root "..\..\dist\ModelSheetPublisher-$version-AutoCAD2021-2026" }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if ((Test-Path -LiteralPath $OutputRoot) -or (Test-Path -LiteralPath ($OutputRoot + '.zip'))) { throw "Output already exists: $OutputRoot" }
$sdk = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
# A fresh output tree prevents an older target's DLLs from leaking into another target's package.
$build = Join-Path $root ('src\bin\Package-' + [guid]::NewGuid().ToString('N'))
& $sdk build (Join-Path $root 'src\ModelSheetPublisher.csproj') -c Release "-p:BaseOutputPath=$build\" '-p:IncludeTestHarness=false' --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $sdk run --project (Join-Path $root 'tests\ModelSheetPublisher.Tests.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
$bundle = Join-Path $OutputRoot 'ModelSheetPublisher.bundle'
foreach ($framework in @('net48', 'net8.0-windows')) {
    $targetBuild = Join-Path $build "Release\$framework"
    $contents = Join-Path $bundle "Contents\Windows\$framework"
    New-Item -ItemType Directory -Path $contents -Force | Out-Null
    $files = Get-ChildItem -LiteralPath $targetBuild -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'ModelSheetPublisher.deps.json' }
    if ($files.Name -match '^(Ac.*|AdWindows|AdUI.*|HostCheck.*)\.dll$') { throw "Autodesk/test runtime must not be distributed: $targetBuild" }
    $files | Copy-Item -Destination $contents
}
Copy-Item -LiteralPath (Join-Path $root 'packaging\PackageContents.xml') -Destination $bundle
foreach ($file in @('Install.ps1', 'Install.cmd', 'Verify-Package.ps1')) { Copy-Item -LiteralPath (Join-Path $root "packaging\$file") -Destination $OutputRoot }
foreach ($file in @('README.md', 'VALIDATION.md', 'THIRD-PARTY-NOTICES.txt')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $OutputRoot }
$assets = Get-Content -LiteralPath (Join-Path $root 'src\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
foreach ($package in $assets.libraries.PSObject.Properties) {
    if ($package.Name -like 'Microsoft.NETFramework.ReferenceAssemblies*' -or $package.Name -like 'AutoCAD.NET*') { continue }
    $packageFolder = Join-Path $packageRoot $package.Value.path
    if (!(Test-Path -LiteralPath $packageFolder)) { continue }
    $licenseFolder = Join-Path $OutputRoot ('Licenses\' + $package.Name.Replace('/', '-'))
    New-Item -ItemType Directory -Path $licenseFolder -Force | Out-Null
    Get-ChildItem -LiteralPath $packageFolder -File | Where-Object { $_.Name -match '(?i)license|third-party|\.nuspec$' } | Copy-Item -Destination $licenseFolder
}
$hashes = Get-ChildItem -LiteralPath $bundle -File -Recurse | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $_.FullName.Substring($OutputRoot.Length + 1).Replace('\', '/')
}
$hashes | Set-Content -LiteralPath (Join-Path $OutputRoot 'SHA256SUMS.txt') -Encoding ascii
& (Join-Path $OutputRoot 'Verify-Package.ps1')
& $sdk run --project (Join-Path $root 'tests-package\PackageCheck.csproj') -c Release -- $OutputRoot
if ($LASTEXITCODE -ne 0) { throw 'Package checks failed.' }
Compress-Archive -LiteralPath $OutputRoot -DestinationPath ($OutputRoot + '.zip')
Write-Host ($OutputRoot + '.zip')

param([Parameter(Mandatory = $true)][string] $Revit2019ApiRoot)

$ErrorActionPreference = 'Stop'
$fillRepository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
$fillDotnet = Resolve-DotNetSdk
foreach ($name in @('RevitAPI.dll', 'RevitAPIUI.dll')) {
    $api = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $Revit2019ApiRoot $name))
    if ($api.Version.Major -ne 19) { throw "Expected Revit 2019 reference: $name; actual version: $($api.Version)" }
}
$fillProject = Join-Path $fillRepository 'plugins/truebim/src/TrueBIM.App/TrueBIM.App.csproj'
& $fillDotnet build $fillProject -c Release -f net48 -p:RevitVersion=2019 "-p:RevitApiRoot=$Revit2019ApiRoot" -p:NuGetAudit=false --no-restore --nologo --verbosity:quiet -p:OutputPath=bin/RebarFill2019/ -p:IntermediateOutputPath=obj/RebarFill2019/
if ($LASTEXITCODE -ne 0) { throw 'Revit 2019 build failed.' }
$fillBuild = Join-Path $fillRepository 'plugins/truebim/src/TrueBIM.App/bin/RebarFill2019'
$fillPackage = Join-Path $fillRepository ('dist/TrueBIM-RebarFill-Revit2019-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $fillPackage) { throw "Package already exists: $fillPackage" }
$fillCore = Join-Path $fillPackage 'Core'
New-Item -ItemType Directory -Path $fillCore -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $fillBuild) { Copy-Item -LiteralPath $file.FullName -Destination $fillCore -Recurse }
$fillPlugin = Join-Path $fillRepository 'plugins/truebim'
foreach ($module in @(@('print', 'Print'), @('sheet-numbering', 'SheetNumbering'), @('schedule-column-collapse', 'ScheduleColumnCollapse'))) {
    $destination = Join-Path $fillCore ('Modules/' + $module[1])
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $fillPlugin ('modules/' + $module[0])) | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse
    }
}
New-Item -ItemType Directory -Path (Join-Path $fillCore 'Assets') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $fillPlugin 'assets/icons') -Destination (Join-Path $fillCore 'Assets/icons') -Recurse
Copy-Item -LiteralPath (Join-Path $fillPlugin 'docs') -Destination (Join-Path $fillCore 'Docs') -Recurse
foreach ($forbidden in @('RevitAPI.dll', 'RevitAPIUI.dll')) {
    if (Test-Path -LiteralPath (Join-Path $fillCore $forbidden)) { throw 'Revit API references must not be distributed in the package.' }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-rebar-fill-2019.ps1') -Destination (Join-Path $fillPackage 'Install.ps1')
Copy-Item -LiteralPath (Join-Path $fillRepository 'plugins/truebim/docs/rebar-fill.md') -Destination (Join-Path $fillPackage 'README.md')
Copy-Item -LiteralPath (Join-Path $fillRepository 'plugins/truebim/docs/rebar-fill-validation.md') -Destination (Join-Path $fillPackage 'VALIDATION.md')
Get-ChildItem -LiteralPath $fillCore -Recurse -File | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($fillCore.Length + 1); SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fillPackage 'checksums.json') -Encoding UTF8
$fillVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $fillPlugin 'installer/TrueBIM.iss') -Raw), '#define AppVersion "([^"]+)"').Groups[1].Value
if (-not $fillVersion) { throw 'Installer version was not found.' }
[pscustomobject]@{ Version = $fillVersion; TargetRevit = '2019'; ReferenceVersion = '19.0.0.0'; BuiltAt = (Get-Date).ToString('o'); SourceCommit = (& git -C $fillRepository rev-parse HEAD); IncludesLocalChanges = $true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fillPackage 'build-info.json') -Encoding UTF8
Compress-Archive -LiteralPath $fillPackage -DestinationPath "$fillPackage.zip"
Write-Output "Package: $fillPackage.zip"

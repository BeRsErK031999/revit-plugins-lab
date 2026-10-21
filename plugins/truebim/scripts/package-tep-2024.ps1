param()

$ErrorActionPreference = 'Stop'
$tepRepository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
$tepDotnet = Resolve-DotNetSdk
$tepProject = Join-Path $tepRepository 'plugins/truebim/src/TrueBIM.App/TrueBIM.App.csproj'
& $tepDotnet build $tepProject -c Release -f net48 -p:RevitVersion=2024 --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Revit 2024 build failed.' }
$tepBuild = Join-Path $tepRepository 'plugins/truebim/src/TrueBIM.App/bin/Release/net48'
$tepPackage = Join-Path $tepRepository ('dist/TrueBIM-TEP-Revit2024-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $tepPackage) { throw "Package already exists: $tepPackage" }
New-Item -ItemType Directory -Path (Join-Path $tepPackage 'Core') -Force | Out-Null
foreach ($tepFile in Get-ChildItem -LiteralPath $tepBuild) {
    Copy-Item -LiteralPath $tepFile.FullName -Destination (Join-Path $tepPackage 'Core') -Recurse
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-tep-package-2024.ps1') -Destination (Join-Path $tepPackage 'Install.ps1')
Copy-Item -LiteralPath (Join-Path $tepRepository 'plugins/truebim/docs/tep-check.md') -Destination (Join-Path $tepPackage 'README.md')
Copy-Item -LiteralPath (Join-Path $tepRepository 'plugins/truebim/docs/tep-validation-2026-10-20.md') -Destination (Join-Path $tepPackage 'VALIDATION.md')
Compress-Archive -LiteralPath $tepPackage -DestinationPath "$tepPackage.zip"
Write-Host "Package: $tepPackage.zip"

param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",

    [switch] $SkipBuild,

    [string] $RevitApiRoot = ""
)

$ErrorActionPreference = "Stop"

$runningRevit = Get-Process -Name "Revit" -ErrorAction SilentlyContinue
if ($runningRevit) {
    throw "Revit is running. Close Revit before local deploy so TrueBIM.App.dll is not locked."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$artifactsDir = Join-Path $repoRoot "plugins\truebim\artifacts-2026"
$manifestSource = Join-Path $repoRoot "plugins\truebim\manifests\2026\TrueBIM.addin"
$versionTargetRoot = Join-Path $env:APPDATA "TrueBIM\2026"
$coreTargetDir = Join-Path $versionTargetRoot "Core"
$addinTargetDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2026"
$addinTargetPath = Join-Path $addinTargetDir "TrueBIM.addin"

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build-artifacts-2026.ps1") -Configuration $Configuration -RevitApiRoot $RevitApiRoot
}

$projectOutput = Join-Path $artifactsDir "Core\TrueBIM.App.dll"
if (-not (Test-Path -LiteralPath $projectOutput)) {
    throw "Revit 2026 build output was not found at '$projectOutput'."
}

& (Join-Path $PSScriptRoot "clean-local-2026.ps1")

New-Item -ItemType Directory -Path $versionTargetRoot -Force | Out-Null
New-Item -ItemType Directory -Path $addinTargetDir -Force | Out-Null
Copy-Item -Path (Join-Path $artifactsDir "Core") -Destination $versionTargetRoot -Recurse -Force
Copy-Item -Path (Join-Path $artifactsDir "Modules") -Destination $versionTargetRoot -Recurse -Force
Copy-Item -Path (Join-Path $artifactsDir "Assets") -Destination $versionTargetRoot -Recurse -Force
Copy-Item -Path (Join-Path $artifactsDir "Docs") -Destination $versionTargetRoot -Recurse -Force

$deployedAssemblyPath = [string] (Join-Path $coreTargetDir "TrueBIM.App.dll")
[xml] $manifest = Get-Content -LiteralPath $manifestSource
$manifest.RevitAddIns.AddIn.Assembly = $deployedAssemblyPath
$manifest.Save($addinTargetPath)

Write-Host "Deployed Revit 2026 TrueBIM payload to $versionTargetRoot"
Write-Host "Deployed TrueBIM.addin to $addinTargetPath"

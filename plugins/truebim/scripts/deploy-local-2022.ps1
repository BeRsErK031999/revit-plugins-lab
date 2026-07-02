param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",

    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$projectPath = Join-Path $repoRoot "plugins\truebim\src\TrueBIM.App\TrueBIM.App.csproj"
$projectOutputDir = Join-Path $repoRoot "plugins\truebim\src\TrueBIM.App\bin\$Configuration\net48"
$manifestSource = Join-Path $repoRoot "plugins\truebim\manifests\2022\TrueBIM.addin"
$sheetNumberingModuleSourceDir = Join-Path $repoRoot "plugins\truebim\modules\sheet-numbering"
$scheduleColumnCollapseModuleSourceDir = Join-Path $repoRoot "plugins\truebim\modules\schedule-column-collapse"
$assetsSourceDir = Join-Path $repoRoot "plugins\truebim\assets"
$coreTargetDir = Join-Path $env:APPDATA "TrueBIM\2022\Core"
$sheetNumberingTargetDir = Join-Path $env:APPDATA "TrueBIM\2022\Modules\SheetNumbering"
$scheduleColumnCollapseTargetDir = Join-Path $env:APPDATA "TrueBIM\2022\Modules\ScheduleColumnCollapse"
$assetsTargetDir = Join-Path $env:APPDATA "TrueBIM\2022\Assets"
$addinTargetDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2022"
$addinTargetPath = Join-Path $addinTargetDir "TrueBIM.addin"

. (Join-Path $PSScriptRoot "resolve-dotnet-sdk.ps1")
$dotnetPath = Resolve-DotNetSdk

$runningRevit = Get-Process -Name "Revit" -ErrorAction SilentlyContinue
if ($runningRevit) {
    throw "Revit is running. Close Revit before local deploy so TrueBIM.App.dll is not locked."
}

if (-not $SkipBuild) {
    & $dotnetPath build $projectPath --configuration $Configuration --framework net48 --nologo --verbosity:minimal
}

$appAssembly = Join-Path $projectOutputDir "TrueBIM.App.dll"
if (-not (Test-Path $appAssembly)) {
    throw "Build output was not found at '$appAssembly'."
}

& (Join-Path $PSScriptRoot "clean-local-2022.ps1")

New-Item -ItemType Directory -Path $coreTargetDir -Force | Out-Null
New-Item -ItemType Directory -Path $sheetNumberingTargetDir -Force | Out-Null
New-Item -ItemType Directory -Path $scheduleColumnCollapseTargetDir -Force | Out-Null
New-Item -ItemType Directory -Path $assetsTargetDir -Force | Out-Null
New-Item -ItemType Directory -Path $addinTargetDir -Force | Out-Null

Copy-Item -Path (Join-Path $projectOutputDir "*") -Destination $coreTargetDir -Recurse -Force
Copy-Item -Path (Join-Path $sheetNumberingModuleSourceDir "module.json") -Destination $sheetNumberingTargetDir -Force
Copy-Item -Path (Join-Path $sheetNumberingModuleSourceDir "README.md") -Destination $sheetNumberingTargetDir -Force
Copy-Item -Path (Join-Path $scheduleColumnCollapseModuleSourceDir "module.json") -Destination $scheduleColumnCollapseTargetDir -Force
Copy-Item -Path (Join-Path $scheduleColumnCollapseModuleSourceDir "README.md") -Destination $scheduleColumnCollapseTargetDir -Force
Copy-Item -Path (Join-Path $assetsSourceDir "icons") -Destination $assetsTargetDir -Recurse -Force

$deployedAssemblyPath = [string] (Join-Path $coreTargetDir "TrueBIM.App.dll")

# Revit does not expand Windows environment variables in AddIn Assembly paths reliably.
# Generate the local manifest with an absolute path while keeping the source manifest reusable for packaging.
[xml] $manifest = Get-Content $manifestSource
$manifest.RevitAddIns.AddIn.Assembly = $deployedAssemblyPath
$manifest.Save($addinTargetPath)

Write-Host "Deployed TrueBIM net48 output to $coreTargetDir"
Write-Host "Deployed Sheet Numbering module manifest to $sheetNumberingTargetDir"
Write-Host "Deployed Schedule Column Collapse module manifest to $scheduleColumnCollapseTargetDir"
Write-Host "Deployed TrueBIM assets to $assetsTargetDir"
Write-Host "Deployed TrueBIM.addin to $addinTargetPath"

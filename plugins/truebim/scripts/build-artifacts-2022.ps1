param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$trueBimRoot = Join-Path $repoRoot "plugins\truebim"
$projectPath = Join-Path $trueBimRoot "src\TrueBIM.App\TrueBIM.App.csproj"
$projectOutputDir = Join-Path $trueBimRoot "src\TrueBIM.App\bin\$Configuration\net48"
$artifactsDir = Join-Path $trueBimRoot "artifacts-2022"
$coreArtifactsDir = Join-Path $artifactsDir "Core"
$printArtifactsDir = Join-Path $artifactsDir "Modules\Print"
$sheetNumberingArtifactsDir = Join-Path $artifactsDir "Modules\SheetNumbering"
$scheduleColumnCollapseArtifactsDir = Join-Path $artifactsDir "Modules\ScheduleColumnCollapse"
$docsArtifactsDir = Join-Path $artifactsDir "Docs"
$assetsArtifactsDir = Join-Path $artifactsDir "Assets"
$docsAssetsSourceDir = Join-Path $trueBimRoot "docs\assets"

. (Join-Path $PSScriptRoot "resolve-dotnet-sdk.ps1")
$dotnetPath = Resolve-DotNetSdk

& $dotnetPath build $projectPath --configuration $Configuration --framework net48 --nologo --verbosity:minimal

$appAssembly = Join-Path $projectOutputDir "TrueBIM.App.dll"
if (-not (Test-Path $appAssembly)) {
    throw "Build output was not found at '$appAssembly'."
}

if (Test-Path $artifactsDir) {
    Remove-Item -LiteralPath $artifactsDir -Recurse -Force
}

New-Item -ItemType Directory -Path $coreArtifactsDir -Force | Out-Null
New-Item -ItemType Directory -Path $printArtifactsDir -Force | Out-Null
New-Item -ItemType Directory -Path $sheetNumberingArtifactsDir -Force | Out-Null
New-Item -ItemType Directory -Path $scheduleColumnCollapseArtifactsDir -Force | Out-Null
New-Item -ItemType Directory -Path $docsArtifactsDir -Force | Out-Null
New-Item -ItemType Directory -Path $assetsArtifactsDir -Force | Out-Null

Copy-Item -Path (Join-Path $projectOutputDir "*") -Destination $coreArtifactsDir -Recurse -Force

Copy-Item -Path (Join-Path $trueBimRoot "modules\print\module.json") -Destination $printArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "modules\print\README.md") -Destination $printArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "modules\sheet-numbering\module.json") -Destination $sheetNumberingArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "modules\sheet-numbering\README.md") -Destination $sheetNumberingArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "modules\schedule-column-collapse\module.json") -Destination $scheduleColumnCollapseArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "modules\schedule-column-collapse\README.md") -Destination $scheduleColumnCollapseArtifactsDir -Force

Copy-Item -Path (Join-Path $trueBimRoot "README.md") -Destination $docsArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "docs\*.md") -Destination $docsArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "installer\README.md") -Destination (Join-Path $docsArtifactsDir "installer-README.md") -Force
if (Test-Path -LiteralPath $docsAssetsSourceDir) {
    Copy-Item -Path $docsAssetsSourceDir -Destination $docsArtifactsDir -Recurse -Force
}

Copy-Item -Path (Join-Path $trueBimRoot "assets\icons") -Destination $assetsArtifactsDir -Recurse -Force

Write-Host "Built TrueBIM artifacts for Revit 2022."
Write-Host "Core: $coreArtifactsDir"
Write-Host "Print: $printArtifactsDir"
Write-Host "Sheet Numbering: $sheetNumberingArtifactsDir"
Write-Host "Schedule Column Collapse: $scheduleColumnCollapseArtifactsDir"
Write-Host "Docs: $docsArtifactsDir"
Write-Host "Assets: $assetsArtifactsDir"

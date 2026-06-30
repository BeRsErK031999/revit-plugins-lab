param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$trueBimRoot = Join-Path $repoRoot "plugins\truebim"
$solutionPath = Join-Path $repoRoot "TrueBIM.sln"
$dotnetPath = "C:\Program Files\dotnet\dotnet.exe"
$projectOutputDir = Join-Path $trueBimRoot "src\TrueBIM.App\bin\$Configuration\net8.0-windows"
$artifactsDir = Join-Path $trueBimRoot "artifacts"
$coreArtifactsDir = Join-Path $artifactsDir "Core"
$sheetNumberingArtifactsDir = Join-Path $artifactsDir "Modules\SheetNumbering"
$docsArtifactsDir = Join-Path $artifactsDir "Docs"
$assetsArtifactsDir = Join-Path $artifactsDir "Assets"

if (-not (Test-Path $dotnetPath)) {
    throw "Required .NET SDK host was not found at '$dotnetPath'."
}

& $dotnetPath build $solutionPath --configuration $Configuration --nologo --verbosity:minimal

$appAssembly = Join-Path $projectOutputDir "TrueBIM.App.dll"
if (-not (Test-Path $appAssembly)) {
    throw "Build output was not found at '$appAssembly'."
}

if (Test-Path $artifactsDir) {
    Remove-Item -LiteralPath $artifactsDir -Recurse -Force
}

New-Item -ItemType Directory -Path $coreArtifactsDir -Force | Out-Null
New-Item -ItemType Directory -Path $sheetNumberingArtifactsDir -Force | Out-Null
New-Item -ItemType Directory -Path $docsArtifactsDir -Force | Out-Null
New-Item -ItemType Directory -Path $assetsArtifactsDir -Force | Out-Null

Copy-Item -Path (Join-Path $projectOutputDir "TrueBIM.App.dll") -Destination $coreArtifactsDir -Force
Copy-Item -Path (Join-Path $projectOutputDir "TrueBIM.App.pdb") -Destination $coreArtifactsDir -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $projectOutputDir "TrueBIM.App.deps.json") -Destination $coreArtifactsDir -Force -ErrorAction SilentlyContinue

Copy-Item -Path (Join-Path $trueBimRoot "modules\sheet-numbering\module.json") -Destination $sheetNumberingArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "modules\sheet-numbering\README.md") -Destination $sheetNumberingArtifactsDir -Force

Copy-Item -Path (Join-Path $trueBimRoot "README.md") -Destination $docsArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "docs\*.md") -Destination $docsArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "installer\README.md") -Destination (Join-Path $docsArtifactsDir "installer-README.md") -Force

Copy-Item -Path (Join-Path $trueBimRoot "assets\icons") -Destination $assetsArtifactsDir -Recurse -Force

Write-Host "Built TrueBIM artifacts for Revit 2025."
Write-Host "Core: $coreArtifactsDir"
Write-Host "Sheet Numbering: $sheetNumberingArtifactsDir"
Write-Host "Docs: $docsArtifactsDir"
Write-Host "Assets: $assetsArtifactsDir"

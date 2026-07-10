param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [string] $RevitApiRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$repoRootPath = [System.IO.Path]::GetFullPath([string] $repoRoot)
$trueBimRoot = Join-Path $repoRootPath "plugins\truebim"
$projectPath = Join-Path $trueBimRoot "src\TrueBIM.App\TrueBIM.App.csproj"
$buildRoot = Join-Path $trueBimRoot "obj\revit-2026-artifacts"
$projectOutputDir = Join-Path $buildRoot "$Configuration\bin"
$projectObjDir = Join-Path $buildRoot "$Configuration\obj"
$artifactsDir = Join-Path $trueBimRoot "artifacts-2026"
$coreArtifactsDir = Join-Path $artifactsDir "Core"
$modulesArtifactsDir = Join-Path $artifactsDir "Modules"
$docsArtifactsDir = Join-Path $artifactsDir "Docs"
$assetsArtifactsDir = Join-Path $artifactsDir "Assets"

if ([string]::IsNullOrWhiteSpace($RevitApiRoot)) {
    $RevitApiRoot = Join-Path ${env:ProgramFiles} "Autodesk\Revit 2026"
}

$RevitApiRoot = [System.IO.Path]::GetFullPath($RevitApiRoot)
$revitApiPath = Join-Path $RevitApiRoot "RevitAPI.dll"
$revitApiUiPath = Join-Path $RevitApiRoot "RevitAPIUI.dll"
if (-not (Test-Path -LiteralPath $revitApiPath) -or -not (Test-Path -LiteralPath $revitApiUiPath)) {
    throw "Revit 2026 API assemblies were not found under '$RevitApiRoot'."
}

. (Join-Path $PSScriptRoot "resolve-dotnet-sdk.ps1")
$dotnetPath = Resolve-DotNetSdk

function Remove-RepoDirectory {
    param([Parameter(Mandatory = $true)][string] $Path)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $resolvedRoot = $repoRootPath.TrimEnd('\')
    if (-not $resolvedPath.StartsWith($resolvedRoot + "\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove '$resolvedPath' because it is outside '$resolvedRoot'."
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)][string] $Source,
        [Parameter(Mandatory = $true)][string] $Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
}

Remove-RepoDirectory -Path $buildRoot
Remove-RepoDirectory -Path $artifactsDir
New-Item -ItemType Directory -Path $projectOutputDir -Force | Out-Null
New-Item -ItemType Directory -Path $projectObjDir -Force | Out-Null

& $dotnetPath build $projectPath `
    --configuration $Configuration `
    --framework net8.0-windows `
    --nologo `
    --verbosity:minimal `
    "-p:RevitVersion=2026" `
    "-p:RevitApiRoot=$RevitApiRoot" `
    "-p:OutputPath=$projectOutputDir\" `
    "-p:IntermediateOutputPath=$projectObjDir\"
if ($LASTEXITCODE -ne 0) {
    throw "Build failed for Revit 2026."
}

$appAssembly = Join-Path $projectOutputDir "TrueBIM.App.dll"
if (-not (Test-Path -LiteralPath $appAssembly)) {
    throw "Build output was not found at '$appAssembly'."
}

Copy-DirectoryContents -Source $projectOutputDir -Destination $coreArtifactsDir
Copy-DirectoryContents -Source (Join-Path $trueBimRoot "modules\print") -Destination (Join-Path $modulesArtifactsDir "Print")
Copy-DirectoryContents -Source (Join-Path $trueBimRoot "modules\sheet-numbering") -Destination (Join-Path $modulesArtifactsDir "SheetNumbering")
Copy-DirectoryContents -Source (Join-Path $trueBimRoot "modules\schedule-column-collapse") -Destination (Join-Path $modulesArtifactsDir "ScheduleColumnCollapse")
Copy-DirectoryContents -Source (Join-Path $trueBimRoot "assets\icons") -Destination (Join-Path $assetsArtifactsDir "icons")

New-Item -ItemType Directory -Path $docsArtifactsDir -Force | Out-Null
Copy-Item -Path (Join-Path $trueBimRoot "README.md") -Destination $docsArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "docs\*.md") -Destination $docsArtifactsDir -Force
Copy-Item -Path (Join-Path $trueBimRoot "installer\README.md") -Destination (Join-Path $docsArtifactsDir "installer-README.md") -Force
$docsAssetsSourceDir = Join-Path $trueBimRoot "docs\assets"
if (Test-Path -LiteralPath $docsAssetsSourceDir) {
    Copy-DirectoryContents -Source $docsAssetsSourceDir -Destination (Join-Path $docsArtifactsDir "assets")
}

$copiedRevitApi = Get-ChildItem -LiteralPath $artifactsDir -Recurse -File |
    Where-Object { $_.Name -in @("RevitAPI.dll", "RevitAPIUI.dll") }
if ($copiedRevitApi) {
    throw "Revit API assemblies must not be copied into the Revit 2026 artifacts."
}

Write-Host "Built TrueBIM artifacts for Revit 2026."
Write-Host "Artifacts: $artifactsDir"

param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",

    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$solutionPath = Join-Path $repoRoot "TrueBIM.sln"
$projectOutput = Join-Path $repoRoot "plugins\truebim\src\TrueBIM.App\bin\$Configuration\net8.0-windows\TrueBIM.App.dll"
$manifestSource = Join-Path $repoRoot "plugins\truebim\manifests\2025\TrueBIM.addin"
$coreTargetDir = Join-Path $env:APPDATA "TrueBIM\2025\Core"
$addinTargetDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2025"
$dotnetPath = "C:\Program Files\dotnet\dotnet.exe"

if (-not (Test-Path $dotnetPath)) {
    throw "Required .NET SDK host was not found at '$dotnetPath'."
}

if (-not $SkipBuild) {
    & $dotnetPath build $solutionPath --configuration $Configuration --nologo --verbosity:minimal
}

if (-not (Test-Path $projectOutput)) {
    throw "Build output was not found at '$projectOutput'."
}

New-Item -ItemType Directory -Path $coreTargetDir -Force | Out-Null
New-Item -ItemType Directory -Path $addinTargetDir -Force | Out-Null

Copy-Item -Path $projectOutput -Destination $coreTargetDir -Force
Copy-Item -Path $manifestSource -Destination $addinTargetDir -Force

Write-Host "Deployed TrueBIM.App.dll to $coreTargetDir"
Write-Host "Deployed TrueBIM.addin to $addinTargetDir"

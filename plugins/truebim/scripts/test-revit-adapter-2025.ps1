param(
    [string] $FamilyDirectory = (Join-Path $env:USERPROFILE "Desktop\Revit\Армирование\Семейства"),

    [string] $Filter = "FullyQualifiedName~IsoFieldFamilyInspectionTests"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$projectPath = Join-Path $repoRoot "plugins\truebim\tests\TrueBIM.Revit.Tests\TrueBIM.Revit.Tests.csproj"
$reportDirectory = Join-Path $repoRoot "plugins\truebim\test-results\revit-2025-adapter"

. (Join-Path $PSScriptRoot "resolve-dotnet-sdk.ps1")
$dotnetPath = Resolve-DotNetSdk

if (-not (Test-Path -LiteralPath $FamilyDirectory)) {
    throw "IsoField family directory was not found at '$FamilyDirectory'."
}

$revitExecutable = "C:\Program Files\Autodesk\Revit 2025\Revit.exe"
if (-not (Test-Path -LiteralPath $revitExecutable)) {
    throw "Revit 2025 was not found at '$revitExecutable'."
}

New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$environmentVariables = @(
    "TRUEBIM_ISOFIELD_FAMILY_DIR",
    "TRUEBIM_REVIT_TEST_REPORT_DIR"
)
$previousEnvironment = @{}
foreach ($variableName in $environmentVariables) {
    $previousEnvironment[$variableName] = [Environment]::GetEnvironmentVariable(
        $variableName,
        [EnvironmentVariableTarget]::Process)
}

try {
    $env:TRUEBIM_ISOFIELD_FAMILY_DIR = [System.IO.Path]::GetFullPath($FamilyDirectory)
    $env:TRUEBIM_REVIT_TEST_REPORT_DIR = [System.IO.Path]::GetFullPath($reportDirectory)

    Write-Host "Revit opens in read-only Viewer Mode for this family contract test."
    Write-Host "ricaun.RevitTest requires an Autodesk user already signed in to Revit 2025."
    Write-Host "On the first run Revit can ask whether the signed ricaun.RevitTest add-in is trusted."
    Write-Host "Use test-revit-2025.ps1 instead when Autodesk sign-in is unavailable."

    & $dotnetPath test $projectPath `
        --configuration Release `
        --nologo `
        --verbosity:minimal `
        --filter $Filter `
        --results-directory $reportDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Revit 2025 integration tests failed."
    }

    Write-Host "Revit 2025 integration tests completed."
    Write-Host "Results: $reportDirectory"
}
finally {
    foreach ($variableName in $environmentVariables) {
        [Environment]::SetEnvironmentVariable(
            $variableName,
            $previousEnvironment[$variableName],
            [EnvironmentVariableTarget]::Process)
    }
}

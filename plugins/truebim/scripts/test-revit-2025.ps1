param(
    [string] $FamilyDirectory = (Join-Path $env:USERPROFILE "Desktop\Revit\Армирование\Семейства"),

    [ValidateRange(30, 600)]
    [int] $TimeoutSeconds = 180,

    [switch] $KeepRevitOpen
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$projectPath = Join-Path $repoRoot "plugins\truebim\tests\TrueBIM.Revit.FamilyHarness\TrueBIM.Revit.FamilyHarness.csproj"
$reportDirectory = Join-Path $repoRoot "plugins\truebim\test-results\revit-2025"

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
$reportPath = Join-Path $reportDirectory "isofield-family-contract.json"
$runId = [Guid]::NewGuid().ToString("N")
$manifestDirectory = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2025"
$manifestPath = Join-Path $manifestDirectory "TrueBIM.FamilyContractHarness.$runId.addin"
$environmentVariables = @(
    "TRUEBIM_ISOFIELD_FAMILY_DIR",
    "TRUEBIM_REVIT_HARNESS_REPORT_PATH",
    "TRUEBIM_REVIT_HARNESS_RUN_ID"
)
$previousEnvironment = @{}
foreach ($variableName in $environmentVariables) {
    $previousEnvironment[$variableName] = [Environment]::GetEnvironmentVariable(
        $variableName,
        [EnvironmentVariableTarget]::Process)
}

& $dotnetPath build $projectPath --configuration Release --nologo --verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "The Revit 2025 family contract harness failed to build."
}

$assemblyPath = Join-Path `
    (Split-Path -Parent $projectPath) `
    "bin\Release\net8.0-windows\TrueBIM.Revit.FamilyHarness.dll"
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "The harness assembly was not found at '$assemblyPath'."
}

$existingRevit = Get-Process -Name "Revit" -ErrorAction SilentlyContinue | Where-Object {
    try {
        [string]::Equals($_.Path, $revitExecutable, [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        $false
    }
}
if ($existingRevit) {
    $processIds = ($existingRevit.Id | Sort-Object) -join ", "
    throw "Close Revit 2025 before this isolated test. Existing process IDs: $processIds."
}

New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
if (Test-Path -LiteralPath $manifestPath) {
    throw "Refusing to overwrite the existing manifest '$manifestPath'."
}

$escapedAssemblyPath = [System.Security.SecurityElement]::Escape($assemblyPath)
$manifestContent = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>TrueBIM Family Contract Harness</Name>
    <Assembly>$escapedAssemblyPath</Assembly>
    <AddInId>4C9A9250-87B7-4E47-B91B-C3797E055A90</AddInId>
    <FullClassName>TrueBIM.Revit.FamilyHarness.FamilyContractHarnessApplication</FullClassName>
    <VendorId>TrueBIM</VendorId>
    <VendorDescription>TrueBIM local read-only family contract test</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

$process = $null
try {
    [System.IO.File]::WriteAllText(
        $manifestPath,
        $manifestContent,
        [System.Text.UTF8Encoding]::new($false))
    Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

    $env:TRUEBIM_ISOFIELD_FAMILY_DIR = [System.IO.Path]::GetFullPath($FamilyDirectory)
    $env:TRUEBIM_REVIT_HARNESS_REPORT_PATH = [System.IO.Path]::GetFullPath($reportPath)
    $env:TRUEBIM_REVIT_HARNESS_RUN_ID = $runId

    Write-Host "Starting an isolated Revit 2025 Viewer process; Autodesk sign-in is not required."
    Write-Host "The harness opens every supplied family read-only and never saves it."
    Write-Host "A first-run Revit add-in security prompt can require choosing Load Once manually."
    $process = Start-Process -FilePath $revitExecutable -ArgumentList "/viewer" -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $reportPath)) {
        if ($process.HasExited) {
            break
        }

        Start-Sleep -Seconds 1
        $process.Refresh()
    }

    if (-not (Test-Path -LiteralPath $reportPath)) {
        if ($process.HasExited) {
            throw "Revit 2025 exited before the family contract report was written."
        }

        throw "Timed out after $TimeoutSeconds seconds waiting for the family contract report."
    }

    $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    if ($report.RunId -ne $runId) {
        throw "The report run ID does not match the current harness run."
    }

    if (-not $report.Succeeded) {
        $details = @($report.ContractErrors) + @($report.FatalError) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        throw "Family contract validation failed: $($details -join ' | ')"
    }

    Write-Host "Revit 2025 family contract passed for $($report.Families.Count) supplied families."
    Write-Host "Results: $reportPath"
}
finally {
    if (-not $KeepRevitOpen -and $process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(20000)) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
    }

    Remove-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue
    foreach ($variableName in $environmentVariables) {
        [Environment]::SetEnvironmentVariable(
            $variableName,
            $previousEnvironment[$variableName],
            [EnvironmentVariableTarget]::Process)
    }
}

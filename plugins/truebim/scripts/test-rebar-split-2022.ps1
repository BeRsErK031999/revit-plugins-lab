param(
    [ValidateRange(30, 1800)][int] $TimeoutSeconds = 120,
    [switch] $AllowExistingRevit,
    [switch] $ShowViewer,
    [string] $ModelPath = '',
    [switch] $SkipAvailabilityCheck,
    [switch] $ProtectionOnly,
    [switch] $Comprehensive,
    [ValidateSet('', 'NestedGroup', 'RebarOnlyGroup', 'RotatedRepeatedGroup', 'NestedRepeatedGroup',
        'MultiSetRepeatedGroup', 'MirroredRepeatedGroup', 'PinnedMemberRepeatedGroup', 'ParameterIssues', 'GroupExtraction', 'SharedGroupsProbe')][string] $ProtectionScenario = ''
)

$ErrorActionPreference = 'Stop'
if ($ProtectionScenario) { $ProtectionOnly = $true }
if ($ProtectionOnly -and -not [string]::IsNullOrWhiteSpace($ModelPath)) {
    throw 'ProtectionOnly uses a temporary fixture and cannot be combined with ModelPath.'
}
if ($Comprehensive -and -not [string]::IsNullOrWhiteSpace($ModelPath)) {
    throw 'Run Comprehensive and ModelPath separately: repeated-group fixtures require an active test document.'
}
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$harnessRoot = Join-Path $repoRoot 'plugins\truebim\tests\TrueBIM.Revit.RebarSplitHarness'
$reportRoot = Join-Path $repoRoot 'plugins\truebim\test-results\rebar-split'
if (-not $AllowExistingRevit -and (Get-Process -Name Revit -ErrorAction SilentlyContinue)) {
    throw 'Close Revit or use -AllowExistingRevit to start a separate, isolated Viewer.'
}
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
$rebarDotnet = Resolve-DotNetSdk
& $rebarDotnet build (Join-Path $harnessRoot 'TrueBIM.Revit.RebarSplitHarness.csproj') -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false --nologo --verbosity:quiet
if ($LASTEXITCODE -ne 0) { throw 'Rebar split harness build failed.' }

$runId = [Guid]::NewGuid().ToString('N')
$manifestDirectory = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2022'
$manifestPath = Join-Path $manifestDirectory ('TrueBIM.RebarSplitHarness.' + $runId + '.addin')
$reportPath = Join-Path $reportRoot ('revit-2022-' + $runId + '.json')
$assembly = [Security.SecurityElement]::Escape((Join-Path $harnessRoot 'bin\Release\net48\TrueBIM.Revit.RebarSplitHarness.dll'))
$manifest = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>TrueBIM Rebar Split Regression</Name><Assembly>' + $assembly + '</Assembly><AddInId>BF262416-23CC-4A48-A08F-0C7A92D1660E</AddInId><FullClassName>TrueBIM.Revit.RebarSplitHarness.RebarSplitHarnessApplication</FullClassName><VendorId>TRBM</VendorId><VendorDescription>TrueBIM isolated regression test</VendorDescription></AddIn></RevitAddIns>'
$previousReportVariable = $env:TRUEBIM_REBAR_SPLIT_HARNESS_REPORT
$previousModelVariable = $env:TRUEBIM_REBAR_SPLIT_HARNESS_MODEL
$previousAvailabilityVariable = $env:TRUEBIM_REBAR_SPLIT_SKIP_AVAILABILITY
$previousProtectionVariable = $env:TRUEBIM_REBAR_SPLIT_PROTECTION_ONLY
$previousScenarioVariable = $env:TRUEBIM_REBAR_SPLIT_PROTECTION_SCENARIO
$previousComprehensiveVariable = $env:TRUEBIM_REBAR_SPLIT_COMPREHENSIVE
$viewerProcess = $null
try {
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
    $env:TRUEBIM_REBAR_SPLIT_HARNESS_REPORT = $reportPath
    $env:TRUEBIM_REBAR_SPLIT_HARNESS_MODEL = $null
    $env:TRUEBIM_REBAR_SPLIT_SKIP_AVAILABILITY = if ($SkipAvailabilityCheck) { '1' } else { $null }
    $env:TRUEBIM_REBAR_SPLIT_PROTECTION_ONLY = if ($ProtectionOnly) { '1' } else { $null }
    $env:TRUEBIM_REBAR_SPLIT_PROTECTION_SCENARIO = $ProtectionScenario
    $env:TRUEBIM_REBAR_SPLIT_COMPREHENSIVE = if ($Comprehensive) { '1' } else { $null }
    if (-not [string]::IsNullOrWhiteSpace($ModelPath)) {
        $inputDirectory = Join-Path $reportRoot "inputs\$runId"
        New-Item -ItemType Directory -Path $inputDirectory -Force | Out-Null
        $modelCopy = Join-Path $inputDirectory ([IO.Path]::GetFileName($ModelPath))
        Copy-Item -LiteralPath $ModelPath -Destination $modelCopy
        $env:TRUEBIM_REBAR_SPLIT_HARNESS_MODEL = $modelCopy
    }
    $windowStyle = if ($ShowViewer) { 'Normal' } else { 'Hidden' }
    $viewerProcess = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2022\Revit.exe' -ArgumentList '/viewer' -WindowStyle $windowStyle -PassThru
    [pscustomobject]@{ ProcessId = $viewerProcess.Id; Manifest = $manifestPath; Report = $reportPath } |
        ConvertTo-Json | Set-Content (Join-Path $reportRoot 'active-harness.json')
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $reportPath)) {
        if ($viewerProcess.HasExited) { break }
        Start-Sleep -Milliseconds 500
        $viewerProcess.Refresh()
    }
    if (-not (Test-Path -LiteralPath $reportPath)) { throw "Revit did not produce a report within $TimeoutSeconds seconds. Check Viewer initialization/add-in approval." }
    $report = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json
    Write-Output "Report: $reportPath"
    $report.Scenarios | Select-Object Scenario, Passed, SetCount, Bars | Format-Table -AutoSize
    if ($report.FatalError) { throw $report.FatalError }
    $expectedScenarios = if ($Comprehensive) { 176 } elseif ($ProtectionScenario -eq 'SharedGroupsProbe') { 12 } elseif ($ProtectionScenario -eq 'GroupExtraction') { 10 } elseif ($ProtectionScenario -eq 'ParameterIssues') { 12 } elseif ($ProtectionScenario) { 8 } elseif ($ProtectionOnly) { 140 } elseif ([string]::IsNullOrWhiteSpace($ModelPath)) { 140 } else { 36 }
    if (@($report.Scenarios).Count -ne $expectedScenarios -or @($report.Scenarios | Where-Object { -not $_.Passed }).Count -gt 0) {
        throw "Regression failed. See $reportPath."
    }
    if (-not [string]::IsNullOrWhiteSpace($ModelPath) -and -not $SkipAvailabilityCheck) {
        $report.Availability | Format-Table -AutoSize
        if (@($report.Availability | Where-Object { $_.SelectedCount -eq 0 -and $_.FixedCondition }).Count -eq 0 -or
            @($report.Availability | Where-Object { $_.SelectedCount -gt 0 -and $_.FixedCondition }).Count -eq 0) {
            throw "Command availability regression failed. See $reportPath."
        }
    }
}
finally {
    if ($viewerProcess -and -not $viewerProcess.HasExited) { Stop-Process -Id $viewerProcess.Id -Force }
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
    $env:TRUEBIM_REBAR_SPLIT_HARNESS_REPORT = $previousReportVariable
    $env:TRUEBIM_REBAR_SPLIT_HARNESS_MODEL = $previousModelVariable
    $env:TRUEBIM_REBAR_SPLIT_SKIP_AVAILABILITY = $previousAvailabilityVariable
    $env:TRUEBIM_REBAR_SPLIT_PROTECTION_ONLY = $previousProtectionVariable
    $env:TRUEBIM_REBAR_SPLIT_PROTECTION_SCENARIO = $previousScenarioVariable
    $env:TRUEBIM_REBAR_SPLIT_COMPREHENSIVE = $previousComprehensiveVariable
}

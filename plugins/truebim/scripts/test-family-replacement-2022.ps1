param([ValidateRange(30, 300)][int] $TimeoutSeconds = 120, [switch] $AllowExistingRevit, [switch] $ShowViewer)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$harnessRoot = Join-Path $repoRoot 'plugins\truebim\tests\TrueBIM.Revit.ReplacementHarness'
$reportRoot = Join-Path $repoRoot 'plugins\truebim\test-results\family-replacement'
$revitPath = 'C:\Program Files\Autodesk\Revit 2022\Revit.exe'
if (-not $AllowExistingRevit -and (Get-Process -Name Revit -ErrorAction SilentlyContinue)) {
    throw 'Close Revit before starting this isolated regression test.'
}
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
$replacementDotnet = Resolve-DotNetSdk
& $replacementDotnet build (Join-Path $harnessRoot 'TrueBIM.Revit.ReplacementHarness.csproj') -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false --nologo --verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw 'Regression harness build failed.' }

$runId = [Guid]::NewGuid().ToString('N')
$manifestDirectory = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2022'
$manifestPath = Join-Path $manifestDirectory ('TrueBIM.ReplacementHarness.' + $runId + '.addin')
$reportPath = Join-Path $reportRoot ('revit-2022-' + $runId + '.json')
$assembly = [Security.SecurityElement]::Escape((Join-Path $harnessRoot 'bin\Release\net48\TrueBIM.Revit.ReplacementHarness.dll'))
$manifest = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>TrueBIM Replacement Regression</Name><Assembly>' + $assembly + '</Assembly><AddInId>0B7EEBA5-D3EE-453D-BFE1-FF0AC27A4D51</AddInId><FullClassName>TrueBIM.Revit.ReplacementHarness.ReplacementHarnessApplication</FullClassName><VendorId>TRBM</VendorId><VendorDescription>TrueBIM isolated regression test</VendorDescription></AddIn></RevitAddIns>'
$previousReportVariable = $env:TRUEBIM_REPLACEMENT_HARNESS_REPORT
$regressionProcess = $null
try {
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
    $env:TRUEBIM_REPLACEMENT_HARNESS_REPORT = $reportPath
    $viewerWindowStyle = if ($ShowViewer) { 'Normal' } else { 'Hidden' }
    $regressionProcess = Start-Process -FilePath $revitPath -ArgumentList '/viewer' -WindowStyle $viewerWindowStyle -PassThru
    [pscustomobject]@{ProcessId=$regressionProcess.Id;Manifest=$manifestPath;Report=$reportPath;Started=(Get-Date).ToString('o')} |
        ConvertTo-Json | Set-Content (Join-Path $reportRoot 'active-harness.json')
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $harnessStarted = $false
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $reportPath)) {
        if ($regressionProcess.HasExited) { break }
        if (-not $harnessStarted -and (Test-Path -LiteralPath ($reportPath + '.progress.txt'))) {
            # Give execution its full budget after manual add-in approval/startup.
            $harnessStarted = $true
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        }
        Start-Sleep -Milliseconds 500
        $regressionProcess.Refresh()
    }
    if (-not (Test-Path -LiteralPath $reportPath)) { throw "Revit did not produce a report within $TimeoutSeconds seconds." }
    $report = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json
    Write-Output "Report: $reportPath"
    $report.Scenarios | Select-Object Scenario,Total,Replaced,Skipped | Format-Table -AutoSize
    if ($report.FatalError) { throw $report.FatalError }
    $expected = @{
        SingleSelected = 0; MultipleSelected = 2; SingleUnselected = 1; SingleSelectionFixed = 1
        RotatedInsertion = 3; MirroredCenter = 3; OverlapStrict = 0; OverlapIgnored = 1
        CommitMovementRollback = 0; MixedWarningsStrict = 0; MixedWarningsIgnored = 0
    }
    foreach ($name in $expected.Keys) {
        $scenarioResults = @($report.Scenarios | Where-Object { $_.Scenario -eq $name })
        if ($scenarioResults.Count -ne 1 -or $scenarioResults[0].Replaced -ne $expected[$name]) {
            throw "Unexpected result in regression scenario '$name'. See $reportPath."
        }
    }
    $originalFailure = @($report.Scenarios | Where-Object { $_.Scenario -eq 'SingleSelected' })[0]
    if ($originalFailure.Skipped -ne 1 -or $originalFailure.Items[0].Message -notmatch 'дополнительные элементы') {
        throw 'The original cascade-deletion failure was not reproduced.'
    }
}
finally {
    # Only this script's fresh viewer process is stopped; no document is saved.
    if ($regressionProcess -and -not $regressionProcess.HasExited) { Stop-Process -Id $regressionProcess.Id -Force }
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
    $env:TRUEBIM_REPLACEMENT_HARNESS_REPORT = $previousReportVariable
}

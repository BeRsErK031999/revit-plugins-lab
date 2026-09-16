param(
    [Parameter(Mandatory)][string] $ModelPath,
    [ValidateRange(60, 1800)][int] $TimeoutSeconds = 600,
    [switch] $AllowRunningRevit
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$harnessRoot = Join-Path $repoRoot 'plugins\truebim\tests\TrueBIM.Revit.FinishScheduleHarness'
$reportRoot = Join-Path $repoRoot 'plugins\truebim\test-results\finish-model'
if (-not $AllowRunningRevit -and (Get-Process -Name Revit -ErrorAction SilentlyContinue)) {
    throw 'Revit is already running. Use -AllowRunningRevit to keep it open and launch a separate test process.'
}
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
$finishDotnet = Resolve-DotNetSdk
& $finishDotnet build (Join-Path $harnessRoot 'TrueBIM.Revit.FinishScheduleHarness.csproj') -c Release -p:RevitVersion=2023 -m:1 -nodeReuse:false -p:UseSharedCompilation=false --nologo --verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw 'Model harness build failed.' }
$runId = [Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$runRoot = Join-Path $reportRoot $runId
New-Item -ItemType Directory -Path $runRoot | Out-Null
$copyPath = Join-Path $runRoot 'model-copy.rvt'
Copy-Item -LiteralPath $ModelPath -Destination $copyPath
$manifestDirectory = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2023'
$manifestPath = Join-Path $manifestDirectory ('TrueBIM.FinishHarness.' + $runId + '.addin')
$reportPath = Join-Path $runRoot 'report.json'
$assembly = [Security.SecurityElement]::Escape((Join-Path $harnessRoot 'bin\Release\net48\TrueBIM.Revit.FinishScheduleHarness.dll'))
$manifest = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>TrueBIM Finish Regression</Name><Assembly>' + $assembly + '</Assembly><AddInId>96CA279B-0CB2-4D58-90F6-FEF27B615690</AddInId><FullClassName>TrueBIM.Revit.FinishScheduleHarness.FinishScheduleHarnessApplication</FullClassName><VendorId>TRBM</VendorId><VendorDescription>TrueBIM isolated regression test</VendorDescription></AddIn></RevitAddIns>'
$previousReport = $env:TRUEBIM_FINISH_HARNESS_REPORT
$previousModel = $env:TRUEBIM_FINISH_HARNESS_MODEL
$regressionProcess = $null
try {
    [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
    $env:TRUEBIM_FINISH_HARNESS_REPORT = $reportPath
    $env:TRUEBIM_FINISH_HARNESS_MODEL = $copyPath
    $regressionProcess = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2023\Revit.exe' -ArgumentList '/viewer /language ENU' -WindowStyle Hidden -PassThru
    [pscustomobject]@{ ProcessId=$regressionProcess.Id; Manifest=$manifestPath; Report=$reportPath; Copy=$copyPath } |
        ConvertTo-Json | Set-Content (Join-Path $reportRoot 'active-harness.json')
    Write-Output "Revit process $($regressionProcess.Id); report $reportPath"
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $reportPath)) {
        if ($regressionProcess.HasExited) { break }
        Start-Sleep -Milliseconds 500
        $regressionProcess.Refresh()
    }
    if (-not (Test-Path -LiteralPath $reportPath)) { throw "Revit did not produce a report within $TimeoutSeconds seconds." }
    $report = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json
    Write-Output "Report: $reportPath"
    $report.Passed | Write-Output
    if ($report.FatalError) { throw $report.FatalError }
}
finally {
    if ($regressionProcess -and -not $regressionProcess.HasExited) { Stop-Process -Id $regressionProcess.Id -Force }
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
    $env:TRUEBIM_FINISH_HARNESS_REPORT = $previousReport
    $env:TRUEBIM_FINISH_HARNESS_MODEL = $previousModel
}

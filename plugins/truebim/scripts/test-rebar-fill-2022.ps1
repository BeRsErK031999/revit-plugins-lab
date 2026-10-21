param(
    [Parameter(Mandatory = $true)][string] $ModelPath,
    [ValidateRange(30, 1800)][int] $TimeoutSeconds = 900,
    [switch] $Regression
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$harnessRoot = Join-Path $repoRoot 'plugins\truebim\tests\TrueBIM.Revit.RebarFillHarness'
$reportRoot = Join-Path $repoRoot 'plugins\truebim\test-results\rebar-fill'
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
$fillDotnet = Resolve-DotNetSdk
& $fillDotnet build (Join-Path $harnessRoot 'TrueBIM.Revit.RebarFillHarness.csproj') -c Release --nologo --verbosity:quiet -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Rebar fill harness build failed.' }
$runId = [Guid]::NewGuid().ToString('N')
$runRoot = Join-Path $reportRoot $runId
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$modelCopy = Join-Path $runRoot 'model-copy.rvt'
$originalHash = (Get-FileHash -LiteralPath $ModelPath).Hash
Copy-Item -LiteralPath $ModelPath -Destination $modelCopy
$manifestPath = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2022\TrueBIM.RebarFillHarness.$runId.addin"
$assembly = [Security.SecurityElement]::Escape((Join-Path $harnessRoot 'bin\Release\net48\TrueBIM.Revit.RebarFillHarness.dll'))
$manifest = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>TrueBIM Rebar Fill Audit</Name><Assembly>' + $assembly + '</Assembly><AddInId>16DDCB03-6509-4C5C-80A4-B379123D3F61</AddInId><FullClassName>TrueBIM.Revit.RebarFillHarness.RebarFillHarnessApplication</FullClassName><VendorId>TRBM</VendorId></AddIn></RevitAddIns>'
$previousReport = $env:TRUEBIM_REBAR_FILL_REPORT
$previousModel = $env:TRUEBIM_REBAR_FILL_MODEL
$previousRegression = $env:TRUEBIM_REBAR_FILL_REGRESSION
$viewerProcess = $null
try {
    [IO.File]::WriteAllText($manifestPath, $manifest)
    $env:TRUEBIM_REBAR_FILL_REPORT = Join-Path $runRoot 'report.json'
    $env:TRUEBIM_REBAR_FILL_MODEL = $modelCopy
    $env:TRUEBIM_REBAR_FILL_REGRESSION = if ($Regression) { '1' } else { $null }
    $viewerProcess = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2022\Revit.exe' -ArgumentList '/viewer' -WindowStyle Hidden -PassThru
    [pscustomobject]@{ ProcessId = $viewerProcess.Id; Report = $env:TRUEBIM_REBAR_FILL_REPORT; Manifest = $manifestPath; OriginalSHA256 = $originalHash } | ConvertTo-Json | Set-Content (Join-Path $reportRoot 'active-harness.json')
    Write-Output "Process: $($viewerProcess.Id); report: $env:TRUEBIM_REBAR_FILL_REPORT"
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $env:TRUEBIM_REBAR_FILL_REPORT)) {
        if ($viewerProcess.HasExited) { break }
        Start-Sleep -Milliseconds 500
        $viewerProcess.Refresh()
    }
    if (-not (Test-Path -LiteralPath $env:TRUEBIM_REBAR_FILL_REPORT)) { throw 'Revit did not produce a report; check startup dialogs.' }
    $report = Get-Content -LiteralPath $env:TRUEBIM_REBAR_FILL_REPORT -Raw | ConvertFrom-Json
    if ($report.FatalError) { throw $report.FatalError }
    if ($Regression) {
        $report.Scenarios | Select-Object Scenario, Passed, Error | Format-Table -Wrap
        if (@($report.Scenarios).Count -ne 5 -or @($report.Scenarios | Where-Object { -not $_.Passed }).Count -gt 0) { throw "Regression failed: $env:TRUEBIM_REBAR_FILL_REPORT" }
    }
    Write-Output "Report: $env:TRUEBIM_REBAR_FILL_REPORT"
}
finally {
    if ($viewerProcess -and -not $viewerProcess.HasExited) { Stop-Process -Id $viewerProcess.Id -Force }
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
    $env:TRUEBIM_REBAR_FILL_REPORT = $previousReport
    $env:TRUEBIM_REBAR_FILL_MODEL = $previousModel
    $env:TRUEBIM_REBAR_FILL_REGRESSION = $previousRegression
    if ((Get-FileHash -LiteralPath $ModelPath).Hash -ne $originalHash) { throw 'Original model hash changed.' }
}

param(
    [ValidateRange(30, 300)][int] $TimeoutSeconds = 180,
    [switch] $TrustTestAddinForThisRun,
    [switch] $IsolateKukiForThisRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$harnessRoot = Join-Path $repoRoot 'plugins\truebim\tests\TrueBIM.Revit.FinishScheduleHarness'
$reportRoot = Join-Path $repoRoot 'plugins\truebim\test-results\finish-schedule'
$revitPath = 'C:\Program Files\Autodesk\Revit 2022\Revit.exe'
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Close Revit before starting this isolated regression test.'
}
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
$finishDotnet = Resolve-DotNetSdk
& $finishDotnet build (Join-Path $harnessRoot 'TrueBIM.Revit.FinishScheduleHarness.csproj') -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false --nologo --verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw 'Regression harness build failed.' }

$runId = [Guid]::NewGuid().ToString('N')
$manifestDirectory = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2022'
$manifestPath = Join-Path $manifestDirectory ('TrueBIM.FinishHarness.' + $runId + '.addin')
$reportPath = Join-Path $reportRoot ('revit-2022-' + $runId + '.json')
$assembly = [Security.SecurityElement]::Escape((Join-Path $harnessRoot 'bin\Release\net48\TrueBIM.Revit.FinishScheduleHarness.dll'))
$manifest = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>TrueBIM Finish Regression</Name><Assembly>' + $assembly + '</Assembly><AddInId>96CA279B-0CB2-4D58-90F6-FEF27B615690</AddInId><FullClassName>TrueBIM.Revit.FinishScheduleHarness.FinishScheduleHarnessApplication</FullClassName><VendorId>TRBM</VendorId><VendorDescription>TrueBIM isolated regression test</VendorDescription></AddIn></RevitAddIns>'
$previousReportVariable = $env:TRUEBIM_FINISH_HARNESS_REPORT
$regressionProcess = $null
$trustKey = 'HKCU:\Software\Autodesk\Revit\Autodesk Revit 2022\CodeSigning'
$trustName = '96ca279b-0cb2-4d58-90f6-fef27b615690'
$trustCreated = $false
$kukiOverridePath = Join-Path $manifestDirectory 'Kukai.2022.addin'
$kukiOverrideCreated = $false
try {
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
    if ($IsolateKukiForThisRun) {
        if (Test-Path -LiteralPath $kukiOverridePath) { throw 'A user KUKI manifest already exists; refusing to replace it.' }
        # A user manifest with the same name overrides ProgramData only for this isolated run.
        [IO.File]::WriteAllText($kukiOverridePath, '<RevitAddIns />', [Text.UTF8Encoding]::new($false))
        $kukiOverrideCreated = $true
    }
    if ($TrustTestAddinForThisRun) {
        # Trust only this source-built fixture, for this process lifetime. Never alter global security.
        $trustRegistry = Get-Item -LiteralPath $trustKey
        if ($trustRegistry.GetValueNames() -contains $trustName) { throw 'The test add-in already has a trust preference; refusing to replace it.' }
        New-ItemProperty -LiteralPath $trustKey -Name $trustName -Value 1 -PropertyType DWord | Out-Null
        $trustCreated = $true
    }
    $env:TRUEBIM_FINISH_HARNESS_REPORT = $reportPath
    $regressionProcess = Start-Process -FilePath $revitPath -ArgumentList '/viewer /language ENU' -WindowStyle Hidden -PassThru
    [pscustomobject]@{ProcessId=$regressionProcess.Id;Manifest=$manifestPath;Report=$reportPath;Started=(Get-Date).ToString('o')} |
        ConvertTo-Json | Set-Content (Join-Path $reportRoot 'active-harness.json')
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $reportPath)) {
        if ($regressionProcess.HasExited) { break }
        Start-Sleep -Milliseconds 500
        $regressionProcess.Refresh()
    }
    if (-not (Test-Path -LiteralPath $reportPath)) { throw "Revit did not produce a report within $TimeoutSeconds seconds." }
    $report = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json
    Write-Output "Report: $reportPath"
    $report.Passed | ForEach-Object { Write-Output "PASS: $_" }
    if ($report.FatalError) { throw $report.FatalError }
    if (@($report.Passed).Count -lt 16) { throw 'Incomplete regression report.' }
}
finally {
    # Stop only this script's fresh viewer process. No project is saved.
    if ($regressionProcess -and -not $regressionProcess.HasExited) { Stop-Process -Id $regressionProcess.Id -Force }
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
    if ($kukiOverrideCreated) { Remove-Item -LiteralPath $kukiOverridePath -Force }
    if ($trustCreated) { Remove-ItemProperty -LiteralPath $trustKey -Name $trustName }
    $env:TRUEBIM_FINISH_HARNESS_REPORT = $previousReportVariable
}

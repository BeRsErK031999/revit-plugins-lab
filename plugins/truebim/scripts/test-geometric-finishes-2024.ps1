param(
    [string] $SourceDirectory = 'C:\Users\Borodin_A\Desktop\Revit\Отделка (полы,потолки, стены)',
    [ValidateSet('audit','acceptance','ui')][string] $Mode = 'audit',
    [int] $TimeoutSeconds = 900,
    [switch] $KeepOpen
)
$ErrorActionPreference = 'Stop'
$finishRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$finishHarness = Join-Path $finishRepo 'plugins\truebim\tests\TrueBIM.Revit.GeometricFinishesHarness'
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
& (Resolve-DotNetSdk) build (Join-Path $finishHarness 'TrueBIM.Revit.GeometricFinishesHarness.csproj') -c Release --nologo -v:q -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Geometric finishes harness build failed.' }
$finishRunId = [Guid]::NewGuid().ToString('N')
$finishRun = Join-Path $finishRepo "tmp\geometric-finishes\$finishRunId"
New-Item -ItemType Directory -Path (Join-Path $finishRun 'models') -Force | Out-Null
$finishSources = @(Get-ChildItem -LiteralPath $SourceDirectory -File -Filter '*.rvt')
$finishHashes = @{}
foreach ($finishSource in $finishSources) {
    $finishHashes[$finishSource.FullName] = (Get-FileHash -LiteralPath $finishSource.FullName -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $finishSource.FullName -Destination (Join-Path $finishRun 'models')
}
$finishManifest = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\TrueBIM.GeometricFinishesHarness.$finishRunId.addin"
$finishAssembly = [Security.SecurityElement]::Escape((Join-Path $finishHarness 'bin\Release\net48\TrueBIM.Revit.GeometricFinishesHarness.dll'))
$finishXml = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>TrueBIM Geometric Finishes QA</Name><Assembly>' + $finishAssembly + '</Assembly><AddInId>72D39CB2-DBE2-4BA5-A4A5-85D156022B61</AddInId><FullClassName>TrueBIM.Revit.GeometricFinishesHarness.FinishesHarnessApplication</FullClassName><VendorId>TRBM</VendorId></AddIn></RevitAddIns>'
$finishOldRoot = $env:TRUEBIM_FINISHES_QA
$finishOldMode = $env:TRUEBIM_FINISHES_MODE
$finishProcess = $null
try {
    [IO.File]::WriteAllText($finishManifest, $finishXml)
    $env:TRUEBIM_FINISHES_QA = $finishRun
    $env:TRUEBIM_FINISHES_MODE = $Mode
    $finishProcess = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2024\Revit.exe' -ArgumentList '/nosplash' -WindowStyle Hidden -PassThru
    [pscustomobject]@{ ProcessId=$finishProcess.Id; RunRoot=$finishRun; Manifest=$finishManifest; SourceHashes=$finishHashes } | ConvertTo-Json | Set-Content (Join-Path $finishRepo 'tmp\geometric-finishes\active.json')
    Write-Output "Process: $($finishProcess.Id); run: $finishRun"
    $finishDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $finishDeadline -and -not (Test-Path (Join-Path $finishRun 'done')) -and -not (Test-Path (Join-Path $finishRun 'failed'))) {
        if ($finishProcess.HasExited) { break }
        Start-Sleep -Milliseconds 500
        $finishProcess.Refresh()
    }
    if (Test-Path (Join-Path $finishRun 'failed')) { throw (Get-Content (Join-Path $finishRun 'failed') -Raw) }
    if (-not (Test-Path (Join-Path $finishRun 'done'))) { throw 'Revit did not complete the test; inspect startup dialogs.' }
    if ($Mode -ne 'audit') {
        $reports = if ($Mode -eq 'ui') { @('ui.json') } else { @('acceptance.json', 'fixtures.json', 'ui.json') }
        foreach ($report in $reports) {
            $cases = Get-Content -LiteralPath (Join-Path $finishRun $report) -Raw | ConvertFrom-Json
            if (@($cases | Where-Object { $_.Passed -ne $true }).Count -gt 0) { throw "Acceptance failed: $report in $finishRun" }
        }
    }
    Write-Output "Completed: $finishRun"
}
finally {
    if (-not $KeepOpen -and $finishProcess -and -not $finishProcess.HasExited) { Stop-Process -Id $finishProcess.Id -Force }
    if (Test-Path -LiteralPath $finishManifest) { Remove-Item -LiteralPath $finishManifest -Force }
    $env:TRUEBIM_FINISHES_QA = $finishOldRoot
    $env:TRUEBIM_FINISHES_MODE = $finishOldMode
    foreach ($finishSource in $finishSources) {
        if ((Get-FileHash -LiteralPath $finishSource.FullName -Algorithm SHA256).Hash -ne $finishHashes[$finishSource.FullName]) { throw "Original changed: $($finishSource.FullName)" }
    }
}

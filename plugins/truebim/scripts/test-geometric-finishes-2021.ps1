param([int] $TimeoutSeconds = 600)
$ErrorActionPreference = 'Stop'
$finishRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$finishHarness = Join-Path $finishRepo 'plugins\truebim\tests\TrueBIM.Revit.GeometricFinishesLegacyHarness'
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
& (Resolve-DotNetSdk) build (Join-Path $finishHarness 'TrueBIM.Revit.GeometricFinishesLegacyHarness.csproj') -c Release --nologo -v:q -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Legacy finish harness build failed.' }
$finishRun = Join-Path $finishRepo ('tmp\geometric-finishes\legacy-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $finishRun -Force | Out-Null
$finishManifest = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2021\TrueBIM.GeometricFinishesLegacyQA.addin'
if (Test-Path -LiteralPath $finishManifest) { throw 'A legacy QA manifest already exists.' }
$finishAssembly = [Security.SecurityElement]::Escape((Join-Path $finishHarness 'bin\Release\net48\TrueBIM.Revit.GeometricFinishesLegacyHarness.dll'))
$finishXml = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>TrueBIM Legacy Finish QA</Name><Assembly>' + $finishAssembly + '</Assembly><AddInId>C1F2725B-C705-4BC9-BF86-8209D68487F4</AddInId><FullClassName>TrueBIM.Revit.GeometricFinishesLegacyHarness.LegacyApplication</FullClassName><VendorId>TRBM</VendorId></AddIn></RevitAddIns>'
$finishOldRoot = $env:TRUEBIM_FINISHES_LEGACY_QA
$finishProcess = $null
try {
    [IO.File]::WriteAllText($finishManifest, $finishXml)
    $env:TRUEBIM_FINISHES_LEGACY_QA = $finishRun
    $finishProcess = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2021\Revit.exe' -ArgumentList '/nosplash' -WindowStyle Hidden -PassThru
    [pscustomobject]@{ProcessId=$finishProcess.Id; RunRoot=$finishRun} | ConvertTo-Json | Set-Content (Join-Path $finishRepo 'tmp\geometric-finishes\legacy-active.json')
    Write-Output "Process: $($finishProcess.Id); run: $finishRun"
    $finishDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $finishDeadline -and -not (Test-Path (Join-Path $finishRun 'done')) -and -not (Test-Path (Join-Path $finishRun 'failed'))) {
        if ($finishProcess.HasExited) { break }
        Start-Sleep -Milliseconds 500
        $finishProcess.Refresh()
    }
    if (Test-Path (Join-Path $finishRun 'failed')) { throw (Get-Content (Join-Path $finishRun 'failed') -Raw) }
    if (-not (Test-Path (Join-Path $finishRun 'done'))) { throw 'Legacy Revit QA timed out.' }
    $cases = Get-Content -LiteralPath (Join-Path $finishRun 'legacy.json') -Raw | ConvertFrom-Json
    if (@($cases | Where-Object { $_.Passed -ne $true }).Count) { throw "Legacy acceptance failed: $finishRun" }
    Write-Output "Completed: $finishRun"
}
finally {
    if ($finishProcess -and -not $finishProcess.HasExited) { Stop-Process -Id $finishProcess.Id -Force }
    if (Test-Path -LiteralPath $finishManifest) { Remove-Item -LiteralPath $finishManifest -Force }
    $env:TRUEBIM_FINISHES_LEGACY_QA = $finishOldRoot
}

param(
    [string] $SourcePath = 'W:\Shared\Степанов. Материалы для разработки плагинов\КР • Сворачивание ВРС\D114_Р_КР_КЖ04.2.2_rvt24_отсоединено.rvt',
    [int] $TimeoutSeconds = 900
)
$ErrorActionPreference = 'Stop'
$vrsRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$vrsHarness = Join-Path $vrsRepo 'plugins\truebim\tests\TrueBIM.Revit.ScheduleCollapseHarness'
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
& (Resolve-DotNetSdk) build (Join-Path $vrsHarness 'TrueBIM.Revit.ScheduleCollapseHarness.csproj') -c Release --nologo -v:q -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Schedule harness build failed.' }
$vrsRunId = [Guid]::NewGuid().ToString('N')
$vrsRun = Join-Path $vrsRepo "tmp\schedule-collapse\$vrsRunId"
New-Item -ItemType Directory -Path (Join-Path $vrsRun 'models') -Force | Out-Null
$vrsHash = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash
Copy-Item -LiteralPath $SourcePath -Destination (Join-Path $vrsRun 'models')
$vrsManifest = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\TrueBIM.ScheduleCollapseHarness.$vrsRunId.addin"
$vrsAssembly = [Security.SecurityElement]::Escape((Join-Path $vrsHarness 'bin\Release\net48\TrueBIM.Revit.ScheduleCollapseHarness.dll'))
$vrsXml = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>TrueBIM Schedule Collapse QA</Name><Assembly>' + $vrsAssembly + '</Assembly><AddInId>30FE648F-334A-4708-A553-0E1254FF7A39</AddInId><FullClassName>TrueBIM.Revit.ScheduleCollapseHarness.ScheduleHarnessApplication</FullClassName><VendorId>TRBM</VendorId></AddIn></RevitAddIns>'
$vrsPreviousRoot = $env:TRUEBIM_VRS_QA
$vrsProcess = $null
try {
    [IO.File]::WriteAllText($vrsManifest, $vrsXml)
    $env:TRUEBIM_VRS_QA = $vrsRun
    $vrsProcess = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2024\Revit.exe' -ArgumentList '/nosplash' -WindowStyle Hidden -PassThru
    [pscustomobject]@{ ProcessId=$vrsProcess.Id; RunRoot=$vrsRun; SourceHash=$vrsHash } | ConvertTo-Json | Set-Content (Join-Path $vrsRepo 'tmp\schedule-collapse\active.json')
    Write-Output "Process: $($vrsProcess.Id); run: $vrsRun"
    $vrsDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $vrsDeadline -and -not (Test-Path (Join-Path $vrsRun 'done')) -and -not (Test-Path (Join-Path $vrsRun 'failed'))) {
        if ($vrsProcess.HasExited) { break }
        Start-Sleep -Milliseconds 500
        $vrsProcess.Refresh()
    }
    if (Test-Path (Join-Path $vrsRun 'failed')) { throw (Get-Content (Join-Path $vrsRun 'failed') -Raw) }
    if (-not (Test-Path (Join-Path $vrsRun 'done'))) { throw 'Revit did not complete the test; inspect startup dialogs.' }
    $vrsCases = @(Get-Content (Join-Path $vrsRun 'acceptance.json') -Raw | ConvertFrom-Json)
    if ($vrsCases.Count -ne 8) { throw 'Expected eight native acceptance cases.' }
    $vrsExpectedVisible = @(45, 57, 60, 61, 62, 63, 64, 65, 67, 69, 72, 89)
    foreach ($vrsCase in $vrsCases) {
        if ($vrsCase.HiddenColumnCount -ne 105 -or $vrsCase.VisibleColumnCount -ne 12 -or $vrsCase.UnchangedColumnCount -ne 21) {
            throw 'Unexpected native acceptance field counts.'
        }
        foreach ($vrsField in $vrsCase.Fields | Where-Object Name -Match '^Р • И[АЗ]') {
            if ($vrsField.Hidden -eq ($vrsField.Id -in $vrsExpectedVisible)) { throw "Wrong visibility: $($vrsField.Name)" }
        }
    }
    Write-Output "Completed: $vrsRun"
}
finally {
    if ($vrsProcess -and -not $vrsProcess.HasExited) { Stop-Process -Id $vrsProcess.Id -Force }
    if (Test-Path -LiteralPath $vrsManifest) { Remove-Item -LiteralPath $vrsManifest -Force }
    $env:TRUEBIM_VRS_QA = $vrsPreviousRoot
    if ((Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash -ne $vrsHash) { throw 'Original model changed.' }
}

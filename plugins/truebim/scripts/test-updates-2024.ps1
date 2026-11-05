param([switch] $KeepOpen, [int] $TimeoutSeconds = 600)
$ErrorActionPreference = 'Stop'
$updateRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$updateHarness = Join-Path $updateRepo 'plugins\truebim\tests\TrueBIM.Revit.UpdateHarness'
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
& (Resolve-DotNetSdk) build (Join-Path $updateHarness 'TrueBIM.Revit.UpdateHarness.csproj') -c Release --nologo -v:q -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Native update harness build failed.' }
$updateRunId = [Guid]::NewGuid().ToString('N')
$updateRun = Join-Path $updateRepo "tmp\updates-native\$updateRunId"
New-Item -ItemType Directory -Path $updateRun -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $updateRepo 'dist\installer\TrueBIM-Setup-0.2.51.exe') -Destination $updateRun
$updateAddinDirectory = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'
$updateOriginalManifest = Join-Path $updateAddinDirectory 'TrueBIM.addin'
$updateBackupManifest = Join-Path $updateAddinDirectory "TrueBIM.$updateRunId.qa-backup"
$updateTestManifest = Join-Path $updateAddinDirectory "TrueBIM.UpdateHarness.$updateRunId.addin"
$updateTargetAssembly = [Security.SecurityElement]::Escape((Join-Path $updateHarness 'bin\Release\net48\TrueBIM.UpdateHarness.Target.dll'))
$updateHarnessAssembly = [Security.SecurityElement]::Escape((Join-Path $updateHarness 'bin\Release\net48\TrueBIM.Revit.UpdateHarness.dll'))
$updateXml = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>TrueBIM</Name><Assembly>' + $updateTargetAssembly + '</Assembly><AddInId>8F8E8CC7-D3C9-49BA-8F40-AD0F2F8D32F7</AddInId><FullClassName>TrueBIM.App.App</FullClassName><VendorId>TRBM</VendorId></AddIn><AddIn Type="Application"><Name>TrueBIM Updates QA</Name><Assembly>' + $updateHarnessAssembly + '</Assembly><AddInId>5ADDD73B-71BB-4A13-A6C4-684222EB8B62</AddInId><FullClassName>TrueBIM.Revit.UpdateHarness.UpdateHarnessApplication</FullClassName><VendorId>TRBM</VendorId></AddIn></RevitAddIns>'
$updateOldRoot = $env:TRUEBIM_UPDATE_QA
$updateMoved = $false
$updateProcess = $null
try {
    if (Test-Path -LiteralPath $updateOriginalManifest) { Move-Item -LiteralPath $updateOriginalManifest -Destination $updateBackupManifest; $updateMoved = $true }
    [IO.File]::WriteAllText($updateTestManifest, $updateXml)
    $env:TRUEBIM_UPDATE_QA = $updateRun
    $updateProcess = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2024\Revit.exe' -ArgumentList '/nosplash' -WindowStyle Hidden -PassThru
    [pscustomobject]@{ProcessId=$updateProcess.Id; Run=$updateRun; Manifest=$updateTestManifest; Original=$updateOriginalManifest; Backup=$updateBackupManifest} | ConvertTo-Json | Set-Content (Join-Path $updateRepo 'tmp\updates-native\active.json')
    Write-Output "Process=$($updateProcess.Id); evidence=$updateRun"
    $updateDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not (Test-Path (Join-Path $updateRun 'done')) -and -not (Test-Path (Join-Path $updateRun 'failed'))) {
        if ($updateProcess.HasExited -or [DateTime]::UtcNow -ge $updateDeadline) { throw 'Native update QA did not finish; inspect startup dialogs.' }
        Start-Sleep -Milliseconds 300
        $updateProcess.Refresh()
    }
    if (Test-Path (Join-Path $updateRun 'failed')) { throw (Get-Content (Join-Path $updateRun 'failed') -Raw) }
    Write-Output "Native checks passed: $updateRun"
    if ($KeepOpen) {
        Write-Output "Create $updateRun\stop to close the QA Revit and restore its manifest."
        while (-not (Test-Path (Join-Path $updateRun 'stop')) -and -not $updateProcess.HasExited) {
            Start-Sleep -Milliseconds 500
            $updateProcess.Refresh()
        }
    }
} finally {
    if ($updateProcess -and -not $updateProcess.HasExited) { Stop-Process -Id $updateProcess.Id -Force; $null = $updateProcess.WaitForExit(30000) }
    if (Test-Path -LiteralPath $updateTestManifest) { Remove-Item -LiteralPath $updateTestManifest }
    if ($updateMoved) { Move-Item -LiteralPath $updateBackupManifest -Destination $updateOriginalManifest }
    $env:TRUEBIM_UPDATE_QA = $updateOldRoot
}

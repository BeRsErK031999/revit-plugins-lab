param([switch] $CancelInUi)
$ErrorActionPreference = 'Stop'
$updateRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
$updateDotnet = Resolve-DotNetSdk
& $updateDotnet build (Join-Path $updateRepo 'plugins\truebim\src\TrueBIM.Updater\TrueBIM.Updater.csproj') -c Release --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Updater build failed.' }
& $updateDotnet build (Join-Path $updateRepo 'plugins\truebim\tests\TrueBIM.UpdateFixture\TrueBIM.UpdateFixture.csproj') -c Release --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
$updateRun = Join-Path $updateRepo ('tmp\updater-handoff\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $updateRun -Force | Out-Null
$updateFixture = Join-Path $updateRepo 'plugins\truebim\tests\TrueBIM.UpdateFixture\bin\Release\net48\TrueBIM-Setup-99.0.0.exe'
$updateHelper = Join-Path $updateRepo 'plugins\truebim\src\TrueBIM.Updater\bin\Release\net48\TrueBIM.Updater.exe'
function Wait-UpdateCondition([scriptblock] $Condition, [int] $Seconds = 30) {
    $updateDeadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while (-not (& $Condition)) {
        if ([DateTime]::UtcNow -ge $updateDeadline) { throw 'Timed out waiting for updater test.' }
        Start-Sleep -Milliseconds 200
    }
}
$updateResults = @()
$updateScenarios = if ($CancelInUi) { @('cancel-ui') } else { @('success','tamper','installer-error') }
foreach ($updateScenario in $updateScenarios) {
    $updateCase = Join-Path $updateRun $updateScenario
    $updateRevitDir = Join-Path $updateCase 'fake-revit'
    New-Item -ItemType Directory -Path $updateRevitDir -Force | Out-Null
    $updateInstaller = Join-Path $updateCase 'TrueBIM-Setup-99.0.0.exe'
    Copy-Item -LiteralPath $updateFixture -Destination $updateInstaller
    Copy-Item -LiteralPath $updateFixture -Destination (Join-Path $updateRevitDir 'Revit.exe')
    Copy-Item -LiteralPath $updateHelper -Destination $updateCase
    if (Test-Path -LiteralPath ($updateHelper + '.config')) { Copy-Item -LiteralPath ($updateHelper + '.config') -Destination $updateCase }
    $updateHash = (Get-FileHash -LiteralPath $updateInstaller -Algorithm SHA256).Hash
    $updateRevitProcess = $null
    $updateHelperProcess = $null
    try {
        $updateRevitProcess = Start-Process -FilePath (Join-Path $updateRevitDir 'Revit.exe') -ArgumentList '--hold' -WorkingDirectory $updateRevitDir -WindowStyle Hidden -PassThru
        Wait-UpdateCondition { Test-Path (Join-Path $updateRevitDir 'holding.txt') }
        $updateArguments = '"' + $updateInstaller + '" 99.0.0 ' + $updateHash + ' revit2024'
        $updateWindowStyle = if ($CancelInUi) { 'Normal' } else { 'Hidden' }
        $updateHelperProcess = Start-Process -FilePath (Join-Path $updateCase 'TrueBIM.Updater.exe') -ArgumentList $updateArguments -WorkingDirectory $updateCase -WindowStyle $updateWindowStyle -PassThru
        $updateLog = Join-Path $updateCase 'update.log'
        Wait-UpdateCondition { (Test-Path $updateLog) -and (Get-Content -LiteralPath $updateLog -Raw) -match 'Waiting for all Revit' }
        Start-Sleep -Milliseconds 1200
        if ((Test-Path (Join-Path $updateCase 'launched.txt')) -or $updateRevitProcess.HasExited) { throw 'Updater did not preserve the running Revit process.' }
        if ($CancelInUi) {
            [pscustomobject]@{ Run=$updateRun; HelperId=$updateHelperProcess.Id; RevitId=$updateRevitProcess.Id } | ConvertTo-Json | Set-Content (Join-Path $updateRepo 'tmp\updater-handoff\active-ui.json')
            Write-Output 'Click Cancel in the updater window for the cancellation test.'
            Wait-UpdateCondition { $updateHelperProcess.Refresh(); $updateHelperProcess.HasExited } -Seconds 180
            if ((Test-Path (Join-Path $updateCase 'launched.txt')) -or $updateRevitProcess.HasExited) { throw 'Cancellation changed Revit or launched the installer.' }
            if ((Get-Content -LiteralPath $updateLog -Raw) -notmatch 'Cancelled before installation') { throw 'UI cancellation was not recorded.' }
        }
        else {
            if ($updateScenario -eq 'tamper') { Add-Content -LiteralPath $updateInstaller -Value 'tamper' }
            if ($updateScenario -eq 'installer-error') { Set-Content -LiteralPath (Join-Path $updateCase 'fail-install.txt') -Value 'fail' }
            Set-Content -LiteralPath (Join-Path $updateRevitDir 'release.txt') -Value 'close normally'
            if ($updateScenario -eq 'tamper') {
                Wait-UpdateCondition { (Get-Content -LiteralPath $updateLog -Raw) -match 'Контрольная сумма' }
                if (Test-Path (Join-Path $updateCase 'launched.txt')) { throw 'Tampered installer was launched.' }
            }
            else {
                $updateExpectedExit = if ($updateScenario -eq 'success') { '0' } else { '8' }
                Wait-UpdateCondition { (Get-Content -LiteralPath $updateLog -Raw) -match ('Installer exit code=' + $updateExpectedExit) }
                $updateLaunchedArguments = Get-Content -LiteralPath (Join-Path $updateCase 'launched.txt')
                foreach ($updateRequired in @('/NORESTART','/NOCLOSEAPPLICATIONS','/NORESTARTAPPLICATIONS','/COMPONENTS=revit2024')) {
                    if ($updateLaunchedArguments -notcontains $updateRequired) { throw "Missing installer option: $updateRequired" }
                }
            }
        }
        $updateResults += [pscustomobject]@{ Scenario=$updateScenario; Passed=$true }
    }
    finally {
        if ($updateHelperProcess -and -not $updateHelperProcess.HasExited) { Stop-Process -Id $updateHelperProcess.Id -Force }
        if ($updateRevitProcess -and -not $updateRevitProcess.HasExited) { Stop-Process -Id $updateRevitProcess.Id -Force }
    }
}
$updateResults | ConvertTo-Json | Set-Content (Join-Path $updateRun 'results.json')
$updateResults | Format-Table -AutoSize
Write-Output "Evidence: $updateRun"

param([string] $InnoCompilerPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$outputRoot = Join-Path $repoRoot 'dist\installer-tests'
if (Get-Process Revit, RevitCoreConsole -ErrorAction SilentlyContinue) {
    throw 'Close Revit before running this test. It never terminates existing processes.'
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
& $InnoCompilerPath /Q (Join-Path $PSScriptRoot 'RevitProcessGuard.iss')
if ($LASTEXITCODE -ne 0) { throw 'Guard harness compilation failed.' }
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$outputRoot\Revit.exe" (Join-Path $PSScriptRoot 'ProcessFixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Process fixture compilation failed.' }
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library "/out:$outputRoot\ProbePayload.dll" (Join-Path $PSScriptRoot 'ProcessFixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Image mapping fixture compilation failed.' }
Copy-Item -LiteralPath "$outputRoot\Revit.exe" -Destination "$outputRoot\RevitCoreConsole.exe" -Force
Copy-Item -LiteralPath "$outputRoot\Revit.exe" -Destination "$outputRoot\UnrelatedApp.exe" -Force

$cases = @(
    @{ Name = 'no-processes'; Processes = @(); Exit = 0 },
    @{ Name = 'unrelated'; Processes = @('UnrelatedApp'); Exit = 0 },
    @{ Name = 'revit'; Processes = @('Revit'); Exit = 7 },
    @{ Name = 'headless'; Processes = @('RevitCoreConsole'); Exit = 7 },
    @{ Name = 'multiple'; Processes = @('Revit', 'Revit', 'RevitCoreConsole', 'UnrelatedApp'); Exit = 7 },
    @{ Name = 'unlocked-files'; Processes = @(); FileMode = 'free'; Exit = 0 },
    @{ Name = 'write-locked-file'; Processes = @(); FileMode = 'write-locked'; Exit = 7 },
    @{ Name = 'delete-locked-file'; Processes = @(); FileMode = 'delete-locked'; Exit = 7 },
    @{ Name = 'readonly-file'; Processes = @(); FileMode = 'readonly'; Exit = 7 },
    @{ Name = 'released-file'; Processes = @(); FileMode = 'released'; Exit = 0 },
    @{ Name = 'loaded-dll'; Processes = @(); FileMode = 'image'; Exit = 7 }
)
$results = foreach ($case in $cases) {
    $fixtures = @()
    $lockedStream = $null
    $payload = $null
    $testRoot = Join-Path $outputRoot ($case.Name + '-' + [guid]::NewGuid().ToString('N'))
    try {
        foreach ($name in $case.Processes) {
            $fixtures += Start-Process -FilePath "$outputRoot\$name.exe" -WindowStyle Hidden -PassThru
        }
        if ($case.FileMode) {
            $nested = Join-Path $testRoot '2025\Modules'
            New-Item -ItemType Directory -Path $nested -Force | Out-Null
            $payload = Join-Path $nested 'ProbePayload.dll'
            Copy-Item -LiteralPath "$outputRoot\ProbePayload.dll" -Destination $payload
            $before = (Get-FileHash -LiteralPath $payload).Hash
            switch ($case.FileMode) {
                'write-locked' { $lockedStream = [IO.File]::Open($payload, 'Open', 'Read', 'Read') }
                'delete-locked' { $lockedStream = [IO.File]::Open($payload, 'Open', 'Read', 'ReadWrite') }
                'readonly' { (Get-Item -LiteralPath $payload).IsReadOnly = $true }
                'released' { $lockedStream = [IO.File]::Open($payload, 'Open', 'Read', 'Read'); $lockedStream.Dispose(); $lockedStream = $null }
                'image' {
                    $marker = Join-Path $outputRoot ('loaded-' + [guid]::NewGuid().ToString('N') + '.txt')
                    $fixtures += Start-Process -FilePath "$outputRoot\UnrelatedApp.exe" -WindowStyle Hidden -PassThru -ArgumentList @(
                        ('"' + $payload + '"'), ('"' + $marker + '"')
                    )
                    $deadline = [DateTime]::UtcNow.AddSeconds(10)
                    while (-not (Test-Path -LiteralPath $marker) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
                    if (-not (Test-Path -LiteralPath $marker)) { throw 'DLL image fixture did not become ready.' }
                }
            }
        }
        $log = Join-Path $outputRoot ($case.Name + '.log')
        $setup = Start-Process -FilePath "$outputRoot\RevitProcessGuard-Test.exe" -WindowStyle Hidden -PassThru -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + $log + '"'), ('/GUARDROOT="' + $testRoot + '"')
        )
        if (-not $setup.WaitForExit(30000)) {
            $setup.Kill()
            throw "Installer guard timed out: $($case.Name)"
        }
        if ($setup.ExitCode -ne $case.Exit) { throw "Unexpected exit code in $($case.Name): $($setup.ExitCode)" }
        foreach ($fixture in $fixtures) {
            if ($fixture.HasExited) { throw "Silent installation terminated a fixture: $($case.Name)" }
        }
        $logText = Get-Content -LiteralPath $log -Raw
        if ($logText -notmatch 'Terminal-server ownership/session/termination checks passed') { throw 'Session policy checks missing.' }
        if ($payload) {
            if ((Get-FileHash -LiteralPath $payload).Hash -ne $before) { throw 'Preflight changed the existing DLL.' }
            if ($case.Exit -eq 7 -and -not $logText.Contains($payload)) { throw 'Blocked file path missing from diagnostic.' }
        }
        foreach ($fixture in ($fixtures | Where-Object ProcessName -ne 'UnrelatedApp')) {
            if ($logText -notmatch ('PID ' + $fixture.Id + '\b')) { throw 'Blocked process PID missing from diagnostic.' }
        }
        [pscustomobject]@{ Case = $case.Name; ExitCode = $setup.ExitCode; Passed = $true }
    }
    finally {
        if ($lockedStream) { $lockedStream.Dispose() }
        if ($payload -and (Test-Path -LiteralPath $payload)) { (Get-Item -LiteralPath $payload).IsReadOnly = $false }
        foreach ($fixture in $fixtures) {
            if (-not $fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit() }
            $fixture.Dispose()
        }
    }
}
$results | ConvertTo-Json | Set-Content -LiteralPath "$outputRoot\results.json" -Encoding UTF8
$results | Format-Table -AutoSize

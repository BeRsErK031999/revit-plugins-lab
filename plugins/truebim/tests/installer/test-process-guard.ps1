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
Copy-Item -LiteralPath "$outputRoot\Revit.exe" -Destination "$outputRoot\RevitCoreConsole.exe" -Force
Copy-Item -LiteralPath "$outputRoot\Revit.exe" -Destination "$outputRoot\UnrelatedApp.exe" -Force

$cases = @(
    @{ Name = 'no-processes'; Processes = @(); Exit = 0 },
    @{ Name = 'unrelated'; Processes = @('UnrelatedApp'); Exit = 0 },
    @{ Name = 'revit'; Processes = @('Revit'); Exit = 7 },
    @{ Name = 'headless'; Processes = @('RevitCoreConsole'); Exit = 7 },
    @{ Name = 'multiple'; Processes = @('Revit', 'Revit', 'RevitCoreConsole', 'UnrelatedApp'); Exit = 7 }
)
$results = foreach ($case in $cases) {
    $fixtures = @()
    try {
        foreach ($name in $case.Processes) {
            $fixtures += Start-Process -FilePath "$outputRoot\$name.exe" -WindowStyle Hidden -PassThru
        }
        $log = Join-Path $outputRoot ($case.Name + '.log')
        $setup = Start-Process -FilePath "$outputRoot\RevitProcessGuard-Test.exe" -WindowStyle Hidden -PassThru -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + $log + '"')
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
        foreach ($fixture in ($fixtures | Where-Object ProcessName -ne 'UnrelatedApp')) {
            if ($logText -notmatch ('PID ' + $fixture.Id + '\b')) { throw 'Blocked process PID missing from diagnostic.' }
        }
        [pscustomobject]@{ Case = $case.Name; ExitCode = $setup.ExitCode; Passed = $true }
    }
    finally {
        foreach ($fixture in $fixtures) {
            if (-not $fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit() }
            $fixture.Dispose()
        }
    }
}
$results | ConvertTo-Json | Set-Content -LiteralPath "$outputRoot\results.json" -Encoding UTF8
$results | Format-Table -AutoSize

param(
    [Parameter(Mandatory)][string] $ModelPath,
    [ValidateRange(60, 1800)][int] $TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $pluginRoot '..\..'))
$dotnet = Join-Path $repositoryRoot 'tools\.dotnet\dotnet.exe'
$runId = [Guid]::NewGuid().ToString('N')
$runRoot = Join-Path $repositoryRoot ('tmp\vernadskogo-audit\' + $runId)
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$binaryRoot = Join-Path $runRoot 'bin'
& $dotnet build (Join-Path $pluginRoot 'tests\Vernadskogo.RevitHarness\Vernadskogo.RevitHarness.csproj') -c Release "-p:OutputPath=$binaryRoot\" --nologo --verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw 'Revit harness build failed.' }
$sourcePath = (Resolve-Path -LiteralPath $ModelPath).Path
$sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$copyPath = Join-Path $runRoot ([IO.Path]::GetFileName($sourcePath))
Copy-Item -LiteralPath $sourcePath -Destination $copyPath
$manifestDirectory = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'
New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
$manifestPath = Join-Path $manifestDirectory ('Vernadskogo.Audit.' + $runId + '.addin')
$reportPath = Join-Path $runRoot 'report.json'
$assembly = [Security.SecurityElement]::Escape((Join-Path $binaryRoot 'Vernadskogo.RevitHarness.dll'))
$manifest = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>Vernadskogo model regression</Name><Assembly>' + $assembly + '</Assembly><AddInId>02FDFA02-7014-405C-BB67-83C91D280E6D</AddInId><FullClassName>Vernadskogo.RevitHarness.ModelAuditApplication</FullClassName><VendorId>VRND</VendorId><VendorDescription>Vernadskogo disposable model regression</VendorDescription></AddIn></RevitAddIns>'
$previousReport = $env:VERNADSKOGO_AUDIT_REPORT
$previousModel = $env:VERNADSKOGO_AUDIT_MODEL
$testProcess = $null
try {
    [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
    $env:VERNADSKOGO_AUDIT_REPORT = $reportPath
    $env:VERNADSKOGO_AUDIT_MODEL = $copyPath
    $testProcess = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2024\Revit.exe' -ArgumentList '/language RUS /nosplash' -WindowStyle Hidden -PassThru
    [pscustomobject]@{ ProcessId=$testProcess.Id; Manifest=$manifestPath; Report=$reportPath; Copy=$copyPath; SourceHash=$sourceHash } |
        ConvertTo-Json | Set-Content (Join-Path $runRoot 'run.json')
    Write-Output "Test process: $($testProcess.Id); report: $reportPath"
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $reportPath)) {
        $testProcess.Refresh()
        if ($testProcess.HasExited) { throw "Test Revit exited before report: $($testProcess.ExitCode)" }
        Start-Sleep -Milliseconds 500
    }
    if (-not (Test-Path -LiteralPath $reportPath)) { throw "Timeout. Report expected at: $reportPath" }
    $report = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json
    $report.Passed | Write-Output
    $report.Facts | ConvertTo-Json -Depth 5 | Write-Output
    if ($report.FatalError) { throw $report.FatalError }
}
finally {
    # Only the disposable process created by this invocation is stopped.
    if ($testProcess -and -not $testProcess.HasExited) { Stop-Process -Id $testProcess.Id -Force }
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
    $env:VERNADSKOGO_AUDIT_REPORT = $previousReport
    $env:VERNADSKOGO_AUDIT_MODEL = $previousModel
    $finalHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    if ($sourceHash -ne $finalHash) { throw 'Original model hash changed during test.' }
    Write-Output "Original model unchanged (SHA256 $sourceHash)."
}

param([string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if (!$OutputRoot) { $OutputRoot = Join-Path $root ('..\..\tmp\model-sheet-publisher\sdk-matrix-' + [guid]::NewGuid().ToString('N')) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$sdk = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
$matrix = @(
    @{ Year = 2021; Framework = 'net48'; Api = '24.0.0' },
    @{ Year = 2022; Framework = 'net48'; Api = '24.1.51000' },
    @{ Year = 2023; Framework = 'net48'; Api = '24.2.0' },
    @{ Year = 2024; Framework = 'net48'; Api = '24.3.0' },
    @{ Year = 2025; Framework = 'net8.0-windows'; Api = '25.0.1' },
    @{ Year = 2026; Framework = 'net8.0-windows'; Api = '25.1.0' }
)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
foreach ($entry in $matrix) {
    $output = Join-Path $OutputRoot $entry.Year
    & $sdk build (Join-Path $root 'src\ModelSheetPublisher.csproj') -c Release "-p:TargetFrameworks=$($entry.Framework)" "-p:AutoCADApiVersion=$($entry.Api)" "-p:BaseOutputPath=$output\" '-p:IncludeTestHarness=false' --nologo /warnaserror 2>&1 | Tee-Object -FilePath (Join-Path $OutputRoot "$($entry.Year).log")
    if ($LASTEXITCODE -ne 0) { throw "SDK compatibility build failed: AutoCAD $($entry.Year)" }
}
'PASS: API compilation for AutoCAD 2021, 2022, 2023, 2024, 2025 and 2026. This is not a native-host runtime test.' | Set-Content (Join-Path $OutputRoot 'PASS.txt')
Write-Host "SDK matrix passed: $OutputRoot"

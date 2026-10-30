param(
    [string] $ModelDirectory = 'C:\Users\Borodin_A\Desktop\Revit\ТЭП',
    [int] $TimeoutSeconds = 600,
    [switch] $KeepRevitOpen
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$exe = 'C:\Program Files\Autodesk\Revit 2024\Revit.exe'
if (Get-Process Revit -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) {
    throw 'Close Revit 2024 before the isolated test.'
}
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
$dotnet = Resolve-DotNetSdk
$project = Join-Path $repo 'plugins/truebim/tests/TrueBIM.Revit.SelectionBoxHarness/TrueBIM.Revit.SelectionBoxHarness.csproj'
& $dotnet build $project -c Release --nologo -v:q
if ($LASTEXITCODE) { throw 'Harness build failed.' }
$qaRoot = Join-Path $repo ('tmp/selection-box-qa-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$models = Join-Path $qaRoot 'models'
New-Item -ItemType Directory -Path $models -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath $ModelDirectory -Filter '*.rvt')
$before = @($sources | Get-FileHash -Algorithm SHA256)
$before | Select-Object Path,Hash | ConvertTo-Json | Set-Content (Join-Path $qaRoot 'source-hashes-before.json') -Encoding UTF8
foreach ($source in $sources) { Copy-Item -LiteralPath $source.FullName -Destination $models }
$assembly = Join-Path (Split-Path $project) 'bin/Release/net48/TrueBIM.Revit.SelectionBoxHarness.dll'
$manifest = Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2024/TrueBIM.SelectionBox.QA.addin'
if (Test-Path -LiteralPath $manifest) { throw "Existing QA manifest: $manifest" }
$escapedAssembly = [Security.SecurityElement]::Escape($assembly)
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application">
<Name>TrueBIM Selection Box QA</Name><Assembly>$escapedAssembly</Assembly>
<AddInId>1DCD4E2A-557F-4542-BDA8-F109D6E1E147</AddInId>
<FullClassName>TrueBIM.Revit.SelectionBoxHarness.SelectionBoxHarnessApplication</FullClassName>
<VendorId>TRBM</VendorId><VendorDescription>TrueBIM local QA on disposable copies</VendorDescription>
</AddIn></RevitAddIns>
"@
$oldEnvironment = $env:TRUEBIM_SELECTION_BOX_QA
$process = $null
try {
    [IO.File]::WriteAllText($manifest, $xml, [Text.UTF8Encoding]::new($false))
    $env:TRUEBIM_SELECTION_BOX_QA = $qaRoot
    Write-Output "QA output: $qaRoot"
    $process = Start-Process -FilePath $exe -ArgumentList '/viewer' -WindowStyle Hidden -PassThru
    Write-Output "QA process: $($process.Id)"
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not $process.HasExited -and
        -not (Test-Path (Join-Path $qaRoot 'done')) -and -not (Test-Path (Join-Path $qaRoot 'failed'))) {
        Start-Sleep -Seconds 1
        $process.Refresh()
    }
    if (Test-Path (Join-Path $qaRoot 'report.txt')) { Get-Content (Join-Path $qaRoot 'report.txt') }
    if (-not (Test-Path (Join-Path $qaRoot 'done'))) { throw "Native QA did not pass. See $qaRoot" }
}
finally {
    $env:TRUEBIM_SELECTION_BOX_QA = $oldEnvironment
    if (-not $KeepRevitOpen -and $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Remove-Item -LiteralPath $manifest -Force -ErrorAction SilentlyContinue
    $after = @($sources | Get-FileHash -Algorithm SHA256)
    $after | Select-Object Path,Hash | ConvertTo-Json | Set-Content (Join-Path $qaRoot 'source-hashes-after.json') -Encoding UTF8
    if (Compare-Object ($before | ForEach-Object Hash) ($after | ForEach-Object Hash)) { throw 'Source model hashes changed.' }
    Write-Output 'Source RVT hashes unchanged.'
}

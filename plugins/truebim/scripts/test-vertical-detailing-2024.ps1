param(
    [string] $SourceDirectory = 'C:\Users\Borodin_A\Desktop\Revit\Оформление вертикальных конструкций',
    [ValidateRange(30, 1800)][int] $TimeoutSeconds = 900,
    [ValidateSet('audit','apply')][string] $Mode = 'apply',
    [string] $ViewIds = '2310980,2568691',
    [string] $PreviewPayload = '',
    [string] $ViewNameFilter = '',
    [ValidateSet('Straight','BentSameSide','BentOppositeSides')][string] $Variant = 'Straight',
    [switch] $Viewer
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$harnessRoot = Join-Path $repoRoot 'plugins\truebim\tests\TrueBIM.Revit.VerticalDetailingHarness'
. (Join-Path $PSScriptRoot 'resolve-dotnet-sdk.ps1')
$verticalDotnet = Resolve-DotNetSdk
& $verticalDotnet build (Join-Path $harnessRoot 'TrueBIM.Revit.VerticalDetailingHarness.csproj') -c Release --nologo --verbosity:quiet -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Vertical detailing harness build failed.' }
$runId = [Guid]::NewGuid().ToString('N')
$reportRoot = Join-Path $repoRoot 'tmp\vertical-detailing\native'
$runRoot = Join-Path $reportRoot $runId
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$originals = @(Get-ChildItem -LiteralPath $SourceDirectory -File -Filter '*.rvt')
$hashes = @{}
foreach ($file in $originals) {
    $hashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $runRoot $file.Name)
}
$modelCopy = Join-Path $runRoot 'D114_Р_КР_КЖ03.2.1_rvt24.rvt'
$manifestPath = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\TrueBIM.VerticalDetailingHarness.$runId.addin"
$assembly = [Security.SecurityElement]::Escape((Join-Path $harnessRoot 'bin\Release\net48\TrueBIM.Revit.VerticalDetailingHarness.dll'))
$manifest = '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>TrueBIM Vertical Detailing Audit</Name><Assembly>' + $assembly + '</Assembly><AddInId>3A2241B6-A5EC-4268-A945-AF9C03999E31</AddInId><FullClassName>TrueBIM.Revit.VerticalDetailingHarness.VerticalDetailingHarnessApplication</FullClassName><VendorId>TRBM</VendorId></AddIn></RevitAddIns>'
$previousReport = $env:TRUEBIM_VERTICAL_REPORT
$previousModel = $env:TRUEBIM_VERTICAL_MODEL
$previousMode = $env:TRUEBIM_VERTICAL_MODE
$previousViews = $env:TRUEBIM_VERTICAL_VIEWS
$previousPayload = $env:TRUEBIM_VERTICAL_PAYLOAD
$previousViewNameFilter = $env:TRUEBIM_VERTICAL_VIEW_NAME_FILTER
$previousVariant = $env:TRUEBIM_VERTICAL_VARIANT
$viewerProcess = $null
try {
    [IO.File]::WriteAllText($manifestPath, $manifest)
    $env:TRUEBIM_VERTICAL_REPORT = Join-Path $runRoot 'report.json'
    $env:TRUEBIM_VERTICAL_MODEL = $modelCopy
    $env:TRUEBIM_VERTICAL_MODE = $Mode
    $env:TRUEBIM_VERTICAL_VIEWS = $ViewIds
    $env:TRUEBIM_VERTICAL_PAYLOAD = $PreviewPayload
    $env:TRUEBIM_VERTICAL_VIEW_NAME_FILTER = $ViewNameFilter
    $env:TRUEBIM_VERTICAL_VARIANT = $Variant
    $launchArguments = if ($Viewer) { '/viewer' } else { '/nosplash' }
    $viewerProcess = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2024\Revit.exe' -ArgumentList $launchArguments -WindowStyle Hidden -PassThru
    [pscustomobject]@{ ProcessId=$viewerProcess.Id; Report=$env:TRUEBIM_VERTICAL_REPORT; Manifest=$manifestPath; RunRoot=$runRoot; SourceHashes=$hashes } | ConvertTo-Json | Set-Content (Join-Path $reportRoot 'active-harness.json')
    Write-Output "Process: $($viewerProcess.Id); report: $env:TRUEBIM_VERTICAL_REPORT"
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $env:TRUEBIM_VERTICAL_REPORT)) {
        if ($viewerProcess.HasExited) { break }
        Start-Sleep -Milliseconds 500
        $viewerProcess.Refresh()
    }
    if (-not (Test-Path -LiteralPath $env:TRUEBIM_VERTICAL_REPORT)) { throw 'Revit did not produce a report; check startup dialogs.' }
    $report = Get-Content -LiteralPath $env:TRUEBIM_VERTICAL_REPORT -Raw | ConvertFrom-Json
    if ($report.FatalError) { throw $report.FatalError }
    if ($Mode -eq 'apply') {
        $failedViews = @($report.Tests | Where-Object { -not $_.First.Succeeded -or -not $_.Second.Succeeded -or -not $_.RepeatStable -or -not $_.ViewUnchanged -or -not $_.ProtectedAnnotationSafe -or -not $_.NativeEqualitySafe -or -not $_.NoManualPitchText -or $_.VisibleAnnotations -ne $_.OwnedAnnotations })
        if (-not $report.ModelUnchanged -or -not $report.QuantityDisabledSafe -or -not $report.SelectionWorks -or -not $report.HorizontalViewsRejected -or -not $report.BatchSucceeded -or -not $report.BatchUndoStable -or $failedViews.Count -gt 0) {
            throw "Native acceptance failed. Views: $($failedViews.Name -join ', '). See $env:TRUEBIM_VERTICAL_REPORT"
        }
        if ($PreviewPayload -and -not $report.Preview.Rendered) { throw 'Preview window was not rendered.' }
    }
    Write-Output "Report: $env:TRUEBIM_VERTICAL_REPORT"
}
finally {
    if ($viewerProcess -and -not $viewerProcess.HasExited) { Stop-Process -Id $viewerProcess.Id -Force }
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
    $env:TRUEBIM_VERTICAL_REPORT = $previousReport
    $env:TRUEBIM_VERTICAL_MODEL = $previousModel
    $env:TRUEBIM_VERTICAL_MODE = $previousMode
    $env:TRUEBIM_VERTICAL_VIEWS = $previousViews
    $env:TRUEBIM_VERTICAL_PAYLOAD = $previousPayload
    $env:TRUEBIM_VERTICAL_VIEW_NAME_FILTER = $previousViewNameFilter
    $env:TRUEBIM_VERTICAL_VARIANT = $previousVariant
    foreach ($file in $originals) {
        if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $hashes[$file.FullName]) { throw "Original changed: $($file.FullName)" }
    }
}

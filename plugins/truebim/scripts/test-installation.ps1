param(
    [string[]] $Years = @("2019", "2020", "2021", "2022", "2023", "2024", "2025"),

    [string] $InstallRoot = "",

    [string] $AddinsRoot = "",

    [string[]] $SmokeTestedYears = @(),

    [string] $ReportPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path $env:APPDATA "TrueBIM"
}

if ([string]::IsNullOrWhiteSpace($AddinsRoot)) {
    $AddinsRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins"
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $reportDir = Join-Path ([string] $repoRoot) "dist"
    New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    $ReportPath = Join-Path $reportDir "installation-report.json"
}

function Test-RevitInstalled {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Year
    )

    $installRoot = Join-Path ${env:ProgramFiles} "Autodesk\Revit $Year"
    $revitExe = Join-Path $installRoot "Revit.exe"
    $userAddinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$Year"
    $machineAddinRoot = Join-Path ${env:ProgramData} "Autodesk\Revit\Addins\$Year"

    return (Test-Path -LiteralPath $revitExe) -or (Test-Path -LiteralPath $userAddinRoot) -or (Test-Path -LiteralPath $machineAddinRoot)
}

$report = foreach ($year in $Years) {
    $versionInstallRoot = Join-Path $InstallRoot $year
    $expectedDllPath = Join-Path $versionInstallRoot "TrueBIM.App.dll"
    $manifestPath = Join-Path (Join-Path $AddinsRoot $year) "TrueBIM.addin"
    $manifestInstalled = Test-Path -LiteralPath $manifestPath
    $xmlValid = $false
    $assemblyPath = ""
    $assemblyFound = $false
    $manifestPathEncodingValid = $false
    $errorMessage = ""

    if ($manifestInstalled) {
        try {
            [xml] $manifest = Get-Content -LiteralPath $manifestPath
            $assemblyPath = [string] $manifest.RevitAddIns.AddIn.Assembly
            $manifestPathEncodingValid = -not (
                [string]::IsNullOrWhiteSpace($assemblyPath) -or
                $assemblyPath.Contains([string] [char] 0xFFFD) -or
                $assemblyPath.Contains("?")
            )
            $xmlValid = $manifestPathEncodingValid
            $assemblyFound = $xmlValid -and (Test-Path -LiteralPath $assemblyPath)
        }
        catch {
            $errorMessage = $_.Exception.Message
        }
    }

    $payloadDllFound = Test-Path -LiteralPath $expectedDllPath
    $depsPath = Join-Path $versionInstallRoot "TrueBIM.App.deps.json"
    $runtimeConfigPath = Join-Path $versionInstallRoot "TrueBIM.App.runtimeconfig.json"
    $depsFound = Test-Path -LiteralPath $depsPath
    $runtimeConfigFound = Test-Path -LiteralPath $runtimeConfigPath
    $revit2025PayloadOk = $year -ne "2025" -or $depsFound
    $smokeTested = $SmokeTestedYears -contains $year
    $successful = $manifestInstalled -and $xmlValid -and $assemblyFound -and $payloadDllFound -and $revit2025PayloadOk

    [pscustomobject]@{
        RevitVersion = $year
        RevitInstalledOnPc = Test-RevitInstalled -Year $year
        InstallRoot = $versionInstallRoot
        PayloadDllFound = $payloadDllFound
        ManifestPath = $manifestPath
        ManifestInstalled = $manifestInstalled
        XmlValid = $xmlValid
        ManifestPathEncodingValid = $manifestPathEncodingValid
        AssemblyPath = $assemblyPath
        AssemblyFound = $assemblyFound
        Revit2025DepsFound = if ($year -eq "2025") { $depsFound } else { $null }
        Revit2025RuntimeConfigFound = if ($year -eq "2025") { $runtimeConfigFound } else { $null }
        RuntimeSmokeTested = $smokeTested
        SuccessfulFileValidation = $successful
        Error = $errorMessage
    }
}

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReportPath -Encoding UTF8

Write-Host "TrueBIM installed file validation:"
$report | Format-Table RevitVersion, RevitInstalledOnPc, PayloadDllFound, ManifestInstalled, XmlValid, ManifestPathEncodingValid, AssemblyFound, RuntimeSmokeTested, SuccessfulFileValidation -AutoSize
Write-Host "Report: $ReportPath"
Write-Host "Runtime smoke-test is marked only for years passed in -SmokeTestedYears."

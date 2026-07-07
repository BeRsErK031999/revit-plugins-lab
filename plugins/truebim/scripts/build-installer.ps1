param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [string] $InnoCompilerPath = "",

    [switch] $SkipInstaller,

    [switch] $AllowMissingRevitApi
)

$ErrorActionPreference = "Stop"

$revitYears = @("2019", "2020", "2021", "2022", "2023", "2024", "2025")
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$repoRootPath = [System.IO.Path]::GetFullPath([string] $repoRoot)
$trueBimRoot = Join-Path $repoRootPath "plugins\truebim"
$projectPath = Join-Path $trueBimRoot "src\TrueBIM.App\TrueBIM.App.csproj"
$distRoot = Join-Path $repoRootPath "dist"
$distRevitRoot = Join-Path $distRoot "revit"
$distInstallerRoot = Join-Path $distRoot "installer"
$buildTempRoot = Join-Path $trueBimRoot "obj\installer-build"
$installerScriptPath = Join-Path $trueBimRoot "installer\TrueBIM.iss"
$defaultInstallRoot = Join-Path $env:APPDATA "TrueBIM"

. (Join-Path $PSScriptRoot "resolve-dotnet-sdk.ps1")
$dotnetPath = Resolve-DotNetSdk

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Parent,

        [Parameter(Mandatory = $true)]
        [string] $Child
    )

    $parentPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\')
    $childPath = [System.IO.Path]::GetFullPath($Child).TrimEnd('\')
    if (-not $childPath.StartsWith($parentPath + "\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate on '$childPath' because it is outside '$parentPath'."
    }
}

function Remove-DirectorySafe {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    Assert-ChildPath -Parent $repoRootPath -Child $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
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

function Resolve-InnoCompiler {
    param(
        [string] $RequestedPath
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (Test-Path -LiteralPath $RequestedPath) {
            return $RequestedPath
        }

        throw "Inno Setup compiler was not found at '$RequestedPath'."
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles} "Inno Setup 6\ISCC.exe")
    )

    $command = Get-Command iscc -ErrorAction SilentlyContinue
    if ($command) {
        $candidates += $command.Source
    }

    foreach ($candidate in ($candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Inno Setup compiler was not found. Install Inno Setup 6 or pass -InnoCompilerPath."
}

function New-AddinManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $AssemblyPath
    )

    $manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>TrueBIM</Name>
    <Assembly>$AssemblyPath</Assembly>
    <AddInId>8F8E8CC7-D3C9-49BA-8F40-AD0F2F8D32F7</AddInId>
    <FullClassName>TrueBIM.App.App</FullClassName>
    <VendorId>TRBM</VendorId>
    <VendorDescription>TrueBIM</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

    Set-Content -LiteralPath $Path -Value $manifest -Encoding UTF8
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Source,

        [Parameter(Mandatory = $true)]
        [string] $Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Source directory was not found: '$Source'."
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
}

function Assert-Manifest {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Year
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing add-in manifest for Revit $Year at '$Path'."
    }

    [xml] $manifest = Get-Content -LiteralPath $Path
    $assemblyPath = [string] $manifest.RevitAddIns.AddIn.Assembly
    if ([string]::IsNullOrWhiteSpace($assemblyPath)) {
        throw "Manifest '$Path' does not contain an Assembly path."
    }

    if ($assemblyPath.IndexOf("\$Year\", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Manifest '$Path' points to '$assemblyPath', which does not contain the Revit year '$Year'."
    }

    if (-not $assemblyPath.EndsWith("\TrueBIM.App.dll", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Manifest '$Path' points to '$assemblyPath', not to TrueBIM.App.dll."
    }
}

function Assert-NoRevitApiPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $copiedRevitApi = Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @("RevitAPI.dll", "RevitAPIUI.dll") }
    if ($copiedRevitApi) {
        $names = ($copiedRevitApi | ForEach-Object { $_.FullName }) -join ", "
        throw "Revit API assemblies must not be copied into installer payload: $names"
    }
}

Remove-DirectorySafe -Path $distRoot
Remove-DirectorySafe -Path $buildTempRoot
New-Item -ItemType Directory -Path $distRevitRoot -Force | Out-Null
New-Item -ItemType Directory -Path $distInstallerRoot -Force | Out-Null

$results = New-Object System.Collections.Generic.List[object]
$skipped = New-Object System.Collections.Generic.List[string]

foreach ($year in $revitYears) {
    $framework = if ($year -eq "2025") { "net8.0-windows" } else { "net48" }
    $revitApiRoot = Join-Path ${env:ProgramFiles} "Autodesk\Revit $year"
    $apiPath = Join-Path $revitApiRoot "RevitAPI.dll"
    $apiUiPath = Join-Path $revitApiRoot "RevitAPIUI.dll"

    if (-not (Test-Path -LiteralPath $apiPath) -or -not (Test-Path -LiteralPath $apiUiPath)) {
        if ($AllowMissingRevitApi) {
            $skipped.Add($year) | Out-Null
            $results.Add([pscustomobject]@{
                    RevitVersion = $year
                    Framework = $framework
                    Dll = $false
                    Manifest = $false
                    RevitInstalled = Test-RevitInstalled -Year $year
                    Result = "Skipped: Revit API assemblies were not found"
                }) | Out-Null
            continue
        }

        throw "Revit API assemblies were not found for Revit $year under '$revitApiRoot'."
    }

    $tempOutputDir = Join-Path $buildTempRoot "$year\bin"
    $tempObjDir = Join-Path $buildTempRoot "$year\obj"
    New-Item -ItemType Directory -Path $tempOutputDir -Force | Out-Null
    New-Item -ItemType Directory -Path $tempObjDir -Force | Out-Null

    & $dotnetPath build $projectPath `
        --configuration $Configuration `
        --framework $framework `
        --nologo `
        --verbosity:minimal `
        "-p:RevitVersion=$year" `
        "-p:OutputPath=$tempOutputDir\" `
        "-p:IntermediateOutputPath=$tempObjDir\"
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for Revit $year ($framework)."
    }

    $appAssembly = Join-Path $tempOutputDir "TrueBIM.App.dll"
    if (-not (Test-Path -LiteralPath $appAssembly)) {
        throw "Build output was not found for Revit $year at '$appAssembly'."
    }

    $distYearRoot = Join-Path $distRevitRoot $year
    New-Item -ItemType Directory -Path $distYearRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $tempOutputDir "*") -Destination $distYearRoot -Recurse -Force

    Copy-DirectoryContents -Source (Join-Path $trueBimRoot "modules\print") -Destination (Join-Path $distYearRoot "Modules\Print")
    Copy-DirectoryContents -Source (Join-Path $trueBimRoot "modules\sheet-numbering") -Destination (Join-Path $distYearRoot "Modules\SheetNumbering")
    Copy-DirectoryContents -Source (Join-Path $trueBimRoot "modules\schedule-column-collapse") -Destination (Join-Path $distYearRoot "Modules\ScheduleColumnCollapse")
    Copy-DirectoryContents -Source (Join-Path $trueBimRoot "assets\icons") -Destination (Join-Path $distYearRoot "Assets\icons")

    $docsDir = Join-Path $distYearRoot "Docs"
    New-Item -ItemType Directory -Path $docsDir -Force | Out-Null
    Copy-Item -Path (Join-Path $trueBimRoot "README.md") -Destination $docsDir -Force
    Copy-Item -Path (Join-Path $trueBimRoot "docs\*.md") -Destination $docsDir -Force
    Copy-Item -Path (Join-Path $trueBimRoot "installer\README.md") -Destination (Join-Path $docsDir "installer-README.md") -Force
    $docsAssetsDir = Join-Path $trueBimRoot "docs\assets"
    if (Test-Path -LiteralPath $docsAssetsDir) {
        Copy-DirectoryContents -Source $docsAssetsDir -Destination (Join-Path $docsDir "assets")
    }

    $manifestPath = Join-Path $distYearRoot "TrueBIM.addin"
    $defaultAssemblyPath = Join-Path (Join-Path $defaultInstallRoot $year) "TrueBIM.App.dll"
    New-AddinManifest -Path $manifestPath -AssemblyPath $defaultAssemblyPath

    Assert-Manifest -Path $manifestPath -Year $year
    Assert-NoRevitApiPayload -Path $distYearRoot

    $results.Add([pscustomobject]@{
            RevitVersion = $year
            Framework = $framework
            Dll = Test-Path -LiteralPath (Join-Path $distYearRoot "TrueBIM.App.dll")
            Manifest = Test-Path -LiteralPath $manifestPath
            RevitInstalled = Test-RevitInstalled -Year $year
            Result = "Ready"
        }) | Out-Null
}

if ($skipped.Count -gt 0 -and -not $SkipInstaller) {
    $SkipInstaller = $true
    Write-Warning "Installer compilation was skipped because these Revit versions were not built: $($skipped -join ', ')."
}

if (-not $SkipInstaller) {
    $innoPath = Resolve-InnoCompiler -RequestedPath $InnoCompilerPath
    Push-Location -LiteralPath (Split-Path -Parent $installerScriptPath)
    try {
        & $innoPath $installerScriptPath
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup compiler failed."
        }
    }
    finally {
        Pop-Location
    }
}

$reportPath = Join-Path $distRoot "build-report.json"
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Host ""
Write-Host "TrueBIM multi-version package report:"
$results | Format-Table -AutoSize
Write-Host "dist/revit: $distRevitRoot"
Write-Host "dist/installer: $distInstallerRoot"
Write-Host "Report: $reportPath"

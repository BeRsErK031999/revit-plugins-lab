$ErrorActionPreference = "Stop"

$checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param(
        [string] $Name,
        [bool] $Passed,
        [string] $Message
    )

    $checks.Add([pscustomobject]@{
        Name = $Name
        Passed = $Passed
        Message = $Message
    }) | Out-Null

    $status = if ($Passed) { "PASS" } else { "FAIL" }
    Write-Host "[$status] $Name - $Message"
}

function Invoke-Checked {
    param(
        [string] $Name,
        [scriptblock] $Command
    )

    try {
        & $Command
        Add-Check -Name $Name -Passed $true -Message "Completed."
    }
    catch {
        Add-Check -Name $Name -Passed $false -Message $_.Exception.Message
        throw
    }
}

function Test-AbsolutePath {
    param([string] $Path)

    return [System.IO.Path]::IsPathRooted($Path) -and -not $Path.Contains("%")
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$solutionPath = Join-Path $repoRoot "TrueBIM.sln"
$dotnetPath = "C:\Program Files\dotnet\dotnet.exe"
$buildInstallerScript = Join-Path $repoRoot "plugins\truebim\scripts\build-installer.ps1"
$deployScript = Join-Path $repoRoot "plugins\truebim\scripts\deploy-local-2025.ps1"
$isccPath = "C:\Users\Borodin_Artem\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
$addinPath = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2025\TrueBIM.addin"
$sheetNumberingManifestPath = Join-Path $env:APPDATA "TrueBIM\2025\Modules\SheetNumbering\module.json"
$scheduleColumnCollapseManifestPath = Join-Path $env:APPDATA "TrueBIM\2025\Modules\ScheduleColumnCollapse\module.json"
$moduleSettingsPath = Join-Path $env:APPDATA "TrueBIM\2025\module-settings.json"

try {
    if (-not (Test-Path $dotnetPath)) {
        throw "Required .NET SDK host was not found at '$dotnetPath'."
    }

    Add-Check -Name ".NET SDK path" -Passed $true -Message $dotnetPath

    Invoke-Checked -Name "Release build" -Command {
        & $dotnetPath build $solutionPath --configuration Release --nologo --verbosity:minimal
    }

    Invoke-Checked -Name "Release tests" -Command {
        & $dotnetPath test $solutionPath --configuration Release --nologo --verbosity:minimal
    }

    if (Test-Path $isccPath) {
        Invoke-Checked -Name "Build multi-version installer" -Command {
            & $buildInstallerScript -InnoCompilerPath $isccPath
        }
    }
    else {
        Add-Check -Name "Build multi-version installer" -Passed $true -Message "Skipped because ISCC.exe was not found at '$isccPath'."
    }

    $runningRevit = Get-Process -Name "Revit" -ErrorAction SilentlyContinue
    if ($runningRevit) {
        $message = "Revit is running. Close Revit before qa-preflight deploy so TrueBIM.App.dll is not locked."
        Add-Check -Name "Revit closed before deploy" -Passed $false -Message $message
        throw $message
    }

    Add-Check -Name "Revit closed before deploy" -Passed $true -Message "No Revit process detected."

    Invoke-Checked -Name "Local deploy" -Command {
        & $deployScript -Configuration Release
    }

    if (-not (Test-Path $addinPath)) {
        throw "Installed add-in manifest was not found at '$addinPath'."
    }

    Add-Check -Name "Installed add-in manifest" -Passed $true -Message $addinPath

    [xml] $addin = Get-Content $addinPath
    $assemblyPath = [string] $addin.RevitAddIns.AddIn.Assembly
    if (-not (Test-AbsolutePath $assemblyPath)) {
        throw "Add-in Assembly path is not absolute: '$assemblyPath'."
    }

    if (-not (Test-Path $assemblyPath)) {
        throw "Add-in Assembly DLL was not found at '$assemblyPath'."
    }

    Add-Check -Name "Add-in Assembly path" -Passed $true -Message $assemblyPath

    if (-not (Test-Path $sheetNumberingManifestPath)) {
        throw "Installed Sheet Numbering module manifest was not found at '$sheetNumberingManifestPath'."
    }

    Add-Check -Name "Installed Sheet Numbering manifest" -Passed $true -Message $sheetNumberingManifestPath

    if (-not (Test-Path $scheduleColumnCollapseManifestPath)) {
        throw "Installed Schedule Column Collapse module manifest was not found at '$scheduleColumnCollapseManifestPath'."
    }

    Add-Check -Name "Installed Schedule Column Collapse manifest" -Passed $true -Message $scheduleColumnCollapseManifestPath

    if (Test-Path $moduleSettingsPath) {
        Add-Check -Name "Module settings" -Passed $true -Message "Optional settings file exists: '$moduleSettingsPath'."
    }
    else {
        Add-Check -Name "Module settings" -Passed $true -Message "Optional settings file is absent; manifest defaults will be used."
    }
}
finally {
    Write-Host ""
    Write-Host "TrueBIM Revit 2025 QA preflight summary"
    Write-Host "---------------------------------------"
    foreach ($check in $checks) {
        $status = if ($check.Passed) { "PASS" } else { "FAIL" }
        Write-Host ("[{0}] {1}: {2}" -f $status, $check.Name, $check.Message)
    }
}

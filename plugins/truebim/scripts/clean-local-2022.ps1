param(
    [switch] $IncludeUserData
)

$ErrorActionPreference = "Stop"

function Test-PathInside {
    param(
        [string] $CandidatePath,
        [string] $AllowedRoot
    )

    $resolvedCandidate = [System.IO.Path]::GetFullPath($CandidatePath)
    $resolvedRoot = [System.IO.Path]::GetFullPath($AllowedRoot)

    if (-not $resolvedRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $resolvedRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    return $resolvedCandidate.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)
}

function Remove-SafePath {
    param(
        [string] $Path,
        [string[]] $AllowedRoots
    )

    if (-not (Test-Path $Path)) {
        Write-Host "Skip missing path: $Path"
        return
    }

    $resolvedPath = (Resolve-Path $Path).Path
    $isAllowed = $false
    foreach ($allowedRoot in $AllowedRoots) {
        if (Test-PathInside -CandidatePath $resolvedPath -AllowedRoot $allowedRoot) {
            $isAllowed = $true
            break
        }
    }

    if (-not $isAllowed) {
        throw "Refusing to delete '$resolvedPath' because it is outside the allowed TrueBIM/Revit add-ins roots."
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    Write-Host "Removed $resolvedPath"
}

$trueBimRoot = Join-Path $env:APPDATA "TrueBIM"
$trueBim2022Root = Join-Path $trueBimRoot "2022"
$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2022"
$addinPath = Join-Path $addinRoot "TrueBIM.addin"
$corePath = Join-Path $trueBim2022Root "Core"
$modulesPath = Join-Path $trueBim2022Root "Modules"
$assetsPath = Join-Path $trueBim2022Root "Assets"
$settingsPath = Join-Path $trueBim2022Root "module-settings.json"
$logsPath = Join-Path $trueBimRoot "Logs"
$exportsPath = Join-Path $trueBimRoot "Exports"

Remove-SafePath -Path $addinPath -AllowedRoots @($addinRoot)
Remove-SafePath -Path $corePath -AllowedRoots @($trueBimRoot)
Remove-SafePath -Path $modulesPath -AllowedRoots @($trueBimRoot)
Remove-SafePath -Path $assetsPath -AllowedRoots @($trueBimRoot)

if ($IncludeUserData) {
    Remove-SafePath -Path $settingsPath -AllowedRoots @($trueBimRoot)
    Remove-SafePath -Path $logsPath -AllowedRoots @($trueBimRoot)
    Remove-SafePath -Path $exportsPath -AllowedRoots @($trueBimRoot)
}
else {
    Write-Host "User data preserved. Pass -IncludeUserData to remove logs, exports, and module-settings.json."
}

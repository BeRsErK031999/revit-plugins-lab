param(
    [string] $DotNetPath = '',
    [string] $InnoCompilerPath = '',
    [string] $RevitApiRoot = 'C:\Program Files\Autodesk\Revit 2024',
    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $pluginRoot '..\..'))

if (-not $DotNetPath) {
    $sdkCandidates = @(
        (Join-Path $repositoryRoot 'tools\.dotnet\dotnet.exe'),
        (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    )
    foreach ($sdkCandidate in $sdkCandidates) {
        if (Test-Path -LiteralPath $sdkCandidate) {
            $sdks = & $sdkCandidate --list-sdks
            if ($LASTEXITCODE -eq 0 -and $sdks) { $DotNetPath = $sdkCandidate; break }
        }
    }
}
if (-not $DotNetPath) { throw 'Install .NET SDK 8 or pass -DotNetPath.' }

$testProject = Join-Path $pluginRoot 'tests\Vernadskogo.Tests\Vernadskogo.Tests.csproj'
& $DotNetPath test $testProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Vernadskogo tests failed.' }

$project = Join-Path $pluginRoot 'src\Vernadskogo.App\Vernadskogo.App.csproj'
& $DotNetPath build $project -c Release --nologo "-p:RevitApiRoot=$RevitApiRoot"
if ($LASTEXITCODE -ne 0) { throw 'Vernadskogo build failed.' }

$payload = Join-Path $repositoryRoot 'dist\vernadskogo\2024'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $pluginRoot 'src\Vernadskogo.App\bin\Release\net48\Vernadskogo.App.dll') -Destination $payload -Force

if (-not $SkipInstaller) {
    if (-not $InnoCompilerPath) {
        $compilerCandidates = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )
        $InnoCompilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $InnoCompilerPath) { throw 'Install Inno Setup 6 or pass -InnoCompilerPath.' }
    & $InnoCompilerPath (Join-Path $pluginRoot 'installer\Vernadskogo.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Vernadskogo installer build failed.' }
    Write-Output (Join-Path $repositoryRoot 'dist\vernadskogo\installer\Vernadskogo-Revit2024-Setup.exe')
}

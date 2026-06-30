function Resolve-DotNetSdk {
    $candidatePaths = New-Object System.Collections.Generic.List[string]

    function Add-CandidatePath {
        param([string] $Path)

        if (-not [string]::IsNullOrWhiteSpace($Path)) {
            $candidatePaths.Add($Path) | Out-Null
        }
    }

    if ($env:DOTNET_ROOT) {
        Add-CandidatePath (Join-Path $env:DOTNET_ROOT "dotnet.exe")
    }

    if ($env:USERPROFILE) {
        Add-CandidatePath (Join-Path $env:USERPROFILE ".dotnet\dotnet.exe")
    }

    if ($env:ProgramFiles) {
        Add-CandidatePath (Join-Path $env:ProgramFiles "dotnet\dotnet.exe")
    }

    if (${env:ProgramFiles(x86)}) {
        Add-CandidatePath (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")
    }

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) {
        Add-CandidatePath $dotnetCommand.Source
    }

    foreach ($candidatePath in ($candidatePaths | Select-Object -Unique)) {
        if (-not (Test-Path $candidatePath)) {
            continue
        }

        $sdks = & $candidatePath --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) {
            return $candidatePath
        }
    }

    throw "Required .NET SDK was not found. Install the SDK requested by global.json or set DOTNET_ROOT."
}

param(
    [string] $InstallerPath = '',
    [string] $ReleaseDirectory = 'W:\Shared\RevitPlugins',
    [string] $PreviousInstallerPath = ''
)
$ErrorActionPreference = 'Stop'
$publishRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if (-not $InstallerPath) { $InstallerPath = Join-Path $publishRepo 'dist\installer\TrueBIM-Setup.exe' }
$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $ReleaseDirectory -PathType Container)) { throw "Release folder is unavailable: $ReleaseDirectory" }
$publishVersion = (Get-Item -LiteralPath $InstallerPath).VersionInfo.ProductVersion.Trim()
if ($publishVersion -notmatch '^\d+\.\d+\.\d+$' -or (Get-Item -LiteralPath $InstallerPath).VersionInfo.ProductName.Trim() -ne 'TrueBIM') { throw 'Not a versioned TrueBIM installer.' }
[xml] $publishProperties = Get-Content -LiteralPath (Join-Path $publishRepo 'plugins\truebim\Version.props') -Raw
if ($publishVersion -ne [string]$publishProperties.Project.PropertyGroup.Version) { throw 'Installer and source versions differ.' }
$publishHash = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash
$publishName = "TrueBIM-Setup-$publishVersion.exe"
$publishManifestPath = Join-Path $ReleaseDirectory 'TrueBIM-release.json'
$publishOriginalManifest = if (Test-Path -LiteralPath $publishManifestPath) { [IO.File]::ReadAllText($publishManifestPath) } else { $null }

function Assert-PublishedPackage($Package) {
    if (-not $Package -or $Package.version -notmatch '^\d+\.\d+\.\d+$' -or $Package.file -cne "TrueBIM-Setup-$($Package.version).exe" -or $Package.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid published package metadata.' }
    $publishPackagePath = Join-Path $ReleaseDirectory $Package.file
    if ((Get-FileHash -LiteralPath $publishPackagePath -Algorithm SHA256).Hash -ne $Package.sha256) { throw "Published hash mismatch: $publishPackagePath" }
    if ((Get-Item -LiteralPath $publishPackagePath).VersionInfo.ProductVersion.Trim() -ne $Package.version) { throw 'Published version mismatch.' }
}
function Copy-ImmutableInstaller([string] $Source, [string] $Name, [string] $Hash) {
    $publishTarget = Join-Path $ReleaseDirectory $Name
    if (Test-Path -LiteralPath $publishTarget) {
        if ((Get-FileHash -LiteralPath $publishTarget -Algorithm SHA256).Hash -ne $Hash) { throw "A different installer is already published as $Name. Bump the version." }
        return
    }
    $publishTemporary = Join-Path $ReleaseDirectory ($Name + '.' + [Guid]::NewGuid().ToString('N') + '.upload')
    try {
        Copy-Item -LiteralPath $Source -Destination $publishTemporary
        if ((Get-FileHash -LiteralPath $publishTemporary -Algorithm SHA256).Hash -ne $Hash) { throw "Uploaded hash mismatch: $Name" }
        Move-Item -LiteralPath $publishTemporary -Destination $publishTarget
    } finally { if (Test-Path -LiteralPath $publishTemporary) { Remove-Item -LiteralPath $publishTemporary } }
}

if ($publishOriginalManifest) {
    $publishOriginal = $publishOriginalManifest | ConvertFrom-Json
    if ($publishOriginal.schemaVersion -ne 1 -or $publishOriginal.product -ne 'TrueBIM') { throw 'Unsupported existing release manifest.' }
    Assert-PublishedPackage $publishOriginal.latest
    Assert-PublishedPackage $publishOriginal.previous
    if ([version]$publishOriginal.latest.version -gt [version]$publishVersion) { throw 'Refusing to downgrade the published release.' }
    $publishPrevious = if ($publishOriginal.latest.version -eq $publishVersion) { $publishOriginal.previous } else { $publishOriginal.latest }
} else {
    if (-not $PreviousInstallerPath) { throw 'First publication requires a verified previous installer.' }
    $publishPreviousVersion = (Get-Item -LiteralPath $PreviousInstallerPath).VersionInfo.ProductVersion.Trim()
    if ($publishPreviousVersion -notmatch '^\d+\.\d+\.\d+$' -or [version]$publishPreviousVersion -ge [version]$publishVersion) { throw 'Invalid previous version.' }
    $publishPreviousHash = (Get-FileHash -LiteralPath $PreviousInstallerPath -Algorithm SHA256).Hash
    $publishPrevious = [ordered]@{ version=$publishPreviousVersion; file="TrueBIM-Setup-$publishPreviousVersion.exe"; sha256=$publishPreviousHash }
    Copy-ImmutableInstaller $PreviousInstallerPath $publishPrevious.file $publishPrevious.sha256
}
Copy-ImmutableInstaller $InstallerPath $publishName $publishHash
$publishLatest = [ordered]@{ version=$publishVersion; file=$publishName; sha256=$publishHash }
Assert-PublishedPackage $publishLatest
Assert-PublishedPackage $publishPrevious
$publishManifest = [ordered]@{ schemaVersion=1; product='TrueBIM'; latest=$publishLatest; previous=$publishPrevious }
$publishManifestTemporary = Join-Path $ReleaseDirectory ('TrueBIM-release.' + [Guid]::NewGuid().ToString('N') + '.upload')
$publishManifestBackup = $publishManifestTemporary + '.backup'
try {
    [IO.File]::WriteAllText($publishManifestTemporary, ($publishManifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    $publishCurrentManifest = if (Test-Path -LiteralPath $publishManifestPath) { [IO.File]::ReadAllText($publishManifestPath) } else { $null }
    if ($publishCurrentManifest -cne $publishOriginalManifest) { throw 'The release manifest changed during publication; review the concurrent release.' }
    if ($null -eq $publishOriginalManifest) { Move-Item -LiteralPath $publishManifestTemporary -Destination $publishManifestPath }
    else { [IO.File]::Replace($publishManifestTemporary, $publishManifestPath, $publishManifestBackup) }
} finally {
    foreach ($publishScratch in @($publishManifestTemporary, $publishManifestBackup)) {
        if (Test-Path -LiteralPath $publishScratch) { Remove-Item -LiteralPath $publishScratch }
    }
}
Assert-PublishedPackage (Get-Content -LiteralPath $publishManifestPath -Raw | ConvertFrom-Json).latest
$publishManifest | ConvertTo-Json -Depth 5

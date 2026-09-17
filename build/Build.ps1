[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $Installer
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'AsusHardwareService.csproj'
$runtime = 'win-x64'
$publishDir = Join-Path $repoRoot "artifacts/publish/$runtime"
$installerDir = Join-Path $repoRoot 'artifacts/installer'
$installerScript = Join-Path $repoRoot 'installer/AsusHardwareService.iss'

function Get-ProjectVersion {
    $value = & dotnet msbuild $project -nologo -getProperty:VersionPrefix
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read VersionPrefix from MSBuild.'
    }

    $version = ($value | Select-Object -Last 1).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "VersionPrefix '$version' must use stable MAJOR.MINOR.PATCH form."
    }

    return $version
}

function Find-InnoCompiler {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7/ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }

    return $candidates | Select-Object -First 1
}

$version = Get-ProjectVersion

# Always create artifacts from a clean staging directory so stale files cannot leak into a release.
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "Publishing ASUS Hardware Service $version ($runtime)..."
& dotnet publish $project `
    --configuration $Configuration `
    --runtime $runtime `
    --self-contained true `
    --output $publishDir

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

if (-not $Installer) {
    Write-Host "Published to $publishDir"
    return
}

$iscc = Find-InnoCompiler
if (-not $iscc) {
    throw 'Inno Setup 6 or 7 was not found. Install Inno Setup, or run this script without -Installer.'
}

if (Test-Path $installerDir) {
    Remove-Item $installerDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

Write-Host "Building Inno Setup installer with $iscc..."
& $iscc `
    "--define=AppVersion=$version" `
    "--define=PublishDir=$publishDir" `
    "--output-dir=$installerDir" `
    $installerScript

if ($LASTEXITCODE -ne 0) {
    throw 'Inno Setup compilation failed.'
}

$installerName = "AsusHardwareService-$version-win-x64-setup.exe"
$installerPath = Join-Path $installerDir $installerName
if (-not (Test-Path $installerPath)) {
    throw "Installer build completed but '$installerName' was not produced."
}

$hash = (Get-FileHash -Path $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$installerPath.sha256"
"$hash  $installerName" | Set-Content -Path $checksumPath -Encoding ascii

Write-Host "Installer: $installerPath"
Write-Host "Checksum:  $checksumPath"

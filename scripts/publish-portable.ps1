#requires -Version 5.1
<#
.SYNOPSIS
  Builds the portable, self-contained, single-file x64 release of SimpleRay.

  Produces dist\SimpleRay-<version>-win-x64.zip (+ .sha256) containing:
    SimpleRay.exe   (single-file, .NET runtime bundled)
    core\           (sing-box.exe, wintun.dll)
    geo\            (geoip/geosite *.srs)

  Run scripts\fetch-deps.ps1 first so the runtime binaries exist.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$AppProj  = Join-Path $RepoRoot "src\SimpleRay.App\SimpleRay.App.csproj"
$Runtime  = Join-Path $RepoRoot "src\SimpleRay.App\runtime"
$PubDir   = Join-Path $RepoRoot "publish\portable"
$DistDir  = Join-Path $RepoRoot "dist"

# --- Preconditions --------------------------------------------------------
$singBox = Join-Path $Runtime "core\sing-box.exe"
if (-not (Test-Path $singBox)) {
    throw "Runtime deps missing ($singBox). Run scripts\fetch-deps.ps1 first."
}

# Read <Version> from the csproj.
[xml]$csproj = Get-Content $AppProj
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { throw "Could not read <Version> from $AppProj" }
Write-Host "Building SimpleRay $version (portable, win-x64, single-file)" -ForegroundColor Cyan

# --- Publish --------------------------------------------------------------
if (Test-Path $PubDir) { Remove-Item -Recurse -Force $PubDir }

dotnet publish $AppProj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $PubDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# Drop stray symbol files if any slipped through.
Get-ChildItem -Recurse $PubDir -Include *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

# Copy the runtime deps next to the single-file exe (kept out of the bundle on purpose).
Copy-Item (Join-Path $Runtime "core") (Join-Path $PubDir "core") -Recurse -Force
Copy-Item (Join-Path $Runtime "geo")  (Join-Path $PubDir "geo")  -Recurse -Force
# Never ship a stale generated config.
Remove-Item (Join-Path $PubDir "core\config.json") -Force -ErrorAction SilentlyContinue

Write-Host "Published files:" -ForegroundColor Green
Get-ChildItem -Recurse $PubDir | ForEach-Object {
    $_.FullName.Substring($PubDir.Length + 1)
} | Sort-Object

# --- Package --------------------------------------------------------------
New-Item -ItemType Directory -Force $DistDir | Out-Null
$zip = Join-Path $DistDir "SimpleRay-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $PubDir "*") -DestinationPath $zip

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Set-Content -Path "$zip.sha256" -Value "$hash *SimpleRay-$version-win-x64.zip" -Encoding ascii

Write-Host "`nPackaged:" -ForegroundColor Cyan
Write-Host "  $zip"
Write-Host "  SHA256: $hash"

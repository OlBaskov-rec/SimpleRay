#requires -Version 5.1
<#
.SYNOPSIS
  Builds the current code into ready-to-run artifacts and drops them in one pickup folder.

.DESCRIPTION
  Produces, in -OutDir (default: <repo>\build):
    SimpleRay-<version>-win-x64.zip        portable (unzip, run SimpleRay.exe)
    SimpleRay-<version>-win-x64.zip.sha256
    SimpleRay-<version>-setup.exe           per-user installer (if Inno Setup is present)

  Steps: fetch runtime deps if missing -> publish portable -> build installer -> copy to OutDir.
  Requires the .NET 8 SDK on PATH. The installer step also needs Inno Setup 6 (ISCC.exe);
  if it's absent the script still produces the portable zip and warns.

.EXAMPLE
  ./scripts/build-local.ps1
  ./scripts/build-local.ps1 -OutDir D:\SimpleRayBuilds
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo "build" }

$runtime = Join-Path $repo "src\SimpleRay.App\runtime"
$dist    = Join-Path $repo "dist"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found on PATH. Install the .NET 8 SDK first."
}

# --- 1. runtime deps (sing-box / wintun / geo) ---------------------------
if (-not (Test-Path (Join-Path $runtime "core\sing-box.exe"))) {
    Write-Host "Fetching runtime deps..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "fetch-deps.ps1")
}

# --- 2. portable package (produces dist\SimpleRay-<ver>-win-x64.zip + .sha256) ---
Write-Host "Building portable package ($Configuration)..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "publish-portable.ps1") -Configuration $Configuration

# version from the csproj (source of truth)
[xml]$csproj = Get-Content (Join-Path $repo "src\SimpleRay.App\SimpleRay.App.csproj")
$ver = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1

# --- 3. installer (optional; needs Inno Setup 6) -------------------------
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host "Building installer..." -ForegroundColor Cyan
    & $iscc "/DAppVersion=$ver" (Join-Path $PSScriptRoot "installer.iss") | Out-Null
} else {
    Write-Warning "Inno Setup (ISCC.exe) not found - skipping installer. Install: winget install JRSoftware.InnoSetup"
}

# --- 4. collect the deliverables into the pickup folder ------------------
New-Item -ItemType Directory -Force $OutDir | Out-Null
Get-ChildItem $OutDir -Filter "SimpleRay-*" -ErrorAction SilentlyContinue | Remove-Item -Force
foreach ($pattern in @(
    "SimpleRay-$ver-win-x64.zip",
    "SimpleRay-$ver-win-x64.zip.sha256",
    "SimpleRay-$ver-setup.exe"
)) {
    $src = Join-Path $dist $pattern
    if (Test-Path $src) { Copy-Item $src $OutDir -Force }
}

Write-Host "`nBuild $ver ready in: $OutDir" -ForegroundColor Green
Get-ChildItem $OutDir -Filter "SimpleRay-*" | ForEach-Object {
    "  {0,10:N2} MB  {1}" -f ($_.Length / 1MB), $_.Name
}

#requires -Version 5.1
<#
.SYNOPSIS
  Downloads and verifies SimpleRay's bundled runtime dependencies.

  Places into src\SimpleRay.App\runtime\:
    core\sing-box.exe   (SagerNet/sing-box, SHA256 cross-checked vs GitHub digest)
    core\wintun.dll     (wintun.net, Authenticode signature verified: WireGuard LLC)
    geo\*.srs           (SagerNet sing-geoip / sing-geosite rule-sets)

  Binaries are intentionally git-ignored; run this once after cloning.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Pinned versions / expected hashes.
$SingBoxVersion = "1.13.12"
$SingBoxSha256  = "E93FC531134EB1BEB4EFA3C74990A24E48456098A31C03B60D5DDF17F223CF98"
$WintunVersion  = "0.14.1"
$WintunZipSha256 = "07C256185D6EE3652E09FA55C0B673E2624B565E02C4B9091C79CA7D2F24EF51"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Runtime  = Join-Path $RepoRoot "src\SimpleRay.App\runtime"
$CoreDir  = Join-Path $Runtime "core"
$GeoDir   = Join-Path $Runtime "geo"
$Tmp      = Join-Path $env:TEMP ("simpleray_deps_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $CoreDir, $GeoDir, $Tmp | Out-Null

function Assert-Hash($path, $expected, $what) {
    $actual = (Get-FileHash $path -Algorithm SHA256).Hash
    if ($actual -ne $expected) {
        throw "$what hash mismatch.`n  expected: $expected`n  actual:   $actual"
    }
    Write-Host "  [ok] SHA256 verified: $what" -ForegroundColor Green
}

try {
    # --- sing-box ---------------------------------------------------------
    Write-Host "Fetching sing-box $SingBoxVersion ..."
    $sbZip = Join-Path $Tmp "sing-box.zip"
    $sbUrl = "https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/sing-box-$SingBoxVersion-windows-amd64.zip"
    Invoke-WebRequest -Uri $sbUrl -OutFile $sbZip -UseBasicParsing
    Assert-Hash $sbZip $SingBoxSha256 "sing-box zip"
    Expand-Archive -Path $sbZip -DestinationPath $Tmp -Force
    $sbExe = Get-ChildItem -Recurse $Tmp -Filter "sing-box.exe" | Select-Object -First 1
    Copy-Item $sbExe.FullName (Join-Path $CoreDir "sing-box.exe") -Force

    # --- wintun -----------------------------------------------------------
    Write-Host "Fetching wintun $WintunVersion ..."
    $wtZip = Join-Path $Tmp "wintun.zip"
    Invoke-WebRequest -Uri "https://www.wintun.net/builds/wintun-$WintunVersion.zip" -OutFile $wtZip -UseBasicParsing
    Assert-Hash $wtZip $WintunZipSha256 "wintun zip"
    Expand-Archive -Path $wtZip -DestinationPath (Join-Path $Tmp "wintun") -Force
    $wtDll = Get-ChildItem -Recurse (Join-Path $Tmp "wintun") -Filter "wintun.dll" |
        Where-Object { $_.FullName -match "amd64" } | Select-Object -First 1
    $sig = Get-AuthenticodeSignature $wtDll.FullName
    if ($sig.Status -ne "Valid" -or $sig.SignerCertificate.Subject -notmatch "WireGuard LLC") {
        throw "wintun.dll Authenticode check failed (status=$($sig.Status), signer=$($sig.SignerCertificate.Subject))"
    }
    Write-Host "  [ok] Authenticode verified: wintun.dll (WireGuard LLC)" -ForegroundColor Green
    Copy-Item $wtDll.FullName (Join-Path $CoreDir "wintun.dll") -Force

    # --- geo rule-sets ----------------------------------------------------
    Write-Host "Fetching geoip/geosite rule-sets ..."
    $geo = @{
        "geoip-ru.srs"                 = "https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set/geoip-ru.srs"
        "geosite-private.srs"          = "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-private.srs"
        "geosite-category-ru.srs"      = "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-category-ru.srs"
        "geosite-category-ads-all.srs" = "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-category-ads-all.srs"
    }
    foreach ($name in $geo.Keys) {
        Invoke-WebRequest -Uri $geo[$name] -OutFile (Join-Path $GeoDir $name) -UseBasicParsing
        Write-Host "  [ok] $name"
    }

    Write-Host "`nAll dependencies fetched into $Runtime" -ForegroundColor Cyan
}
finally {
    Remove-Item -Recurse -Force $Tmp -ErrorAction SilentlyContinue
}

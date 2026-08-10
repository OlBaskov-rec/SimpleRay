#requires -Version 5.1
<#
.SYNOPSIS
  Inspects the WFP filters/sublayer the kill-switch installs, plus the TUN adapter.
  Run ELEVATED, WHILE the kill-switch is engaged (harness waiting at "Press Enter").

.NOTES
  If PowerShell blocks this script ("running scripts is disabled"), launch it with:
    powershell -ExecutionPolicy Bypass -File .\tests\manual\kill-switch\check-filters.ps1
#>
$ErrorActionPreference = "Stop"
$Name = "SimpleRay kill-switch"

function Get-WfpItems([string]$what) {
    $xml = Join-Path $env:TEMP "sr-$what.xml"
    netsh wfp show $what file=$xml | Out-Null
    if (-not (Test-Path $xml)) { return @() }
    [xml]$doc = Get-Content $xml -Raw
    Remove-Item $xml -Force -ErrorAction SilentlyContinue
    # Only items carrying our display name.
    $doc.SelectNodes('//item') | Where-Object { $_.displayData.name -eq $Name }
}

Write-Host "=== SimpleRay filters (ALE_AUTH_CONNECT v4/v6) ===" -ForegroundColor Cyan
$filters = Get-WfpItems "filters"
if (-not $filters) {
    Write-Host "  (none found — kill-switch NOT engaged, or filters were removed)" -ForegroundColor Yellow
} else {
    Write-Host ("  count: {0} (expected 6-8: block-all/permit-loopback/permit-singbox [+permit-tun] x2 layers)" -f @($filters).Count)
    @($filters) | ForEach-Object {
        # Layer rendered as a friendly key or GUID; shorten the common ones.
        $layer = "$($_.layerKey)"
        if ($layer -match 'CONNECT_V4|c38d57d1') { $layer = 'v4' }
        elseif ($layer -match 'CONNECT_V6|4a72393b') { $layer = 'v6' }
        [pscustomobject]@{
            Tag        = $_.displayData.description
            Layer      = $layer
            Action     = "$($_.action.type)"
            Conds      = "$($_.numFilterConditions)"
            SpecWeight = "$($_.weight.uint8)$($_.weight.uint64)"
            EffWeight  = "$($_.effectiveWeight.uint64)"
        }
    } | Format-Table -AutoSize
}

Write-Host "=== SimpleRay sublayer (want a high weight, e.g. 65535) ===" -ForegroundColor Cyan
$subs = Get-WfpItems "sublayers"
if (-not $subs) {
    Write-Host "  (none found)" -ForegroundColor Yellow
} else {
    @($subs) | ForEach-Object {
        $w = if ($_.weight.uint16) { $_.weight.uint16 } else { "$($_.weight)".Trim() }
        [pscustomobject]@{ Weight = $w; Key = "$($_.subLayerKey)" }
    } | Format-Table -AutoSize
}

Write-Host "=== TUN adapter (expected name: simpleray, only while sing-box runs) ===" -ForegroundColor Cyan
Get-NetAdapter -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -like '*simpleray*' -or $_.InterfaceDescription -like '*wintun*' } |
  Format-Table Name, ifIndex, InterfaceDescription, Status

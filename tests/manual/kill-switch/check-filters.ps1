#requires -Version 5.1
<#
.SYNOPSIS
  Inspects the WFP filters/sublayer the kill-switch installs, plus the TUN adapter.
  Run ELEVATED, WHILE the kill-switch is engaged (harness waiting at "Press Enter").

.NOTES
  ASCII only on purpose: PowerShell 5.1 reads .ps1 as the system ANSI codepage, so
  non-ASCII chars corrupt the file. If PowerShell blocks it ("running scripts is
  disabled"), launch with:
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
    $doc.SelectNodes('//item') | Where-Object { $_.displayData.name -eq $Name }
}

Write-Host "=== SimpleRay filters (ALE_AUTH_CONNECT v4/v6) ===" -ForegroundColor Cyan
$filters = Get-WfpItems "filters"
if (-not $filters) {
    Write-Host "  (none found: kill-switch NOT engaged, or filters were removed)" -ForegroundColor Yellow
} else {
    Write-Host ("  count: {0} (expect 6-8: block-all/permit-loopback/permit-singbox [+permit-tun] x2 layers)" -f @($filters).Count)
    @($filters) | ForEach-Object {
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
$SubKey = "6f1e2a55-9b7c-4c3a-8d21-5a2f9c0b1e77"   # WfpKillSwitch.SubLayerKey
$sxml = Join-Path $env:TEMP "sr-sublayers.xml"
netsh wfp show sublayers file=$sxml | Out-Null
$subs = @()
if (Test-Path $sxml) {
    [xml]$sdoc = Get-Content $sxml -Raw
    # Match by our display name OR our stable sublayer key (name matching alone missed it before).
    $subs = $sdoc.SelectNodes('//item') | Where-Object {
        $_.displayData.name -eq $Name -or "$($_.subLayerKey.'#text')$($_.subLayerKey)" -match $SubKey
    }
    Remove-Item $sxml -Force -ErrorAction SilentlyContinue
}
if (-not $subs) {
    Write-Host "  (none found) -> our sublayer is missing; filters would fall to the default sublayer" -ForegroundColor Yellow
} else {
    @($subs) | ForEach-Object {
        $w = if ($_.weight.uint16) { $_.weight.uint16 }
             elseif ($_.weight.'#text') { $_.weight.'#text' }
             else { "$($_.weight)".Trim() }
        [pscustomobject]@{ Name = "$($_.displayData.name)"; Weight = $w; Key = "$($_.subLayerKey.'#text')$($_.subLayerKey)".Trim() }
    } | Format-Table -AutoSize
}

Write-Host "=== TUN adapter (name simpleray, present only while sing-box runs) ===" -ForegroundColor Cyan
Get-NetAdapter -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -like '*simpleray*' -or $_.InterfaceDescription -like '*wintun*' } |
  Format-Table Name, ifIndex, InterfaceDescription, Status

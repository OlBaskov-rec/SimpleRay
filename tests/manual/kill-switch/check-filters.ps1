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

Write-Host "=== SimpleRay sublayer ===" -ForegroundColor Cyan
$SubKey = "6f1e2a55-9b7c-4c3a-8d21-5a2f9c0b1e77"   # WfpKillSwitch.SubLayerKey
$sxml = Join-Path $env:TEMP "sr-sublayers.xml"
netsh wfp show sublayers file=$sxml | Out-Null
if (Test-Path $sxml) {
    # Raw-text search: robust vs the exact XML shape (XML-node parsing gave false negatives).
    $raw = Get-Content $sxml -Raw
    if ($raw -match [regex]::Escape($SubKey)) {
        Write-Host "  present (key $SubKey found)" -ForegroundColor Green
        # Best-effort weight: first <weight>...<uint16>N</uint16> after our key.
        $tail = $raw.Substring($raw.IndexOf($SubKey))
        if ($tail -match '(?s)<weight>.*?<uint16>(\d+)</uint16>') {
            Write-Host ("  weight: {0} (want 65535)" -f $matches[1])
        }
    } else {
        Write-Host "  (our sublayer key NOT in netsh output) -> filters fell to the default sublayer" -ForegroundColor Yellow
    }
    Remove-Item $sxml -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "  (netsh produced no output - run this window elevated)" -ForegroundColor Yellow
}

Write-Host "=== TUN adapter (name simpleray, present only while sing-box runs) ===" -ForegroundColor Cyan
Get-NetAdapter -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -like '*simpleray*' -or $_.InterfaceDescription -like '*wintun*' } |
  Format-Table Name, ifIndex, InterfaceDescription, Status

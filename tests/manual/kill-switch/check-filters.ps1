#requires -Version 5.1
<#
.SYNOPSIS
  Shows the WFP filters and TUN adapter relevant to the kill-switch. Run elevated.
#>
$xml = Join-Path $env:TEMP "sr-filters.xml"
netsh wfp show filters file=$xml | Out-Null

Write-Host "=== WFP filters named 'SimpleRay kill-switch' ===" -ForegroundColor Cyan
if (Test-Path $xml) {
    $hits = Select-String -Path $xml -Pattern "SimpleRay kill-switch"
    if ($hits) { $hits | ForEach-Object { $_.Line.Trim() } }
    else { Write-Host "  (none — kill-switch not engaged, or filters removed)" }
    Remove-Item $xml -Force -ErrorAction SilentlyContinue
}

Write-Host "`n=== TUN adapter (expected name: simpleray) ===" -ForegroundColor Cyan
Get-NetAdapter -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -like '*simpleray*' -or $_.InterfaceDescription -like '*wintun*' } |
  Format-Table Name, ifIndex, InterfaceDescription, Status

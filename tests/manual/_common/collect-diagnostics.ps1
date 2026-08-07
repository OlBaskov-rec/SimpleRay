#requires -Version 5.1
<#
.SYNOPSIS
  Collects everything useful for a bug report into one text file on the Desktop.
  Run from an elevated PowerShell. Attach the resulting file when reporting an issue.
#>
$ErrorActionPreference = "Continue"
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) ("simpleray-diag-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".txt")

function Section($title) { "`n===== $title =====" | Out-File $out -Append -Encoding utf8 }

"SimpleRay diagnostics $(Get-Date -Format o)" | Out-File $out -Encoding utf8

Section "Windows"
[System.Environment]::OSVersion.VersionString | Out-File $out -Append -Encoding utf8
"Elevated: $((New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole('Administrator'))" | Out-File $out -Append -Encoding utf8

Section "TUN adapter (expected name: simpleray)"
Get-NetAdapter -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -like '*simpleray*' -or $_.InterfaceDescription -like '*wintun*' } |
  Format-List Name, InterfaceDescription, ifIndex, Status | Out-File $out -Append -Encoding utf8

Section "WFP filters named 'SimpleRay'"
$xml = Join-Path $env:TEMP "sr-filters.xml"
netsh wfp show filters file=$xml | Out-Null
if (Test-Path $xml) {
  (Select-String -Path $xml -Pattern "SimpleRay" -Context 0,6 | Out-String) | Out-File $out -Append -Encoding utf8
  Remove-Item $xml -Force -ErrorAction SilentlyContinue
}

Section "last-error.txt"
$err = Join-Path $env:APPDATA "SimpleRay\last-error.txt"
if (Test-Path $err) { Get-Content $err -Raw | Out-File $out -Append -Encoding utf8 } else { "none" | Out-File $out -Append -Encoding utf8 }

Section "running processes"
Get-Process -Name SimpleRay, sing-box -ErrorAction SilentlyContinue |
  Format-Table Name, Id, StartTime | Out-File $out -Append -Encoding utf8

Write-Host "Diagnostics written to: $out" -ForegroundColor Green

#requires -Version 5.1
<#
.SYNOPSIS
  Adds one localization key to ALL nine language files at once, then regenerates LocKeys.
  The 8 non-English files get the English text as a placeholder (translate later). Atomic:
  every file is validated and rendered in memory first, then all are written together, so a
  failure never leaves the set half-updated. Existing values are preserved byte-for-byte
  (textual append), so no re-escaping of Unicode.

.EXAMPLE
  ./scripts/loc-add-key.ps1 -Key "status.reconnected" -En "Reconnected"
#>
param(
    [Parameter(Mandatory)][string]$Key,
    [Parameter(Mandatory)][string]$En
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root "src\SimpleRay.App\Localization\langs"
$langs = 'en', 'ru', 'uk', 'de', 'fr', 'es', 'tr', 'zh', 'fa'

$keyJson = ($Key | ConvertTo-Json -Compress)
$valJson = ($En  | ConvertTo-Json -Compress)

# Validate + render every file in memory first (atomic: no writes until all succeed).
$rendered = @{}
foreach ($l in $langs) {
    $path = Join-Path $dir "$l.json"
    if (-not (Test-Path $path)) { throw "Missing language file: $path" }
    $text = Get-Content $path -Raw -Encoding UTF8

    $obj = $text | ConvertFrom-Json
    if ($obj.PSObject.Properties.Name -contains $Key) { throw "Key '$Key' already exists in $l.json" }

    $lastBrace = $text.LastIndexOf('}')
    if ($lastBrace -lt 0) { throw "Not a JSON object: $l.json" }
    $head = $text.Substring(0, $lastBrace).TrimEnd()          # ...  "last.key": "value"
    $rendered[$l] = $head + ",`r`n  $keyJson`: $valJson`r`n}" # append as the last property
}

# All good -> write them all.
foreach ($l in $langs) {
    [System.IO.File]::WriteAllText((Join-Path $dir "$l.json"), $rendered[$l], [System.Text.UTF8Encoding]::new($false))
}

& (Join-Path $PSScriptRoot "gen-lockeys.ps1") | Out-Null
Write-Output "Added '$Key' to all $($langs.Count) languages (non-en = English placeholder); regenerated LocKeys."

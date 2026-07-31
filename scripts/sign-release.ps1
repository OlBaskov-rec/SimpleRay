<#
.SYNOPSIS
    Generates the release-signing key, or signs a release zip with it (ECDSA P-256 / SHA-256).

.DESCRIPTION
    The in-app updater accepts an update only if its .sig verifies against the public key
    embedded in UpdateSignature.PublicKeyBase64. Requires openssl on PATH.

.EXAMPLE
    # One-time: create the key pair and print the public key to embed.
    ./scripts/sign-release.ps1 -GenerateKey -KeyPath update-signing.key

.EXAMPLE
    # Per release: sign the portable zip (produces <zip>.sig next to it).
    ./scripts/sign-release.ps1 -Zip dist/SimpleRay-0.3.0-win-x64.zip -KeyPath update-signing.key
#>
[CmdletBinding(DefaultParameterSetName = 'Sign')]
param(
    [Parameter(ParameterSetName = 'Gen', Mandatory = $true)]
    [switch]$GenerateKey,

    [Parameter(ParameterSetName = 'Sign', Mandatory = $true)]
    [string]$Zip,

    [Parameter(ParameterSetName = 'Gen')]
    [Parameter(ParameterSetName = 'Sign', Mandatory = $true)]
    [string]$KeyPath = 'update-signing.key'
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
    throw "openssl not found on PATH. Install it (e.g. 'winget install ShiningLight.OpenSSL' or use Git's openssl)."
}

# Windows PowerShell 5.1 turns a native command's stderr into a terminating error under
# ErrorActionPreference=Stop, and openssl writes progress chatter to stderr. Run openssl
# with stderr captured to a file and judge success by the exit code only.
function Invoke-OpenSsl {
    param([string[]]$SslArgs)
    $errFile = New-TemporaryFile
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & openssl @SslArgs 2> $errFile.FullName
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $prev
    }
    if ($code -ne 0) {
        $err = (Get-Content $errFile.FullName -Raw)
        Remove-Item $errFile -Force -ErrorAction SilentlyContinue
        throw "openssl $($SslArgs -join ' ') failed (exit $code): $err"
    }
    Remove-Item $errFile -Force -ErrorAction SilentlyContinue
}

function Get-PublicKeyBase64([string]$key) {
    # Export the public key as DER to a temp file (binary stdout pipes are unreliable in
    # PowerShell), then base64-encode the SubjectPublicKeyInfo bytes.
    $tmp = New-TemporaryFile
    Invoke-OpenSsl @('pkey', '-in', $key, '-pubout', '-outform', 'DER', '-out', $tmp.FullName)
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($tmp.FullName))
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    return $b64
}

if ($GenerateKey) {
    if (Test-Path $KeyPath) { throw "$KeyPath already exists - refusing to overwrite a signing key." }
    Invoke-OpenSsl @('genpkey', '-algorithm', 'EC', '-pkeyopt', 'ec_paramgen_curve:P-256', '-out', $KeyPath)
    Write-Host "Private key written to $KeyPath" -ForegroundColor Green
    Write-Host "KEEP IT SECRET. Do NOT commit it. Add it as the CI secret UPDATE_SIGNING_KEY (full PEM)." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Paste this into UpdateSignature.PublicKeyBase64:" -ForegroundColor Cyan
    Write-Output (Get-PublicKeyBase64 $KeyPath)
    return
}

# Sign
if (-not (Test-Path $Zip)) { throw "Zip not found: $Zip" }
if (-not (Test-Path $KeyPath)) { throw "Key not found: $KeyPath" }
$sig = "$Zip.sig"
Invoke-OpenSsl @('dgst', '-sha256', '-sign', $KeyPath, '-out', $sig, $Zip)
if (-not (Test-Path $sig)) { throw "Signing failed." }
Write-Host "Wrote signature: $sig" -ForegroundColor Green

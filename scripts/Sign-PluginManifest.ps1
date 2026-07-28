#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ManifestPath,
    [Parameter(Mandatory = $true)] [string] $PrivateKeyPath,
    [Parameter(Mandatory = $true)] [string] $KeyId,
    [string] $LauncherPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\release\BaChen AI Launcher.exe')
)

$ErrorActionPreference = 'Stop'
$manifest = (Resolve-Path -LiteralPath $ManifestPath).Path
$privateKey = (Resolve-Path -LiteralPath $PrivateKeyPath).Path
$launcher = (Resolve-Path -LiteralPath $LauncherPath).Path
$payload = Join-Path ([System.IO.Path]::GetTempPath()) ("bachen-manifest-payload-{0}.json" -f [guid]::NewGuid().ToString('N'))

try {
    & $launcher --canonicalize-manifest $manifest $payload
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $payload)) {
        throw "Manifest canonicalization failed with exit code $LASTEXITCODE."
    }

    $rsa = [System.Security.Cryptography.RSA]::Create()
    try {
        $rsa.ImportFromPem((Get-Content -LiteralPath $privateKey -Raw))
        $bytes = [System.IO.File]::ReadAllBytes($payload)
        $signature = $rsa.SignData(
            $bytes,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    }
    finally {
        $rsa.Dispose()
    }

    $json = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    $json.signature.keyId = $KeyId
    $json.signature.algorithm = 'RSA-SHA256'
    $json.signature.value = [Convert]::ToBase64String($signature)
    $json | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $manifest -Encoding utf8NoBOM
    Write-Host "Signed manifest: $manifest"
}
finally {
    Remove-Item -LiteralPath $payload -Force -ErrorAction SilentlyContinue
}

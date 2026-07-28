[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [Parameter(Mandatory = $true)][string]$DownloadUrl,
    [Parameter(Mandatory = $true)][string]$ReleaseNotesUrl,
    [Parameter(Mandatory = $true)][string]$PrivateKeyPath,
    [string]$MinimumCompatibleVersion = "0.11.0",
    [string]$OutputPath = "launcher-update.json"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot "BaChen AI Launcher.csproj"
$executablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$privateKeyPath = (Resolve-Path -LiteralPath $PrivateKeyPath).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputPath)
$hash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash

$manifest = [ordered]@{
    version = $Version
    minimumCompatibleVersion = $MinimumCompatibleVersion
    downloadUrl = $DownloadUrl
    sha256 = $hash
    releaseNotesUrl = $ReleaseNotesUrl
    publishedAt = [DateTimeOffset]::UtcNow.ToString("O")
    signature = [ordered]@{
        keyId = "bachen-launcher-release-2026"
        algorithm = "RSA-SHA256"
        value = ""
    }
}

[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outputPath)) | Out-Null
[System.IO.File]::WriteAllText($outputPath, ($manifest | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))

dotnet build $projectPath -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Launcher build failed with exit code $LASTEXITCODE."
}

$launcherDll = Join-Path $projectRoot "bin\Release\net10.0-windows\BaChen AI Launcher.dll"
dotnet $launcherDll --sign-update-manifest $outputPath $privateKeyPath
if ($LASTEXITCODE -ne 0) {
    throw "Update manifest signing failed with exit code $LASTEXITCODE."
}

Write-Host "Signed update manifest: $outputPath"
Write-Host "Launcher SHA256:       $hash"

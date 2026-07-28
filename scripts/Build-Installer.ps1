[CmdletBinding()]
param(
    [string]$Version = "0.11.0",
    [string]$CompilerPath
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $projectRoot "installer\BaChenAILauncher.iss"
$launcherPath = Join-Path $projectRoot "artifacts\release\BaChen AI Launcher.exe"

if (-not (Test-Path -LiteralPath $launcherPath)) {
    throw "Published launcher not found. Run scripts\Publish.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    $candidates = @(
        $(if ($command) { $command.Source }),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $CompilerPath = $candidates | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($CompilerPath) -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw "Inno Setup 6 compiler was not found. Install JRSoftware.InnoSetup or pass -CompilerPath."
}

$sourceDefinition = $projectRoot.Replace("\", "\\")
& $CompilerPath "/DAppVersion=$Version" "/DSourceRoot=$sourceDefinition" $scriptPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $projectRoot "artifacts\installer\BaChen-AI-Launcher-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Expected installer was not created: $installerPath"
}

Write-Host "Installer: $installerPath"
Write-Host "SHA256:   $((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash)"

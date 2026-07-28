[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot "BaChen AI Launcher.csproj"
$outputPath = Join-Path $projectRoot "artifacts\release"

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedFiles = @(Get-ChildItem -LiteralPath $outputPath -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne "BaChen AI Launcher.exe") {
    $names = $publishedFiles.Name -join ", "
    throw "Unexpected release contents: $names"
}

$exe = $publishedFiles[0]
$hash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash
Write-Host "Published: $($exe.FullName)"
Write-Host "SHA256:   $hash"

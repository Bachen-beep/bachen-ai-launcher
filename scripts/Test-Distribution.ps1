[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$LauncherPath,

    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [Parameter(Mandatory = $true)]
    [string]$ReportPath
)

$ErrorActionPreference = "Stop"
$launcher = (Resolve-Path -LiteralPath $LauncherPath).Path
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$report = [Collections.Generic.List[string]]::new()
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("BaChen Stage2 验收 " + [Guid]::NewGuid().ToString("N"))
$installRoot = Join-Path $testRoot "程序 文件"
$configRoot = Join-Path $testRoot "用户 配置"
$dataRoot = Join-Path $testRoot "插件 数据"
$selfTestReport = Join-Path $testRoot "standalone-self-test.txt"
$installedSelfTestReport = Join-Path $testRoot "installed-self-test.txt"
$previousConfig = $env:BACHEN_AI_CONFIG_DIR
$previousData = $env:BACHEN_AI_DATA_ROOT

function Add-Pass([string]$Name) {
    $script:report.Add("PASS: $Name")
}

function Invoke-CheckedProcess([string]$FilePath, [string]$Arguments, [string]$Name) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($startInfo)
    if (-not $process) { throw "Unable to start: $Name" }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "$Name failed with exit code $($process.ExitCode)." }
    Add-Pass $Name
}

function Test-UiStartup([string]$Executable, [string]$Name) {
    $process = Start-Process -FilePath $Executable -PassThru
    Start-Sleep -Seconds 5
    $process.Refresh()
    if ($process.HasExited) { throw "$Name exited during the startup observation window." }
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
    Add-Pass $Name
}

try {
    New-Item -ItemType Directory -Path $testRoot, $configRoot, $dataRoot -Force | Out-Null
    $env:BACHEN_AI_CONFIG_DIR = $configRoot
    $env:BACHEN_AI_DATA_ROOT = $dataRoot

    $os = Get-CimInstance Win32_OperatingSystem
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $report.Add("DATE: $([DateTimeOffset]::Now.ToString('O'))")
    $report.Add("OS: $($os.Caption) $($os.Version) build $($os.BuildNumber) $env:PROCESSOR_ARCHITECTURE")
    $report.Add("USER: $($identity.Name); administrator=$isAdministrator")
    $report.Add("LAUNCHER_SHA256: $((Get-FileHash -LiteralPath $launcher -Algorithm SHA256).Hash)")
    $report.Add("INSTALLER_SHA256: $((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash)")

    Invoke-CheckedProcess $launcher "--self-test `"$selfTestReport`"" "Standalone launcher self-tests"
    if (-not (Select-String -LiteralPath $selfTestReport -SimpleMatch "SELF TEST PASSED" -Quiet)) {
        throw "Standalone self-test report does not contain the success marker."
    }
    $report.Add("SELF_TEST_COUNT: $(@(Select-String -LiteralPath $selfTestReport -Pattern '^PASS:').Count)")
    Test-UiStartup $launcher "Isolated standalone UI startup"

    $installArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR=`"$installRoot`""
    Invoke-CheckedProcess $installer $installArguments "Silent per-user installation"
    $installedLauncher = Join-Path $installRoot "BaChen AI Launcher.exe"
    if (-not (Test-Path -LiteralPath $installedLauncher)) { throw "Installed launcher was not found: $installedLauncher" }
    Add-Pass "Installed launcher exists"

    Invoke-CheckedProcess $installedLauncher "--self-test `"$installedSelfTestReport`"" "Installed launcher self-tests"
    Test-UiStartup $installedLauncher "Isolated installed UI startup"

    $preservationMarker = Join-Path $configRoot "preserve-after-uninstall.txt"
    Set-Content -LiteralPath $preservationMarker -Value "preserve" -Encoding UTF8
    $uninstaller = Join-Path $installRoot "unins000.exe"
    if (-not (Test-Path -LiteralPath $uninstaller)) { throw "Uninstaller was not found: $uninstaller" }
    Invoke-CheckedProcess $uninstaller "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" "Silent uninstall"
    if (Test-Path -LiteralPath $installedLauncher) { throw "Installed launcher remains after uninstall." }
    Add-Pass "Program files removed"
    if (-not (Test-Path -LiteralPath $preservationMarker)) { throw "User configuration was removed by uninstall." }
    Add-Pass "User data preserved by default"

    $report.Add("DISTRIBUTION TEST PASSED")
}
catch {
    $report.Add("DISTRIBUTION TEST FAILED")
    $report.Add($_.Exception.ToString())
    throw
}
finally {
    $fullReportPath = [IO.Path]::GetFullPath($ReportPath)
    New-Item -ItemType Directory -Path (Split-Path -Parent $fullReportPath) -Force | Out-Null
    [IO.File]::WriteAllLines($fullReportPath, $report, [Text.UTF8Encoding]::new($true))
    $env:BACHEN_AI_CONFIG_DIR = $previousConfig
    $env:BACHEN_AI_DATA_ROOT = $previousData
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

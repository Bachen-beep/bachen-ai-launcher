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
$resolvedLauncherPath = (Resolve-Path -LiteralPath $LauncherPath).Path
$resolvedInstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This qualification test must be started by an administrator."
}

$userName = "BaChenStage2" + ([char]0x7528).ToString() + [char]0x6237
$existingUser = Get-LocalUser -Name $userName -ErrorAction SilentlyContinue
if ($existingUser) {
    throw "The temporary qualification account already exists: $userName"
}

$randomBytes = New-Object byte[] 24
$randomNumberGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
$randomNumberGenerator.GetBytes($randomBytes)
$randomNumberGenerator.Dispose()
$passwordText = [Convert]::ToBase64String($randomBytes) + "aA1!"
$securePassword = ConvertTo-SecureString $passwordText -AsPlainText -Force
$testRoot = Join-Path $env:ProgramData ("BaChen AI Launcher\Qualification\" + [Guid]::NewGuid().ToString("N"))
$childReport = Join-Path $testRoot "standard-user-distribution.txt"
$qualifiedUser = $null
$profile = $null
$failure = $null

try {
    $qualifiedUser = New-LocalUser `
        -Name $userName `
        -Password $securePassword `
        -AccountNeverExpires `
        -PasswordNeverExpires `
        -Description "Temporary BaChen qualification account"
    Add-LocalGroupMember -SID "S-1-5-32-545" -Member $qualifiedUser

    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $identityName = "$env:COMPUTERNAME\$userName"
    $acl = Get-Acl -LiteralPath $testRoot
    $rights = [Security.AccessControl.FileSystemRights]::Modify
    $inheritance = [Security.AccessControl.InheritanceFlags]"ContainerInherit, ObjectInherit"
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $allow = [Security.AccessControl.AccessControlType]::Allow
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identityName, $rights, $inheritance, $propagation, $allow))
    Set-Acl -LiteralPath $testRoot -AclObject $acl

    $stagedLauncher = Join-Path $testRoot "BaChen AI Launcher.exe"
    $stagedInstaller = Join-Path $testRoot "BaChen AI Launcher Setup.exe"
    $stagedDistributionScript = Join-Path $testRoot "Test-Distribution.ps1"
    Copy-Item -LiteralPath $resolvedLauncherPath -Destination $stagedLauncher
    Copy-Item -LiteralPath $resolvedInstallerPath -Destination $stagedInstaller
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Test-Distribution.ps1") -Destination $stagedDistributionScript

    $credential = [Management.Automation.PSCredential]::new($identityName, $securePassword)
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$stagedDistributionScript`" -LauncherPath `"$stagedLauncher`" -InstallerPath `"$stagedInstaller`" -ReportPath `"$childReport`""
    $process = Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList $arguments `
        -Credential $credential `
        -LoadUserProfile `
        -WorkingDirectory $env:SystemRoot `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Standard-user distribution test failed with exit code $($process.ExitCode)."
    }
    if (-not (Test-Path -LiteralPath $childReport)) {
        throw "Standard-user distribution report was not created."
    }

    $fullReportPath = [IO.Path]::GetFullPath($ReportPath)
    New-Item -ItemType Directory -Path (Split-Path -Parent $fullReportPath) -Force | Out-Null
    Copy-Item -LiteralPath $childReport -Destination $fullReportPath -Force
}
catch {
    $failure = $_
    if (Test-Path -LiteralPath $childReport) {
        $fullReportPath = [IO.Path]::GetFullPath($ReportPath)
        New-Item -ItemType Directory -Path (Split-Path -Parent $fullReportPath) -Force | Out-Null
        Copy-Item -LiteralPath $childReport -Destination $fullReportPath -Force
    }
}
finally {
    [Array]::Clear($randomBytes, 0, $randomBytes.Length)
    $passwordText = $null

    if ($qualifiedUser) {
        $profile = Get-CimInstance Win32_UserProfile -Filter "SID='$($qualifiedUser.SID.Value)'" -ErrorAction SilentlyContinue
        Remove-LocalUser -SID $qualifiedUser.SID -ErrorAction SilentlyContinue
    }
    if ($profile -and -not $profile.Loaded -and $profile.LocalPath -like "$env:SystemDrive\Users\*") {
        Remove-CimInstance -InputObject $profile -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

if ($failure) {
    throw $failure
}

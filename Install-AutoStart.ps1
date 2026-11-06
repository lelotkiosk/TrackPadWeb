param(
    [string]$ServerExePath
)

$ErrorActionPreference = 'Stop'
$taskName = 'PhoneTrackpad Wi-Fi Server'

if ([string]::IsNullOrWhiteSpace($ServerExePath)) {
    $candidates = @(
        (Join-Path $PSScriptRoot 'bin\Release\net9.0-windows\win-x64\publish\PhoneTrackpad.exe'),
        (Join-Path $PSScriptRoot 'bin\Release\net9.0\win-x64\publish-final\PhoneTrackpad.exe'),
        (Join-Path $PSScriptRoot 'bin\Release\net9.0\win-x64\publish\PhoneTrackpad.exe'),
        (Join-Path $PSScriptRoot 'downloads\PhoneTrackpad.exe')
    )
    $ServerExePath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($ServerExePath) -or -not (Test-Path -LiteralPath $ServerExePath -PathType Leaf)) {
    throw 'PhoneTrackpad.exe was not found. Pass its path, for example: .\Install-AutoStart.ps1 -ServerExePath C:\Apps\PhoneTrackpad.exe'
}

$ServerExePath = (Resolve-Path -LiteralPath $ServerExePath).Path
$workingDirectory = Split-Path -Parent $ServerExePath
$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute $ServerExePath -WorkingDirectory $workingDirectory
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description 'Starts the PhoneTrackpad Wi-Fi server in the Windows notification area when this user signs in.' `
    -Force | Out-Null

$listener = Get-NetTCPConnection -State Listen -LocalPort 5000 -ErrorAction SilentlyContinue
if (-not $listener) {
    Start-ScheduledTask -TaskName $taskName
}

Write-Output "Auto-start is configured for $user."
Write-Output "Server executable: $ServerExePath"
if ($listener) {
    Write-Output 'The server is already running. The scheduled task will start it at the next sign-in.'
}
else {
    Write-Output 'The scheduled task has been started now.'
}

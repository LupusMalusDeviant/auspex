<#
.SYNOPSIS
    Sets the Auspex sensor up as a task that starts at logon.

.DESCRIPTION
    Copies the executable to a fixed place, puts the settings next to it and
    registers a task in the task scheduler.

    The task runs with the highest privileges. Not out of convenience:
    Windows only hands out per-connection byte counters through TCP-ESTATS,
    and those demand them. Without the privileges the sensor still runs -
    the column then stays empty, which is more honest than a zero.

    It also runs without a visible window (LogonType S4U). A console window
    that opens at every logon gets clicked away after two days and switched
    off after a week.

.PARAMETER Base
    The dashboard's address, e.g. http://192.168.1.61:5390

.PARAMETER Token
    The token from the dashboard under "Settings". Asked for when missing.

.PARAMETER Remove
    Takes the task and the files away again.

.EXAMPLE
    .\setup.ps1 -Base http://192.168.1.61:5390

.EXAMPLE
    .\setup.ps1 -Remove
#>
[CmdletBinding()]
param(
    [string]$Base,
    [string]$Token,
    [string]$Target = "$env:LOCALAPPDATA\Auspex",
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$TaskName = 'Auspex-Sensor'

# An elevated window is a second window: it opens, and if the script fails
# inside it, it closes again before anybody could read why. Exactly that
# happened once - the locked executable (see below) aborted the run, and all
# that was visible was that nothing had changed. So a trap catches the abort
# and holds the window.
trap {
    Write-Host ''
    Write-Host "Failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.InvocationInfo.ScriptLineNumber) {
        Write-Host "  (line $($_.InvocationInfo.ScriptLineNumber))" -ForegroundColor DarkGray
    }
    Write-Host ''
    Read-Host 'Press Enter to close'
    exit 1
}

# Two places carry the details, and both get read.
#
# The bundled sensor.json next to this script comes from the dashboard's
# download and holds the address it was fetched from - so nobody types that
# out. It deliberately holds NO token.
#
# The already installed one does. That matters on an upgrade: the token is
# shown exactly once, and whoever installed the sensor months ago no longer
# has it anywhere except in this file. Without reading it here, updating
# would mean issuing a new token and entering it in two places.
#
# Order: what was passed on the command line wins, then the installed
# settings, then the download.
$sources = @(
    (Join-Path $Target 'sensor.json'),
    (Join-Path $PSScriptRoot 'sensor.json')
)
foreach ($file in $sources) {
    if (-not (Test-Path $file)) { continue }
    try {
        $existing = Get-Content $file -Raw | ConvertFrom-Json
        # New key names first, the old ones as a fallback - a sensor.json
        # from before version 0.9 still carries basis and zeichen.
        if (-not $Base)  { $Base = $existing.base;   if (-not $Base) { $Base = $existing.basis } }
        if (-not $Token) { $Token = $existing.token; if (-not $Token) { $Token = $existing.zeichen } }
    } catch { }
}

# ─── Privileges ──────────────────────────────────────────────────────────
$me = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $me.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'This script needs administrator rights. It restarts itself.'

    # The parameters have to travel along - otherwise the user faces the same
    # questions again in the elevated window.
    $onward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($Base)   { $onward += @('-Base', "`"$Base`"") }
    if ($Target) { $onward += @('-Target', "`"$Target`"") }
    if ($Remove) { $onward += '-Remove' }

    # The token NOT as a parameter: a command line stands in every process
    # list, and every tool that enumerates processes would get at it. Through
    # the environment it travels along without showing up there.
    if ($Token) { $env:AUSPEX_TOKEN = $Token }

    Start-Process -FilePath 'powershell.exe' -ArgumentList $onward -Verb RunAs
    return
}

# ─── Tearing down ────────────────────────────────────────────────────────
if ($Remove) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Task '$TaskName' removed."
    } else {
        Write-Host "There is no task '$TaskName'."
    }

    Get-Process -Name 'auspex-sensor' -ErrorAction SilentlyContinue | Stop-Process -Force
    if (Test-Path $Target) {
        Remove-Item -Recurse -Force $Target
        Write-Host "$Target deleted."
    }
    Write-Host 'Done. What the dashboard already knows stays there.'
    return
}

# ─── Collecting the details ──────────────────────────────────────────────
$source = Join-Path $PSScriptRoot 'auspex-sensor.exe'
if (-not (Test-Path $source)) {
    throw "auspex-sensor.exe is not next to this script ($PSScriptRoot)."
}

# From the environment, when this window is the elevated one.
if (-not $Token -and $env:AUSPEX_TOKEN) {
    $Token = $env:AUSPEX_TOKEN.Trim()
}

while (-not $Base) {
    $Base = (Read-Host 'Dashboard address (e.g. http://192.168.1.61:5390)').Trim()
}
$Base = $Base.TrimEnd('/')

while (-not $Token) {
    Write-Host ''
    Write-Host 'The token is in the dashboard under Settings -> Browser extension.'
    Write-Host 'It is the same one as for the extension and is shown only once.'
    $Token = (Read-Host 'Token').Trim()
}

# ─── Making room ─────────────────────────────────────────────────────────
# Stop first, then overwrite - not the other way round.
#
# Windows locks the executable of a running process. Whoever copies over it
# while the old sensor is still running gets "used by another process", and
# because everything aborts here, the task would stay untouched: old file,
# old process, no hint. An update would appear to have worked and changed
# nothing.
$targetExe = Join-Path $Target 'auspex-sensor.exe'

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Get-Process -Name 'auspex-sensor' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "The old sensor is still running (PID $($_.Id)) - stopping it."
    $_ | Stop-Process -Force -ErrorAction SilentlyContinue
}

# Stopped is not the same as released straight away. Follow up briefly rather
# than failing on the lock at the end after all.
if (Test-Path $targetExe) {
    $free = $false
    foreach ($attempt in 1..20) {
        try {
            $handle = [IO.File]::Open($targetExe, 'Open', 'ReadWrite', 'None')
            $handle.Dispose()
            $free = $true
            break
        } catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $free) {
        throw "$targetExe is still locked after 5 seconds. Is the sensor running under another account?"
    }
}

# ─── Putting it down ─────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $Target | Out-Null
Copy-Item $source $targetExe -Force

$settings = [ordered]@{
    base  = $Base
    token = $Token
    bytes = $true
}
$json = Join-Path $Target 'sensor.json'
$settings | ConvertTo-Json | Set-Content -Path $json -Encoding utf8

# The file carries a token that gives access to the dashboard. It belongs to
# whoever needs it and nobody else - inheritance from the parent folder off,
# then exactly two principals.
$acl = Get-Acl $json
$acl.SetAccessRuleProtection($true, $false)
$acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
foreach ($who in @($env:USERNAME, 'SYSTEM')) {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
        $who, 'FullControl', 'Allow')))
}
Set-Acl -Path $json -AclObject $acl

Write-Host "The executable and the settings are in $Target"

# ─── Registering the task ────────────────────────────────────────────────
# An existing task is already gone above - it had to go before the executable
# could be overwritten.
$action = New-ScheduledTaskAction -Execute $targetExe -WorkingDirectory $Target

$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"

# Who runs the task - and that hangs on whether the machine is in a domain.
#
# Three things are wanted at once: the highest privileges (for the byte
# counters), no visible window (one that opens at every logon gets switched
# off within a week) and no stored password.
#
# S4U can do all three - but only with Kerberos, so only in a domain. On a
# standalone machine with a local account the task can be registered, but it
# then fails at start with 0x80070002. Exactly that happened here on the
# first attempt: registered, "Ready", and no process.
#
# Outside a domain SYSTEM therefore takes over. That costs the binding to the
# signed-in user - no loss for this sensor: the connection table applies to
# the whole machine, not per session.
$inDomain = (Get-CimInstance Win32_ComputerSystem).PartOfDomain

if ($inDomain) {
    $principal = New-ScheduledTaskPrincipal `
        -UserId "$env:USERDOMAIN\$env:USERNAME" `
        -LogonType S4U `
        -RunLevel Highest
    $asWhom = "as $env:USERNAME (S4U)"
} else {
    $principal = New-ScheduledTaskPrincipal `
        -UserId 'SYSTEM' `
        -LogonType ServiceAccount `
        -RunLevel Highest
    $asWhom = 'as SYSTEM'
}

$how = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew

Register-ScheduledTask `
    -TaskName $TaskName `
    -Description 'Reports which program talks to which destination. TCP only, no content.' `
    -Action $action -Trigger $trigger -Principal $principal -Settings $how | Out-Null

# ${asWhom} in braces: PowerShell reads "$asWhom:" as a scoped variable
# reference and the whole file stops parsing. The script would not have run
# at all - which is the one failure mode a setup script must not have.
Write-Host "Task '$TaskName' registered ${asWhom}: at logon, highest privileges, no window."

# ─── And trying it straight away ─────────────────────────────────────────
# Registering is not the same as running. Whoever relies on it only notices
# at the next logon that something is missing.
Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds 6

$running = Get-Process -Name 'auspex-sensor' -ErrorAction SilentlyContinue
$info = Get-ScheduledTask -TaskName $TaskName | Get-ScheduledTaskInfo

if ($running) {
    Write-Host ''
    Write-Host "Running (PID $($running.Id -join ', '))."
    Write-Host 'The first report goes out after about half a minute.'
    Write-Host 'After that it is in the dashboard under Watch -> Where to?'
} else {
    Write-Warning 'The task is registered, but no process is running.'
    Write-Warning ("Last result: 0x{0:X8}" -f $info.LastTaskResult)

    # The two cases that really occur. A bare error number helps nobody
    # guess.
    switch ($info.LastTaskResult) {
        0x80070002 {
            Write-Warning 'File not found - which is also how Windows reports it'
            Write-Warning 'when S4U cannot sign in outside a domain.'
        }
        0x80070005 { Write-Warning 'Access denied.' }
    }

    Write-Host ''
    Write-Host 'To look for yourself:'
    Write-Host "    & '$Target\auspex-sensor.exe' --show"
}

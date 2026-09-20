# Taskbar Timer installer. Launched by Install.cmd - do not run this directly unless
# you are happy to set the execution policy yourself.
#
# WHAT THIS CHANGES ON THE MACHINE:
#   1. Copies TaskbarTimer.exe into C:\Tools\TaskbarTimer
#      (falls back to %LOCALAPPDATA%\TaskbarTimer if C:\Tools cannot be created)
#   2. Creates a Start Menu shortcut under your own user profile
#   3. Optionally adds one HKCU ...\Run entry so it starts with Windows - you are asked
#   4. Starts the timer
# It does not touch HKLM, system settings, or anything belonging to other users.

param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$AppName   = 'Taskbar Timer'
$ExeName   = 'TaskbarTimer.exe'
$RunKey    = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunValue  = 'IllumEdTaskbarTimer'
$Shortcut  = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"

function Write-Step($msg) { Write-Host "  $msg" -ForegroundColor Gray }
function Stop-Timer {
    Get-Process 'TaskbarTimer' -ErrorAction SilentlyContinue | ForEach-Object {
        $_.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 400
        if (-not $_.HasExited) { $_.Kill() }
    }
    Start-Sleep -Milliseconds 500
}

function Find-InstallDir {
    foreach ($candidate in @('C:\Tools\TaskbarTimer',
                             (Join-Path $env:LOCALAPPDATA 'TaskbarTimer'))) {
        try {
            New-Item -ItemType Directory -Path $candidate -Force -ErrorAction Stop | Out-Null
            return $candidate
        } catch { }
    }
    throw 'Could not create an installation folder in either C:\Tools or your AppData folder.'
}

# ---------------------------------------------------------------- uninstall
if ($Uninstall) {
    Write-Host ''
    Write-Host "Removing $AppName" -ForegroundColor Cyan
    Stop-Timer
    Write-Step 'stopped the timer'

    try {
        if ((Get-Item $RunKey).GetValue($RunValue)) {
            Remove-ItemProperty -Path $RunKey -Name $RunValue -ErrorAction SilentlyContinue
            Write-Step 'removed the start-with-Windows entry'
        }
    } catch { }

    if (Test-Path $Shortcut) { Remove-Item $Shortcut -Force; Write-Step 'removed the Start Menu shortcut' }

    foreach ($dir in @('C:\Tools\TaskbarTimer', (Join-Path $env:LOCALAPPDATA 'TaskbarTimer'))) {
        if (Test-Path $dir) {
            Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
            if (-not (Test-Path $dir)) { Write-Step "removed $dir" }
        }
    }

    Write-Host ''
    Write-Host 'Done. Nothing of it is left on this machine.' -ForegroundColor Green
    Write-Host ''
    Read-Host 'Press Enter to close'
    return
}

# ---------------------------------------------------------------- install
Write-Host ''
Write-Host "Installing $AppName" -ForegroundColor Cyan
Write-Host ''

# .NET Framework 4.x is present on every Windows 10 and 11 machine, but check anyway so
# the failure is a clear message rather than a silent non-start.
$release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue).Release
if (-not $release) {
    Write-Host 'This needs Microsoft .NET Framework 4.x, which does not appear to be installed.' -ForegroundColor Red
    Write-Host 'Get it from: https://dotnet.microsoft.com/download/dotnet-framework' -ForegroundColor Yellow
    Write-Host ''
    Read-Host 'Press Enter to close'
    return
}
Write-Step ".NET Framework found (release $release)"

$source = Join-Path $PSScriptRoot $ExeName
if (-not (Test-Path $source)) { throw "$ExeName is missing - keep the whole folder together." }

# Files copied off a flash drive or out of a zip can carry a mark-of-the-web flag that
# makes Windows treat them as downloaded every time they run. Clear it.
try { Unblock-File -Path $source -ErrorAction SilentlyContinue } catch { }

Stop-Timer
$installDir = Find-InstallDir
Copy-Item $source $installDir -Force
$exe = Join-Path $installDir $ExeName
try { Unblock-File -Path $exe -ErrorAction SilentlyContinue } catch { }
Write-Step "installed to $installDir"

$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut($Shortcut)
$lnk.TargetPath       = $exe
$lnk.WorkingDirectory = $installDir
$lnk.Description      = 'Countdown timer that sits on the taskbar'
$lnk.Save()
Write-Step 'added a Start Menu shortcut'

Write-Host ''
$answer = Read-Host 'Start the timer automatically when Windows starts? (y/n)'
if ($answer -match '^(y|yes)$') {
    Set-ItemProperty -Path $RunKey -Name $RunValue -Value ('"' + $exe + '"')
    Write-Step 'it will now start with Windows'
} else {
    Remove-ItemProperty -Path $RunKey -Name $RunValue -ErrorAction SilentlyContinue
    Write-Step 'left out of startup - you can turn it on later by right-clicking the timer'
}

Start-Process -FilePath $exe

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Write-Host ''
Write-Host 'The timer is now on your taskbar, just left of the clock and system icons,' -ForegroundColor White
Write-Host 'showing 30:00 in amber. Left-click it to start counting. Right-click it for' -ForegroundColor White
Write-Host 'durations, volume and settings.' -ForegroundColor White
Write-Host ''
Write-Host 'To remove it later, run Uninstall.cmd from this same folder.' -ForegroundColor Gray
Write-Host ''
Read-Host 'Press Enter to close'

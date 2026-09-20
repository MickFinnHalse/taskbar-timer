@echo off
REM Taskbar Timer - uninstaller
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall

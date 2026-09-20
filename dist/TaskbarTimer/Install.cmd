@echo off
REM Taskbar Timer - installer
REM Runs install.ps1 with the execution policy bypassed for this one process only.
REM Nothing about your PowerShell settings is changed permanently.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"

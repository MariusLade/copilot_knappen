@echo off
rem Runs install.ps1 from cmd.exe or Explorer without changing the PowerShell execution policy.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
pause

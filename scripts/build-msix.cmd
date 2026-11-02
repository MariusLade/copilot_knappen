@echo off
rem Runs build-msix.ps1 from cmd.exe or Explorer without changing the PowerShell execution policy.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-msix.ps1" %*
if errorlevel 1 pause

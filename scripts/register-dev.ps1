<#
.SYNOPSIS
    Development shortcut: registers the unpacked package layout without building/signing an .msix.

.DESCRIPTION
    Requires Developer Mode (Settings > System > For developers). The app gets package identity, so the
    copilotknappen: protocol works and you can test activation with:
        start copilotknappen://?state=Tap
    Note: Windows only lists *signed* packages as Copilot key providers, so use build-msix.ps1 + install.ps1
    to try the real key.
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build-msix.ps1') -Arch $Arch -NoSign
Get-Process CopilotKnappen -ErrorAction SilentlyContinue | Stop-Process -Force
Add-AppxPackage -Register (Join-Path $root "artifacts\layout-$Arch\AppxManifest.xml") -ForceUpdateFromAnyVersion
Write-Host 'Registered. Launch "Copilot Knappen" from the Start menu or run: start copilotknappen://?state=Tap' -ForegroundColor Green

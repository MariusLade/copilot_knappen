<#
.SYNOPSIS
    Installs the Copilot Knappen MSIX: trusts its signing certificate (one UAC prompt) and adds the package.

.EXAMPLE
    .\scripts\install.ps1
    # uses the newest artifacts\*.msix and artifacts\CopilotKnappen.cer (or files next to this script)
.EXAMPLE
    .\install.ps1 -Package .\CopilotKnappen_1.0.12.0_x64.msix -Certificate .\CopilotKnappen.cer
#>
[CmdletBinding()]
param(
    [string]$Package,
    [string]$Certificate
)

$ErrorActionPreference = 'Stop'
$searchDirs = @($PSScriptRoot, (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'), (Get-Location).Path)

if (-not $Package) {
    $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    $Package = $searchDirs | ForEach-Object { Get-ChildItem $_ -Filter "CopilotKnappen_*_$arch.msix" -ErrorAction SilentlyContinue } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $Package) { throw "No CopilotKnappen_*_$arch.msix found. Build it with scripts\build-msix.ps1 or pass -Package." }
}
if (-not $Certificate) {
    $Certificate = $searchDirs | ForEach-Object { Join-Path $_ 'CopilotKnappen.cer' } | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ($Certificate) {
    $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2($Certificate)
    $trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint
    if (-not $trusted) {
        Write-Host "Trusting signing certificate $($cert.Subject) (administrator approval needed)..." -ForegroundColor Cyan
        $command = "Import-Certificate -FilePath '$Certificate' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
        $proc = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList '-NoProfile', '-Command', $command
        if ($proc.ExitCode -ne 0) { throw 'Could not import the certificate.' }
    }
}
else {
    Write-Warning 'No certificate given; assuming the package signer is already trusted.'
}

# Stop a running instance so the package can be updated.
Get-Process CopilotKnappen -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Installing $Package ..." -ForegroundColor Cyan
Add-AppxPackage -Path $Package -ForceUpdateFromAnyVersion

Write-Host ''
Write-Host 'Installed. Next steps:' -ForegroundColor Green
Write-Host '  1. Open Settings > Personalization > Text input > "Customize Copilot key on keyboard"'
Write-Host '     and choose Copilot Knappen.'
Write-Host '  2. Press the Copilot key (or Ctrl+Alt+Space) and start typing.'
Start-Process 'ms-settings:personalization-textinput'

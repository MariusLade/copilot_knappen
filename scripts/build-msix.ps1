<#
.SYNOPSIS
    Builds Copilot Knappen and packages it as a signed MSIX (required for the Copilot key provider).

.DESCRIPTION
    1. dotnet publish (self-contained by default, so no .NET runtime is needed on the target PC)
    2. Lays out AppxManifest.xml + logos next to the published files
    3. makeappx pack
    4. signtool sign - with your own .pfx, or with a self-signed development certificate that is created
       in Cert:\CurrentUser\My on first use. The public part is exported as artifacts\CopilotKnappen.cer.

    Requires the Windows 10/11 SDK (makeappx.exe and signtool.exe), e.g. from the Visual Studio installer
    or https://developer.microsoft.com/windows/downloads/windows-sdk/

.EXAMPLE
    .\scripts\build-msix.ps1
.EXAMPLE
    .\scripts\build-msix.ps1 -Arch arm64 -Version 1.2.0.0
.EXAMPLE
    .\scripts\build-msix.ps1 -CertificatePath C:\certs\mine.pfx -CertificatePassword secret -Publisher "CN=Contoso"
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64',

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0.0',

    # Must match the subject of the signing certificate.
    [string]$Publisher = 'CN=CopilotKnappen Dev',

    # Existing code-signing .pfx. When omitted a self-signed development certificate is used.
    [string]$CertificatePath,
    [string]$CertificatePassword = 'copilotknappen-dev',

    [switch]$NoSign,

    # Smaller package, but the .NET 8 Desktop Runtime must be installed on the target PC.
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$layout = Join-Path $artifacts "layout-$Arch"
$msix = Join-Path $artifacts "CopilotKnappen_$($Version)_$Arch.msix"

if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Path $layout -Force | Out-Null

# ---------------------------------------------------------------- 1. publish
Write-Host "==> Publishing ($Arch)" -ForegroundColor Cyan
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$fileVersion = $Version
dotnet publish (Join-Path $root 'src\CopilotKnappen\CopilotKnappen.csproj') `
    -c Release -r "win-$Arch" --self-contained $selfContained `
    -p:Version=$($Version.Substring(0, $Version.LastIndexOf('.'))) -p:FileVersion=$fileVersion `
    -o $layout
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# ---------------------------------------------------------------- 2. layout
Write-Host '==> Writing package layout' -ForegroundColor Cyan
$manifest = Get-Content (Join-Path $root 'packaging\AppxManifest.xml') -Raw
$manifest = $manifest.Replace('$VERSION$', $Version).Replace('$ARCH$', $Arch).Replace('$PUBLISHER$', [Security.SecurityElement]::Escape($Publisher))
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))
Copy-Item (Join-Path $root 'packaging\Assets') (Join-Path $layout 'Assets') -Recurse -Force
Copy-Item (Join-Path $root 'packaging\Public') (Join-Path $layout 'Public') -Recurse -Force

# ---------------------------------------------------------------- 3. pack
function Find-SdkTool([string]$name) {
    $onPath = Get-Command $name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem $kits -Filter $name -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $tool) { throw "$name not found. Install the Windows 10/11 SDK." }
    return $tool.FullName
}

Write-Host '==> Packing MSIX' -ForegroundColor Cyan
$makeappx = Find-SdkTool 'makeappx.exe'
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }

# ---------------------------------------------------------------- 4. sign
if ($NoSign) {
    Write-Host "==> Done (unsigned): $msix" -ForegroundColor Green
    return
}

$cerPath = Join-Path $artifacts 'CopilotKnappen.cer'
if (-not $CertificatePath) {
    Write-Host '==> Using self-signed development certificate' -ForegroundColor Cyan
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Publisher -and $_.NotAfter -gt (Get-Date).AddDays(7) -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature `
            -FriendlyName 'Copilot Knappen development signing' -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(3)
    }
    $CertificatePath = Join-Path $artifacts 'CopilotKnappen-dev.pfx'
    $securePassword = ConvertTo-SecureString $CertificatePassword -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $CertificatePath -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
}
else {
    $pfx = New-Object Security.Cryptography.X509Certificates.X509Certificate2($CertificatePath, $CertificatePassword)
    if ($pfx.Subject -ne $Publisher) {
        throw "Certificate subject '$($pfx.Subject)' must equal -Publisher '$Publisher'."
    }
    [IO.File]::WriteAllBytes($cerPath, $pfx.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
}

Write-Host '==> Signing' -ForegroundColor Cyan
$signtool = Find-SdkTool 'signtool.exe'
& $signtool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $msix
if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }

Write-Host ''
Write-Host "==> Package:     $msix" -ForegroundColor Green
Write-Host "==> Certificate: $cerPath" -ForegroundColor Green
Write-Host '    Install with: .\scripts\install.ps1' -ForegroundColor Green

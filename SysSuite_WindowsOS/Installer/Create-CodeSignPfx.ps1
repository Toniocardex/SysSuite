# Genera SysSuite_CodeSign.pfx nella cartella indicata (solo se mancante).
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
$pfxPath = Join-Path $OutputRoot 'SysSuite_CodeSign.pfx'

if (Test-Path -LiteralPath $pfxPath) {
    Write-Host '[OK] Certificato gia presente'
    exit 0
}

$cert = New-SelfSignedCertificate `
    -Subject 'CN=SysSuite One, O=Antonio Cardelli, L=Milano, C=IT' `
    -Type CodeSigning `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears(5) `
    -FriendlyName 'SysSuite One Code Signing'

$pfxPassword = ConvertTo-SecureString -String 'SysSuite2024!' -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pfxPassword | Out-Null
Write-Host '[OK] Creato SysSuite_CodeSign.pfx'
exit 0

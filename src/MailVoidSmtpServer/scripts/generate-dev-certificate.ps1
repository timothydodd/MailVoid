# PowerShell script to generate a self-signed certificate for development
# This creates a certificate suitable for testing SMTP over TLS

param(
    [string]$CertPath = "../certs",
    [string]$Password = "development",
    [string]$DnsName = "localhost",
    [string]$FriendlyName = "MailVoid SMTP Development Certificate"
)

# Create certs directory if it doesn't exist
$CertDir = Join-Path $PSScriptRoot $CertPath
if (!(Test-Path $CertDir)) {
    New-Item -ItemType Directory -Path $CertDir | Out-Null
    Write-Host "Created certificate directory: $CertDir" -ForegroundColor Green
}

# Generate certificate
$cert = New-SelfSignedCertificate `
    -DnsName $DnsName, "*.mailvoid.local", "smtp.mailvoid.local" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears(5) `
    -FriendlyName $FriendlyName `
    -KeySpec KeyExchange `
    -KeyUsage DigitalSignature, KeyEncipherment `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1")

Write-Host "Certificate created with thumbprint: $($cert.Thumbprint)" -ForegroundColor Green

# Export to PFX
$pfxPath = Join-Path $CertDir "smtp-dev.pfx"
$securePassword = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null

Write-Host "Certificate exported to: $pfxPath" -ForegroundColor Green
Write-Host "Password: $Password" -ForegroundColor Yellow

# Export to PEM format (for compatibility)
$pemCertPath = Join-Path $CertDir "smtp-dev.crt"
$pemKeyPath = Join-Path $CertDir "smtp-dev.key"

# Export certificate to PEM
$certPem = [System.Convert]::ToBase64String($cert.RawData, [System.Base64FormattingOptions]::InsertLineBreaks)
$pemContent = "-----BEGIN CERTIFICATE-----`n$certPem`n-----END CERTIFICATE-----"
Set-Content -Path $pemCertPath -Value $pemContent -Encoding ASCII

Write-Host "Certificate (PEM) exported to: $pemCertPath" -ForegroundColor Green

# Clean up - remove from store
Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)"

Write-Host "`nDevelopment certificate generated successfully!" -ForegroundColor Green
Write-Host "To use this certificate, update your appsettings.Development.json:" -ForegroundColor Cyan
Write-Host @"
{
  "SmtpServer": {
    "EnableSsl": true,
    "CertificatePath": "./certs/smtp-dev.pfx",
    "CertificatePassword": "$Password"
  }
}
"@
# TLS/SSL Setup Guide for MailVoid SMTP Server

This guide explains how to configure TLS/SSL support for secure SMTP connections in MailVoid.

## Overview

The MailVoid SMTP Server now supports TLS/SSL encryption for secure email transmission. When enabled, the server listens on:
- **Port 25**: Plain text SMTP (always enabled)
- **Port 2580**: Test port with plain text (always enabled)
- **Port 465**: Implicit TLS/SSL (SMTPS)
- **Port 587**: STARTTLS (starts plain, upgrades to encrypted)

## Configuration

### 1. Enable TLS in appsettings.json

Update your `appsettings.json`:

```json
{
  "SmtpServer": {
    "Port": 25,
    "TestPort": 2580,
    "SslPort": 465,
    "StartTlsPort": 587,
    "Name": "MailVoid SMTP Server",
    "MaxMessageSize": 10485760,
    "EnableSsl": true,
    "CertificatePath": "/path/to/your/certificate.pfx",
    "CertificatePassword": "your-certificate-password",
    "TlsProtocols": "Tls12,Tls13"
  }
}
```

### 2. Certificate Setup

The server supports multiple certificate formats:

#### Option A: PFX/PKCS12 Certificate (Recommended)
```bash
# Generate a self-signed certificate for development
openssl req -x509 -newkey rsa:4096 -keyout key.pem -out cert.pem -days 365 -nodes \
  -subj "/C=US/ST=State/L=City/O=Organization/CN=mail.yourdomain.com"

# Convert to PFX format
openssl pkcs12 -export -out certificate.pfx -inkey key.pem -in cert.pem \
  -password pass:your-password

# Update configuration
"CertificatePath": "/path/to/certificate.pfx",
"CertificatePassword": "your-password"
```

#### Option B: PEM Certificate
```bash
# Generate PEM certificate and key
openssl req -x509 -newkey rsa:4096 -keyout cert.key -out cert.crt -days 365 -nodes \
  -subj "/C=US/ST=State/L=City/O=Organization/CN=mail.yourdomain.com"

# Update configuration (key file should be named cert.key if cert is cert.crt)
"CertificatePath": "/path/to/cert.crt",
"CertificatePassword": ""  # Not needed for PEM
```

#### Option C: Let's Encrypt Certificate
```bash
# Use certbot to get a certificate
sudo certbot certonly --standalone -d mail.yourdomain.com

# Convert to PFX (Let's Encrypt provides PEM format)
openssl pkcs12 -export -out certificate.pfx \
  -inkey /etc/letsencrypt/live/mail.yourdomain.com/privkey.pem \
  -in /etc/letsencrypt/live/mail.yourdomain.com/fullchain.pem \
  -password pass:your-password

# Update configuration
"CertificatePath": "/path/to/certificate.pfx",
"CertificatePassword": "your-password"
```

### 3. Using User Secrets (Development)

For development, use user secrets instead of storing certificates in appsettings.json:

```bash
# Navigate to the project directory
cd /path/to/MailVoidSmtpServer

# Set certificate path
dotnet user-secrets set "SmtpServer:CertificatePath" "/path/to/certificate.pfx"
dotnet user-secrets set "SmtpServer:CertificatePassword" "your-password"
dotnet user-secrets set "SmtpServer:EnableSsl" "true"
```

## TLS Protocol Configuration

The `TlsProtocols` setting accepts comma-separated values:
- `Tls` - TLS 1.0 (not recommended)
- `Tls11` - TLS 1.1 (not recommended)
- `Tls12` - TLS 1.2 (recommended minimum)
- `Tls13` - TLS 1.3 (recommended)

Example configurations:
```json
"TlsProtocols": "Tls12,Tls13"  // Recommended (default)
"TlsProtocols": "Tls13"         // Most secure, may have compatibility issues
"TlsProtocols": "Tls11,Tls12,Tls13"  // More compatible, less secure
```

## Testing TLS Connections

### 1. Test with OpenSSL

```bash
# Test implicit TLS (port 465)
openssl s_client -connect localhost:465 -crlf

# Test STARTTLS (port 587)
openssl s_client -connect localhost:587 -starttls smtp -crlf

# Check certificate details
openssl s_client -connect localhost:465 -showcerts
```

### 2. Test with telnet (STARTTLS)

```bash
telnet localhost 587
EHLO test.com
STARTTLS
# Connection should upgrade to TLS
```

### 3. Test with swaks

```bash
# Install swaks
sudo apt-get install swaks  # Ubuntu/Debian
brew install swaks          # macOS

# Test TLS connection
swaks --to test@mailvoid.com --from sender@example.com \
      --server localhost:465 --tls \
      --body "Test email over TLS"

# Test STARTTLS
swaks --to test@mailvoid.com --from sender@example.com \
      --server localhost:587 --tls-on-connect \
      --body "Test email over STARTTLS"
```

## Security Considerations

1. **Certificate Security**:
   - Store certificates outside the application directory
   - Set appropriate file permissions (600 or 400)
   - Use strong passwords for PFX files
   - Rotate certificates before expiry

2. **Protocol Security**:
   - Disable TLS 1.0 and 1.1 in production
   - Consider TLS 1.3 only for maximum security
   - Monitor for protocol vulnerabilities

3. **Authentication**:
   - When TLS is enabled, authentication is automatically required on secure ports
   - Credentials are never sent over plain text on TLS connections

## Troubleshooting

### Certificate Not Loading
- Check file path is absolute
- Verify file permissions
- Check certificate format matches configuration
- Review logs for specific error messages

### Connection Refused on SSL Ports
- Verify `EnableSsl` is set to `true`
- Check certificate loaded successfully in logs
- Ensure ports are not blocked by firewall
- Verify no other service is using the ports

### Certificate Validation Errors
- For self-signed certificates, clients may need to accept the certificate
- Ensure certificate CN matches the server hostname
- Check certificate hasn't expired
- Verify complete certificate chain for CA-signed certificates

## Log Messages

When TLS is configured correctly, you should see:
```
SSL/TLS enabled with certificate: CN=mail.yourdomain.com, TLS Protocols: Tls12,Tls13
SMTP server started successfully on ports 25, 2580, 465 (SSL), 587 (STARTTLS)...
```

For TLS connections, logs will show:
```
SMTP session created - SessionId: xxx, RemoteEndPoint: xxx, Port: 465, Security: SSL/TLS
```
# Cloudflare DNS Challenge Setup

This guide shows how to configure automatic Let's Encrypt certificate generation and renewal using Cloudflare DNS challenges, similar to Nginx Proxy Manager.

## Prerequisites

1. **Domain managed by Cloudflare**: Your SMTP domain must be managed by Cloudflare
2. **Cloudflare API Token**: You need a Cloudflare API token with DNS edit permissions
3. **Certbot with Cloudflare plugin**: Install `certbot-dns-cloudflare` plugin

## Installation

### 1. Install Certbot and Cloudflare Plugin

```bash
# Ubuntu/Debian
sudo apt update
sudo apt install certbot python3-certbot-dns-cloudflare

# CentOS/RHEL
sudo yum install certbot python3-certbot-dns-cloudflare

# Or using pip
pip install certbot-dns-cloudflare
```

### 2. Create Cloudflare API Token

1. Go to [Cloudflare Dashboard](https://dash.cloudflare.com/profile/api-tokens)
2. Click "Create Token"
3. Use "Custom token" template
4. Configure:
   - **Token name**: `MailVoid SMTP Server`
   - **Permissions**: 
     - `Zone:DNS:Edit`
     - `Zone:Zone:Read`
   - **Zone Resources**: 
     - `Include: Specific zone: yourdomain.com`
   - **Client IP Address Filtering**: (optional)
5. Click "Continue to summary" and "Create Token"
6. Copy the token (you won't see it again!)

### 3. Configure MailVoid SMTP Server

#### Option A: Use Configuration File

Create or update `appsettings.LetsEncrypt.json`:

```json
{
  "SmtpServer": {
    "EnableSsl": true,
    "LetsEncryptDomain": "smtp.yourdomain.com"
  },
  "LetsEncrypt": {
    "Enabled": true,
    "Email": "admin@yourdomain.com",
    "CertbotPath": "certbot",
    "CertificateDirectory": "/etc/letsencrypt",
    "CertificatePassword": "mailvoid-ssl-auto",
    "ChallengeMethod": "dns-cloudflare",
    "RenewalDaysBeforeExpiry": 30,
    "RenewalCheckIntervalHours": 24,
    "Domains": [
      "smtp.yourdomain.com"
    ],
    "CloudflareApiToken": "your-cloudflare-api-token-here"
  },
  "Cloudflare": {
    "Enabled": true,
    "ApiToken": "your-cloudflare-api-token-here",
    "Email": "admin@yourdomain.com",
    "DnsRecordTtl": 120,
    "DnsPropagationWaitSeconds": 60,
    "MaxRetryAttempts": 3,
    "RetryDelaySeconds": 10
  }
}
```

#### Option B: Use Environment Variables

```bash
export LetsEncrypt__Enabled=true
export LetsEncrypt__Email=admin@yourdomain.com
export LetsEncrypt__ChallengeMethod=dns-cloudflare
export LetsEncrypt__CloudflareApiToken=your-cloudflare-api-token-here
export LetsEncrypt__Domains__0=smtp.yourdomain.com
export SmtpServer__EnableSsl=true
export SmtpServer__LetsEncryptDomain=smtp.yourdomain.com
export Cloudflare__Enabled=true
export Cloudflare__ApiToken=your-cloudflare-api-token-here
```

## How It Works

### Automatic Certificate Management

1. **Startup**: Service checks for existing certificates
2. **Initial Setup**: If no valid certificate exists, automatically obtains one using DNS challenge
3. **DNS Challenge**: 
   - Creates `_acme-challenge.smtp.yourdomain.com` TXT record in Cloudflare
   - Let's Encrypt validates domain ownership via DNS
   - Certificate is issued and converted to PFX format
   - DNS record is cleaned up
4. **Automatic Renewal**: 
   - Checks every 24 hours (configurable)
   - Renews certificates 30 days before expiry (configurable)
   - Automatically restarts SMTP server with new certificates

### Benefits of DNS Challenge

- **No port 80 required**: Works behind firewalls and NAT
- **Wildcard certificates**: Can obtain `*.yourdomain.com` certificates
- **Multiple domains**: Can secure multiple subdomains
- **Private servers**: Works on private networks without public HTTP access

## Configuration Options

### LetsEncrypt Section

| Setting | Description | Default |
|---------|-------------|---------|
| `Enabled` | Enable/disable Let's Encrypt | `false` |
| `Email` | Contact email for Let's Encrypt | Required |
| `ChallengeMethod` | Challenge type (`dns-cloudflare`) | `http` |
| `CloudflareApiToken` | Cloudflare API token | Required for DNS |
| `Domains` | List of domains to secure | `[]` |
| `RenewalDaysBeforeExpiry` | Days before expiry to renew | `30` |
| `RenewalCheckIntervalHours` | How often to check for renewals | `24` |

### Cloudflare Section

| Setting | Description | Default |
|---------|-------------|---------|
| `Enabled` | Enable Cloudflare API integration | `false` |
| `ApiToken` | Cloudflare API token | Required |
| `DnsRecordTtl` | TTL for DNS challenge records | `120` |
| `DnsPropagationWaitSeconds` | Wait time for DNS propagation | `60` |

## Testing

### Test Certificate Generation

```bash
# Run with LetsEncrypt configuration
dotnet run --environment LetsEncrypt

# Check logs for certificate generation
tail -f logs/mailvoid-smtp.log
```

### Verify SMTP TLS

```bash
# Test implicit TLS (port 465)
openssl s_client -connect smtp.yourdomain.com:465

# Test STARTTLS (port 587)
openssl s_client -connect smtp.yourdomain.com:587 -starttls smtp
```

## Troubleshooting

### Common Issues

1. **"DNS challenge failed"**
   - Verify Cloudflare API token permissions
   - Check domain is managed by Cloudflare
   - Ensure DNS zone exists

2. **"Certificate not found"**
   - Check `CertificateDirectory` permissions
   - Verify certbot installation
   - Review service logs

3. **"SMTP server not restarting"**
   - Check service permissions
   - Verify certificate file paths
   - Review application logs

### Debug Mode

Enable debug logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "MailVoidSmtpServer.Services.LetsEncryptService": "Debug",
      "MailVoidSmtpServer.Services.CloudflareApiService": "Debug"
    }
  }
}
```

## Security Considerations

1. **Secure API Token**: Store Cloudflare API token securely
2. **File Permissions**: Ensure certificate files have proper permissions
3. **Network Security**: Consider firewall rules for SMTP ports
4. **Token Rotation**: Regularly rotate Cloudflare API tokens

## Migration from Manual Certificates

If you're currently using manual certificates:

1. Backup existing certificates
2. Update configuration to use Let's Encrypt
3. Restart service to trigger automatic certificate generation
4. Verify new certificates are working
5. Remove old certificate files

The service will automatically handle the transition and future renewals.
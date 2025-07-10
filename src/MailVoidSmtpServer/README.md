# MailVoid SMTP Server

A containerized SMTP server that receives emails and forwards them to the MailVoid API webhook endpoint.

## Features

- Receives emails via SMTP protocol on port 25
- **TLS/SSL Support**: Secure SMTP connections on ports 465 (implicit TLS) and 587 (STARTTLS)
- Parses email content using MimeKit
- Forwards emails to MailVoid API with API key authentication
- Supports attachments and HTML/text content
- Runs as a containerized application
- Configurable via environment variables

## Configuration

### Environment Variables

- `SmtpServer__Port`: SMTP server port (default: 25)
- `SmtpServer__SslPort`: Implicit TLS/SSL port (default: 465)
- `SmtpServer__StartTlsPort`: STARTTLS port (default: 587)
- `SmtpServer__EnableSsl`: Enable TLS/SSL support (default: false)
- `SmtpServer__CertificatePath`: Path to SSL certificate file (PFX or PEM format)
- `SmtpServer__CertificatePassword`: Certificate password (for PFX files)
- `SmtpServer__TlsProtocols`: Supported TLS protocols (default: "Tls12,Tls13")
- `SmtpServer__Name`: Server name (default: "MailVoid SMTP Server")
- `SmtpServer__MaxMessageSize`: Maximum message size in bytes (default: 10MB)
- `MailVoidApi__BaseUrl`: MailVoid API base URL
- `MailVoidApi__WebhookEndpoint`: Webhook endpoint path (default: "/api/webhook/mail")
- `MailVoidApi__ApiKey`: API key for webhook authentication

### Configuration File

Alternatively, configure via `appsettings.json`:

```json
{
  "SmtpServer": {
    "Port": 25,
    "SslPort": 465,
    "StartTlsPort": 587,
    "Name": "MailVoid SMTP Server",
    "MaxMessageSize": 10485760,
    "EnableSsl": true,
    "CertificatePath": "/path/to/certificate.pfx",
    "CertificatePassword": "your-cert-password",
    "TlsProtocols": "Tls12,Tls13"
  },
  "MailVoidApi": {
    "BaseUrl": "http://localhost:5133",
    "WebhookEndpoint": "/api/webhook/mail",
    "ApiKey": "your-api-key-here"
  }
}
```

## Running with Docker

### Build the container:
```bash
docker build -t mailvoid-smtp .
```

### Run with Docker:
```bash
docker run -d \
  --name mailvoid-smtp \
  -p 2525:25 \
  -e MailVoidApi__BaseUrl=http://host.docker.internal:5133 \
  -e MailVoidApi__ApiKey=your-api-key-here \
  mailvoid-smtp
```

### Run with Docker Compose:
```bash
docker-compose up -d
```

## API Key Authentication

The SMTP server requires an API key to authenticate with the MailVoid API. Configure the API key in the MailVoid API's `appsettings.json`:

```json
{
  "ApiKeys": [
    {
      "Name": "SMTP Server",
      "Key": "your-secure-api-key",
      "Enabled": true,
      "Scopes": ["webhook"]
    }
  ]
}
```

## TLS/SSL Setup

For secure SMTP connections, see [TLS_SETUP.md](TLS_SETUP.md) for detailed instructions.

### Quick Start - Development Certificate

Generate a self-signed certificate for development:

```bash
# Linux/macOS
./generate-dev-cert.sh

# Windows
generate-dev-cert.bat
```

Then enable TLS in your configuration:
```bash
dotnet user-secrets set "SmtpServer:EnableSsl" "true"
dotnet user-secrets set "SmtpServer:CertificatePath" "./certs/smtp-dev.pfx"
dotnet user-secrets set "SmtpServer:CertificatePassword" "development"
```

## Usage

1. Configure your email client or application to send emails via the SMTP server
2. Set SMTP server to:
   - Plain text: `localhost:25` or `localhost:2580`
   - TLS/SSL: `localhost:465` (implicit TLS)
   - STARTTLS: `localhost:587` (explicit TLS)
3. Emails will be automatically forwarded to MailVoid for processing

## Development

### Prerequisites
- .NET 9.0 SDK
- Docker (optional)

### Build and run locally:
```bash
dotnet restore
dotnet run
```

### Testing
Send a test email using any SMTP client or tool:
```bash
# Using swaks (if installed)
swaks --to test@example.com --from sender@example.com --server localhost:2525
```

## Security Notes

- Change default API keys in production
- Use strong, unique API keys
- Consider running on non-standard ports to avoid conflicts
- Ensure proper firewall configuration
# SMTP Server Security Features

This document describes the security features implemented in the MailVoid SMTP Server.

## Overview

The MailVoid SMTP Server includes two main security components:
- **IMailboxFilter**: Controls which emails can be sent and received
- **IUserAuthenticator**: Handles SMTP authentication

## IMailboxFilter Implementation

The `MailVoidMailboxFilter` class provides the following security features:

### Sender Validation (`CanAcceptFromAsync`)
- Rejects emails with empty or invalid sender addresses
- Blocks emails from suspicious domains (example.com, test.com, localhost, etc.)
- Validates email format
- Enforces message size limits (10MB default)

### Recipient Validation (`CanDeliverToAsync`)
- Validates recipient email format
- Only accepts emails for configured test domains to prevent open relay
- Prevents email loops (sender same as recipient)
- Allowed domains are configured in `appsettings.json`

## IUserAuthenticator Implementation

The `MailVoidUserAuthenticator` class provides:

### Authentication Features
- Username/password authentication
- SHA256 password hashing support
- Plain text passwords for development (prefixed with "plain:")
- Session property tracking for authenticated users
- Detailed logging of authentication attempts

### Password Storage
Passwords in `appsettings.json` can be stored as:
1. **Plain text** (development only): `"username": "plain:password"`
2. **SHA256 hash**: `"username": "base64-encoded-hash"`

### Generating Password Hashes
Use the built-in utility:
```bash
dotnet run --project MailVoidSmtpServer -- hash-password yourpassword
```

## Configuration

Add to `appsettings.json`:

```json
{
  "SmtpAuthentication": {
    "RequireAuthentication": false,
    "AllowInsecureAuthentication": true,
    "Users": {
      "testuser": "plain:testpass",
      "admin": "Q1PqTfgGAd3hXx4D0i3K7FJH8nFmOw6s7L8N5tPPCys="
    }
  },
  "MailboxFilter": {
    "AllowedDomains": [
      "test.mailvoid.com",
      "dev.mailvoid.com",
      "staging.mailvoid.com"
    ],
    "BlockedDomains": [
      "example.com",
      "test.com",
      "localhost"
    ],
    "MaxMessageSizeBytes": 10485760
  }
}
```

### Configuration Options

**SmtpAuthentication:**
- `RequireAuthentication`: Whether to require authentication for all connections
- `AllowInsecureAuthentication`: Whether to allow AUTH over non-TLS connections
- `Users`: Dictionary of username to password (plain or hashed)

**MailboxFilter:**
- `AllowedDomains`: List of domains that can receive emails (prevents open relay)
- `BlockedDomains`: List of domains that cannot send emails to this server
- `MaxMessageSizeBytes`: Maximum message size in bytes (default: 10485760 = 10MB)

## Security Best Practices

1. **Production Environment**:
   - Always use hashed passwords (never plain text)
   - Enable `RequireAuthentication`
   - Disable `AllowInsecureAuthentication` and use TLS
   - Configure specific allowed domains

2. **Domain Configuration**:
   - Only add domains you control to `AllowedDomains`
   - Never add production domains to prevent abuse
   - Keep `BlockedDomains` updated with known spam sources
   - Review domain lists regularly

3. **Monitoring**:
   - Monitor authentication failures
   - Track rejected emails
   - Review session logs regularly

## Integration with SmtpServer

The security components integrate with the SmtpServer library:
- Services are registered via dependency injection
- SmtpServer resolves them via IServiceProvider
- Authentication state is tracked in session properties

## Logging

Security events are logged at various levels:
- **Information**: Successful authentications and accepted emails
- **Warning**: Failed authentications and rejected emails
- **Debug**: Detailed validation information

Configure logging in `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "MailVoidSmtpServer.Services.MailVoidMailboxFilter": "Information",
      "MailVoidSmtpServer.Services.MailVoidUserAuthenticator": "Information"
    }
  }
}
```
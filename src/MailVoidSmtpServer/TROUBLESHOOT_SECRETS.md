# Troubleshooting User Secrets on Linux

## Check if User Secrets are Properly Set

### 1. Verify Environment
The application only loads user secrets in Development environment. Check:
```bash
echo $ASPNETCORE_ENVIRONMENT
# Should output: Development (if not set, it defaults to Production)
```

Set it if needed:
```bash
export ASPNETCORE_ENVIRONMENT=Development
```

### 2. Check User Secrets Location
User secrets are stored in:
```bash
~/.microsoft/usersecrets/d2ee9be3-64bf-42ea-b392-36fc8ec6bf45/secrets.json
```

Verify the file exists:
```bash
ls -la ~/.microsoft/usersecrets/d2ee9be3-64bf-42ea-b392-36fc8ec6bf45/
cat ~/.microsoft/usersecrets/d2ee9be3-64bf-42ea-b392-36fc8ec6bf45/secrets.json
```

### 3. Initialize User Secrets (if not done)
From the project directory:
```bash
cd /path/to/MailVoidSmtpServer
dotnet user-secrets init
dotnet user-secrets set "MailVoidApi:ApiKey" "your-secret-key"
dotnet user-secrets set "MailVoidApi:BaseUrl" "https://your-api-url"
```

### 4. List Current Secrets
```bash
dotnet user-secrets list
```

### 5. Debug Output
When you run the application, it will now print debug information:
- Environment name
- Whether it's Development mode
- Current configuration values
- Whether secrets are loaded

## Common Issues

### Issue 1: Wrong Environment
**Problem**: ASPNETCORE_ENVIRONMENT is not set to "Development"
**Solution**: `export ASPNETCORE_ENVIRONMENT=Development`

### Issue 2: Missing UserSecrets Package
**Problem**: Microsoft.Extensions.Configuration.UserSecrets not installed
**Solution**: Already added to the project file

### Issue 3: Wrong UserSecretsId
**Problem**: Secrets stored under different ID
**Solution**: Check the UserSecretsId in the .csproj file matches your secrets directory

### Issue 4: Secrets File Doesn't Exist
**Problem**: Never initialized user secrets
**Solution**: Run `dotnet user-secrets init` and set your secrets

## Example Secrets File
Your `~/.microsoft/usersecrets/d2ee9be3-64bf-42ea-b392-36fc8ec6bf45/secrets.json` should look like:
```json
{
  "MailVoidApi:ApiKey": "your-secret-api-key",
  "MailVoidApi:BaseUrl": "https://your-api-domain.com",
  "SmtpServer:CertificatePath": "/path/to/your/cert.pfx",
  "SmtpServer:CertificatePassword": "cert-password"
}
```
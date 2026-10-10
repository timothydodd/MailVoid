# Debug Environment and User Secrets

## Step 1: Verify Environment Variable
```bash
# Check if it's actually set
echo "ASPNETCORE_ENVIRONMENT = '$ASPNETCORE_ENVIRONMENT'"

# If empty, set it
export ASPNETCORE_ENVIRONMENT=Development

# Verify again
echo "ASPNETCORE_ENVIRONMENT = '$ASPNETCORE_ENVIRONMENT'"
```

## Step 2: Check User Secrets Directory
```bash
# Check if directory exists
ls -la ~/.microsoft/usersecrets/

# Check specific secrets directory
ls -la ~/.microsoft/usersecrets/d2ee9be3-64bf-42ea-b392-36fc8ec6bf45/

# Check secrets file content
cat ~/.microsoft/usersecrets/d2ee9be3-64bf-42ea-b392-36fc8ec6bf45/secrets.json
```

## Step 3: Initialize User Secrets (if needed)
```bash
cd /path/to/your/MailVoidSmtpServer/project

# Initialize user secrets
dotnet user-secrets init

# Set some test secrets
dotnet user-secrets set "MailVoidApi:ApiKey" "test-secret-key"
dotnet user-secrets set "MailVoidApi:BaseUrl" "https://test.example.com"

# List all secrets
dotnet user-secrets list
```

## Step 4: Run with Debug Output
```bash
# Run the application (it will now show debug info)
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

## Step 5: Manual .env File Loading (Alternative)
If you want to use a .env file, add this package and code:

### Add Package:
```bash
dotnet add package DotNetEnv
```

### Then modify Program.cs to load .env:
```csharp
// At the top of Main method
DotNetEnv.Env.Load();
```

## Expected Output
When you run the app, you should see debug output showing:
- Environment variables
- Environment name (should be "Development")
- Whether secrets file exists
- Current configuration values

## Common Issues:

### Issue 1: Environment not persisting
**Solution**: Add to shell profile:
```bash
echo 'export ASPNETCORE_ENVIRONMENT=Development' >> ~/.bashrc
source ~/.bashrc
```

### Issue 2: Wrong shell
**Check what shell you're using:**
```bash
echo $SHELL
```

**If using zsh, use .zshrc instead of .bashrc:**
```bash
echo 'export ASPNETCORE_ENVIRONMENT=Development' >> ~/.zshrc
source ~/.zshrc
```

### Issue 3: Secrets directory doesn't exist
**Solution**: Run `dotnet user-secrets init` from project directory

### Issue 4: Wrong project directory
**Make sure you're in the right directory when setting secrets:**
```bash
cd /path/to/MailVoidSmtpServer
pwd  # Should show the directory containing MailVoidSmtpServer.csproj
dotnet user-secrets init
```
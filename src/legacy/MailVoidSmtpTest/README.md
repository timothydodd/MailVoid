# MailVoid SMTP Test

This is a test application that sends emails to the local MailVoid SMTP server to verify the complete email processing flow.

## How to Run the Test

### Prerequisites

1. **Start the MailVoid API**: 
   ```bash
   cd ../MailVoidApi
   dotnet run
   ```
   The API should be running on http://localhost:5133

2. **Start the MailVoid SMTP Server**:
   ```bash
   cd ../MailVoidSmtpServer
   dotnet run
   ```
   The SMTP server should be running on port 25

3. **Ensure API Key Configuration**:
   - The SMTP server uses API key: `smtp-server-key-change-this-in-production`
   - This key must be enabled in the MailVoid API's `appsettings.json`

### Run the Test

```bash
cd MailVoidSmtpTest
dotnet run
```

## What the Test Does

The test sends 4 different types of emails to verify various scenarios:

1. **Simple Text Email**: Basic plain text email
2. **HTML Email**: Email with both HTML and text versions
3. **Encoded Subject**: Email with special characters and Unicode in the subject
4. **Multiple Recipients**: Email with To, CC, and BCC recipients

## Expected Flow

1. Test sends email to SMTP server (localhost:25)
2. SMTP server receives email and forwards it to MailVoid API webhook
3. API webhook processes the MailData and stores it in the database
4. You can view the received emails through the MailVoid web interface

## Verifying Results

After running the test:

1. Open the MailVoid web interface (usually http://localhost:5133)
2. Check that all 4 test emails appear in the inbox
3. Verify that:
   - Plain text and HTML content are properly displayed
   - Special characters in subjects are decoded correctly
   - Multiple recipients are handled appropriately
   - Email metadata (timestamps, headers) are preserved

## Troubleshooting

- **Connection refused**: Ensure the SMTP server is running on port 25
- **API errors**: Check that the API key is correctly configured and enabled
- **Emails not appearing**: Check the API logs for webhook processing errors
- **Permission denied on port 25**: Try running with elevated privileges or change SMTP port to 2525
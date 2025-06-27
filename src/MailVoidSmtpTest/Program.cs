using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using MimeKit.Utils;
using System.Linq;
using System.Text;

namespace MailVoidSmtpTest;

class Program
{
    private static IConfiguration? _configuration;
    static async Task Main(string[] args)
    {
        Console.WriteLine("MailVoid SMTP Test");
        Console.WriteLine("=================");

        // Load configuration
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddUserSecrets<Program>(optional: true)
            .AddCommandLine(args)
            .Build();

        var config = _configuration.GetSection("SmtpTest");
        var smtpHost = config["Host"] ?? "localhost";
        var standardPort = int.Parse(config["StandardPort"] ?? "25");
        var testPort = int.Parse(config["TestPort"] ?? "2580");
        var sslPort = int.Parse(config["SslPort"] ?? "465");
        var fromEmail = config["FromEmail"] ?? "sender@testdomain.com";
        var toEmail = config["ToEmail"] ?? "recipient@mailvoid.com";
        var enableSslOption = bool.Parse(config["EnableSslOption"] ?? "false");

        int smtpPort;
        bool useSsl;

        // Ask user which port to test
        Console.WriteLine("Select SMTP port to test:");
        Console.WriteLine($"1. Port {standardPort} (Standard SMTP - Production)");
        Console.WriteLine($"2. Port {testPort} (Test SMTP - Recommended)");
        if (enableSslOption)
        {
            Console.WriteLine($"3. Port {sslPort} (SMTP with SSL/TLS)");
            Console.WriteLine($"4. Port {standardPort} (SMTP with SSL/TLS)");
        }
        Console.Write($"Enter choice (1, 2{(enableSslOption ? ", 3, or 4" : "")}): ");

        var choice = Console.ReadLine();
        
        switch (choice)
        {
            case "1":
                smtpPort = standardPort;
                useSsl = false;
                break;
            case "2":
                smtpPort = testPort;
                useSsl = false;
                break;
            case "3" when enableSslOption:
                smtpPort = sslPort;
                useSsl = true;
                break;
            case "4" when enableSslOption:
                smtpPort = standardPort;
                useSsl = true;
                break;
            default:
                Console.WriteLine("Invalid choice, defaulting to test port...");
                smtpPort = testPort;
                useSsl = false;
                break;
        }

        Console.WriteLine($"\nTesting SMTP server at {smtpHost}:{smtpPort} (SSL: {useSsl})");
        Console.WriteLine();

        // Test 1: Simple text email
        Console.WriteLine("Test 1: Sending simple text email...");
        await SendSimpleTextEmail(smtpHost, smtpPort, useSsl, fromEmail, toEmail);

        // Test 2: HTML email
        Console.WriteLine("\nTest 2: Sending HTML email...");
        await SendHtmlEmail(smtpHost, smtpPort, useSsl, fromEmail, toEmail);

        // Test 3: Email with special characters in subject
        Console.WriteLine("\nTest 3: Sending email with encoded subject...");
        await SendEmailWithEncodedSubject(smtpHost, smtpPort, useSsl, fromEmail, toEmail);

        // Test 4: Email with multiple recipients
        Console.WriteLine("\nTest 4: Sending email with multiple recipients...");
        await SendEmailWithMultipleRecipients(smtpHost, smtpPort, useSsl, fromEmail,
            new[] { toEmail, "cc@mailvoid.com", "bcc@test.mailvoid.com" });

        // Test 5: Authentication blocking test
        Console.WriteLine("\nTest 5: Testing authentication blocking...");
        await TestAuthenticationBlocked(smtpHost, smtpPort, useSsl, fromEmail, toEmail);

        // Test 6: Email to different subdomains for mail groups
        Console.WriteLine("\nTest 6: Testing email routing to different mail groups...");
        await SendEmailsToMailGroups(smtpHost, smtpPort, useSsl, fromEmail);

        // Test 7: Email with attachments
        Console.WriteLine("\nTest 7: Sending email with attachments...");
        await SendEmailWithAttachments(smtpHost, smtpPort, useSsl, fromEmail, toEmail);

        // Test 8: Email with inline images
        Console.WriteLine("\nTest 8: Sending email with inline images...");
        await SendEmailWithInlineImages(smtpHost, smtpPort, useSsl, fromEmail, toEmail);

        // Test 9: Large email test
        Console.WriteLine("\nTest 9: Sending large email...");
        await SendLargeEmail(smtpHost, smtpPort, useSsl, fromEmail, toEmail);

        // Test 10: Various encoding tests
        Console.WriteLine("\nTest 10: Testing various encodings...");
        await SendEmailsWithDifferentEncodings(smtpHost, smtpPort, useSsl, fromEmail, toEmail);

        // Test 11: Edge cases and special formats
        Console.WriteLine("\nTest 11: Testing edge cases...");
        await SendEdgeCaseEmails(smtpHost, smtpPort, useSsl, fromEmail, toEmail);

        Console.WriteLine("\nAll tests completed!");
        Console.WriteLine("Check the MailVoid web interface to see if emails were received.");
        Console.WriteLine("\nTest Summary:");
        Console.WriteLine("- Simple text and HTML emails");
        Console.WriteLine("- Special characters and encodings");
        Console.WriteLine("- Multiple recipients (To, CC, BCC)");
        Console.WriteLine("- Authentication blocking");
        Console.WriteLine("- Mail group routing (different subdomains)");
        Console.WriteLine("- Attachments (PDF, images, documents)");
        Console.WriteLine("- Inline images in HTML");
        Console.WriteLine("- Large emails");
        Console.WriteLine("- Various character encodings");
        Console.WriteLine("- Edge cases and special formats");
    }

    static async Task SendSimpleTextEmail(string host, int port, bool useSsl, string from, string to)
    {
        try
        {
            Console.WriteLine($"[DEBUG] Creating message from {from} to {to}");
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Test Sender", from));
            message.To.Add(new MailboxAddress("Test Recipient", to));
            message.Subject = "Simple Text Email Test";

            message.Body = new TextPart("plain")
            {
                Text = @"This is a simple test email sent to the MailVoid SMTP server.
                
It contains plain text content and should be processed correctly by the webhook.

Best regards,
SMTP Test"
            };

            Console.WriteLine($"[DEBUG] Connecting to SMTP server at {host}:{port} (SSL: {useSsl})");
            using var client = await CreateSmtpClient(host, port, useSsl);

            Console.WriteLine($"[DEBUG] Connected: {client.IsConnected}, Authenticated: {client.IsAuthenticated}");
            Console.WriteLine($"[DEBUG] Capabilities: {string.Join(", ", client.Capabilities)}");

            Console.WriteLine("[DEBUG] Sending message...");
            await client.SendAsync(message);
            Console.WriteLine("[DEBUG] Message sent successfully");

            Console.WriteLine("[DEBUG] Disconnecting...");
            await client.DisconnectAsync(true);
            Console.WriteLine("[DEBUG] Disconnected");

            Console.WriteLine("✓ Simple text email sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send simple text email: {ex.Message}");
            Console.WriteLine($"[DEBUG] Exception type: {ex.GetType().Name}");
            Console.WriteLine($"[DEBUG] Stack trace: {ex.StackTrace}");
        }
    }

    static async Task SendHtmlEmail(string host, int port, bool useSsl, string from, string to)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("HTML Test Sender", from));
            message.To.Add(new MailboxAddress("HTML Test Recipient", to));
            message.Subject = "HTML Email Test";

            var builder = new BodyBuilder();
            builder.TextBody = @"This is the text version of the email for clients that don't support HTML.";
            builder.HtmlBody = @"
<html>
<body>
    <h1>HTML Email Test</h1>
    <p>This is an <strong>HTML email</strong> sent to test the MailVoid SMTP server.</p>
    <ul>
        <li>It contains HTML formatting</li>
        <li>It has both text and HTML versions</li>
        <li>It should be processed correctly by the webhook</li>
    </ul>
    <p>Best regards,<br/>
    <em>SMTP Test</em></p>
</body>
</html>";

            message.Body = builder.ToMessageBody();

            using var client = await CreateSmtpClient(host, port, useSsl);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine("✓ HTML email sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send HTML email: {ex.Message}");
        }
    }

    static async Task SendEmailWithEncodedSubject(string host, int port, bool useSsl, string from, string to)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Encoded Test Sender", from));
            message.To.Add(new MailboxAddress("Encoded Test Recipient", to));
            message.Subject = "Test with Special Characters: Üñíçødé ñämé & símböls 📧";

            message.Body = new TextPart("plain")
            {
                Text = @"This email tests the handling of special characters and encoding.

Subject contains: Üñíçødé ñämé & símböls 📧

The email processing should handle these characters correctly.

Best regards,
SMTP Test"
            };

            using var client = await CreateSmtpClient(host, port, useSsl);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine("✓ Email with encoded subject sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send email with encoded subject: {ex.Message}");
        }
    }

    static async Task SendEmailWithMultipleRecipients(string host, int port, bool useSsl, string from, string[] recipients)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Multi-Recipient Sender", from));

            // Add primary recipient
            message.To.Add(new MailboxAddress("Primary Recipient", recipients[0]));

            // Add CC if available
            if (recipients.Length > 1)
            {
                message.Cc.Add(new MailboxAddress("CC Recipient", recipients[1]));
            }

            // Add BCC if available  
            if (recipients.Length > 2)
            {
                message.Bcc.Add(new MailboxAddress("BCC Recipient", recipients[2]));
            }

            message.Subject = "Multiple Recipients Test";

            message.Body = new TextPart("plain")
            {
                Text = @"This email tests sending to multiple recipients.

Primary recipient: " + recipients[0] + @"
CC recipient: " + (recipients.Length > 1 ? recipients[1] : "None") + @"
BCC recipient: " + (recipients.Length > 2 ? recipients[2] : "None") + @"

The webhook should extract all recipients correctly.

Best regards,
SMTP Test"
            };

            using var client = await CreateSmtpClient(host, port, useSsl);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine("✓ Email with multiple recipients sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send email with multiple recipients: {ex.Message}");
        }
    }

    static async Task TestAuthenticationBlocked(string host, int port, bool useSsl, string from, string to)
    {
        try
        {
            Console.WriteLine("Testing that user authentication is properly blocked...");
            
            // Create message for testing
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Test Sender", from));
            message.To.Add(new MailboxAddress("Test Recipient", to));
            message.Subject = "Authentication Test Email";
            message.Body = new TextPart("plain") { Text = "This email tests that authentication is blocked." };

            using var client = new SmtpClient();
            
            // For self-signed certificates in development
            var allowSelfSigned = bool.Parse(_configuration?["SmtpTest:AllowSelfSignedCertificates"] ?? "true");
            if (allowSelfSigned)
            {
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            }

            Console.WriteLine($"[AUTH TEST] Connecting to {host}:{port}...");
            
            if (useSsl)
            {
                await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            }
            else
            {
                await client.ConnectAsync(host, port, SecureSocketOptions.Auto);
            }

            Console.WriteLine("[AUTH TEST] Connected successfully");

            // Test 1: Try authentication with fake credentials
            Console.WriteLine("[AUTH TEST] Attempting authentication with username 'testuser' and password 'testpass'...");
            
            try
            {
                await client.AuthenticateAsync("testuser", "testpass");
                Console.WriteLine("✗ SECURITY ISSUE: Authentication succeeded when it should be blocked!");
            }
            catch (AuthenticationException)
            {
                Console.WriteLine("✓ Authentication correctly blocked (AuthenticationException)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✓ Authentication blocked with exception: {ex.GetType().Name}");
            }

            // Test 2: Try authentication with admin credentials
            Console.WriteLine("[AUTH TEST] Attempting authentication with username 'admin' and password 'admin123'...");
            
            try
            {
                await client.AuthenticateAsync("admin", "admin123");
                Console.WriteLine("✗ SECURITY ISSUE: Admin authentication succeeded when it should be blocked!");
            }
            catch (AuthenticationException)
            {
                Console.WriteLine("✓ Admin authentication correctly blocked (AuthenticationException)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✓ Admin authentication blocked with exception: {ex.GetType().Name}");
            }

            // Test 3: Verify we can still send emails without authentication
            Console.WriteLine("[AUTH TEST] Testing email sending without authentication...");
            
            try
            {
                await client.SendAsync(message);
                Console.WriteLine("✓ Email sent successfully without authentication (as expected)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to send email without authentication: {ex.Message}");
            }

            await client.DisconnectAsync(true);
            Console.WriteLine("✓ Authentication blocking test completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Authentication test failed: {ex.Message}");
            Console.WriteLine($"   Exception type: {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    static async Task<SmtpClient> CreateSmtpClient(string host, int port, bool useSsl)
    {
        var client = new SmtpClient(new ProtocolLogger(Console.OpenStandardOutput()));


        // For self-signed certificates in development
        var allowSelfSigned = bool.Parse(_configuration?["SmtpTest:AllowSelfSignedCertificates"] ?? "true");
        if (allowSelfSigned)
        {
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;
        }

        Console.WriteLine($"[DEBUG] Attempting connection to {host}:{port}...");
        Console.WriteLine($"[DEBUG] SSL mode: {(useSsl ? "StartTls" : "None")}");

        try
        {
            if (useSsl)
            {
                await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            }
            else
             {
                await client.ConnectAsync(host, port, SecureSocketOptions.Auto);
            }

            Console.WriteLine($"[DEBUG] Connection established successfully");
            Console.WriteLine($"[DEBUG] Local endpoint: {client.LocalEndPoint}");
            Console.WriteLine($"[DEBUG] IsSecure: {client.IsSecure}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Connection failed: {ex.Message}");
            throw;
        }

        return client;
    }

    static async Task SendEmailsToMailGroups(string host, int port, bool useSsl, string from)
    {
        try
        {
            var subdomains = new[] { "development", "staging", "production", "testing", "admin", "support" };
            
            foreach (var subdomain in subdomains)
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress($"{subdomain} Team", from));
                message.To.Add(new MailboxAddress($"{subdomain} Recipient", $"test@{subdomain}.mailvoid.com"));
                message.Subject = $"Test Email for {subdomain} Mail Group";

                var builder = new BodyBuilder();
                builder.TextBody = $@"This is a test email for the {subdomain} mail group.

This email should be automatically routed to the '{subdomain}' mail group based on the subdomain in the recipient address.

Mail Group Details:
- Subdomain: {subdomain}
- Expected Path: subdomain/{subdomain}
- Email To: test@{subdomain}.mailvoid.com

This helps test the automatic mail group creation and routing functionality.

Best regards,
{subdomain} Team";

                builder.HtmlBody = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
    <h2>Test Email for {subdomain} Mail Group</h2>
    <p>This is a test email for the <strong>{subdomain}</strong> mail group.</p>
    <div style='background-color: #f0f0f0; padding: 15px; margin: 10px 0;'>
        <h3>Mail Group Details:</h3>
        <ul>
            <li><strong>Subdomain:</strong> {subdomain}</li>
            <li><strong>Expected Path:</strong> subdomain/{subdomain}</li>
            <li><strong>Email To:</strong> test@{subdomain}.mailvoid.com</li>
        </ul>
    </div>
    <p>This helps test the automatic mail group creation and routing functionality.</p>
    <p>Best regards,<br/><em>{subdomain} Team</em></p>
</body>
</html>";

                message.Body = builder.ToMessageBody();

                using var client = await CreateSmtpClient(host, port, useSsl);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                
                Console.WriteLine($"  ✓ Email sent to {subdomain}.mailvoid.com");
            }

            Console.WriteLine("✓ All mail group routing emails sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send mail group emails: {ex.Message}");
        }
    }

    static async Task SendEmailWithAttachments(string host, int port, bool useSsl, string from, string to)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Attachment Test", from));
            message.To.Add(new MailboxAddress("Attachment Recipient", to));
            message.Subject = "Email with Various Attachments";

            var builder = new BodyBuilder();
            builder.TextBody = @"This email contains various attachments to test attachment handling.

Attachments included:
1. PDF document (sample.pdf)
2. Text file (readme.txt)
3. CSV data (data.csv)
4. JSON file (config.json)

Please verify all attachments are received and processed correctly.";

            // Create sample PDF content (simplified)
            var pdfContent = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj\n4 0 obj\n<< /Length 44 >>\nstream\nBT /F1 12 Tf 100 700 Td (Sample PDF) Tj ET\nendstream\nendobj\nxref\n0 5\n0000000000 65535 f\n0000000009 00000 n\n0000000058 00000 n\n0000000115 00000 n\n0000000291 00000 n\ntrailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n384\n%%EOF";
            builder.Attachments.Add("sample.pdf", System.Text.Encoding.UTF8.GetBytes(pdfContent), ContentType.Parse("application/pdf"));

            // Add text file
            builder.Attachments.Add("readme.txt", System.Text.Encoding.UTF8.GetBytes("This is a sample text file attachment.\nIt contains multiple lines.\nTesting attachment handling."), ContentType.Parse("text/plain"));

            // Add CSV file
            var csvContent = "Name,Email,Department\nJohn Doe,john@example.com,Engineering\nJane Smith,jane@example.com,Marketing\nBob Johnson,bob@example.com,Sales";
            builder.Attachments.Add("data.csv", System.Text.Encoding.UTF8.GetBytes(csvContent), ContentType.Parse("text/csv"));

            // Add JSON file
            var jsonContent = @"{
  ""application"": ""MailVoid"",
  ""version"": ""1.0.0"",
  ""settings"": {
    ""debug"": true,
    ""maxRetries"": 3,
    ""timeout"": 30
  }
}";
            builder.Attachments.Add("config.json", System.Text.Encoding.UTF8.GetBytes(jsonContent), ContentType.Parse("application/json"));

            message.Body = builder.ToMessageBody();

            using var client = await CreateSmtpClient(host, port, useSsl);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine("✓ Email with attachments sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send email with attachments: {ex.Message}");
        }
    }

    static async Task SendEmailWithInlineImages(string host, int port, bool useSsl, string from, string to)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Image Test", from));
            message.To.Add(new MailboxAddress("Image Recipient", to));
            message.Subject = "Email with Inline Images and Rich HTML";

            var builder = new BodyBuilder();

            // Create a simple 1x1 red pixel PNG
            var redPixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");
            var image1 = builder.LinkedResources.Add("red-pixel.png", redPixelPng, ContentType.Parse("image/png"));
            image1.ContentId = MimeUtils.GenerateMessageId();

            // Create a simple 1x1 blue pixel PNG
            var bluePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
            var image2 = builder.LinkedResources.Add("blue-pixel.png", bluePixelPng, ContentType.Parse("image/png"));
            image2.ContentId = MimeUtils.GenerateMessageId();

            builder.HtmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; }}
        .header {{ background-color: #4CAF50; color: white; padding: 20px; text-align: center; }}
        .content {{ margin: 20px 0; }}
        .image-container {{ display: flex; gap: 20px; margin: 20px 0; }}
        .image-box {{ border: 2px solid #ddd; padding: 10px; text-align: center; }}
        .footer {{ background-color: #f1f1f1; padding: 10px; text-align: center; font-size: 12px; }}
        table {{ border-collapse: collapse; width: 100%; }}
        th, td {{ border: 1px solid #ddd; padding: 8px; text-align: left; }}
        th {{ background-color: #4CAF50; color: white; }}
    </style>
</head>
<body>
    <div class='header'>
        <h1>Rich HTML Email with Inline Images</h1>
    </div>
    
    <div class='content'>
        <h2>Welcome to MailVoid Testing!</h2>
        <p>This email demonstrates various HTML features and inline images.</p>
        
        <div class='image-container'>
            <div class='image-box'>
                <h3>Image 1</h3>
                <img src='cid:{image1.ContentId}' alt='Red Pixel' width='100' height='100' style='background-color: #f0f0f0;'>
                <p>Red pixel scaled to 100x100</p>
            </div>
            <div class='image-box'>
                <h3>Image 2</h3>
                <img src='cid:{image2.ContentId}' alt='Blue Pixel' width='100' height='100' style='background-color: #f0f0f0;'>
                <p>Blue pixel scaled to 100x100</p>
            </div>
        </div>
        
        <h3>Sample Data Table</h3>
        <table>
            <tr>
                <th>Feature</th>
                <th>Status</th>
                <th>Description</th>
            </tr>
            <tr>
                <td>HTML Support</td>
                <td>✅ Active</td>
                <td>Full HTML rendering with styles</td>
            </tr>
            <tr>
                <td>Inline Images</td>
                <td>✅ Active</td>
                <td>Images embedded using Content-ID</td>
            </tr>
            <tr>
                <td>CSS Styling</td>
                <td>✅ Active</td>
                <td>Internal CSS for rich formatting</td>
            </tr>
        </table>
        
        <h3>Formatted Lists</h3>
        <ul style='list-style-type: square;'>
            <li>Rich text formatting</li>
            <li>Inline images using CID references</li>
            <li>CSS styling support</li>
            <li>Table layouts</li>
        </ul>
    </div>
    
    <div class='footer'>
        <p>© 2024 MailVoid Test Suite | This is a test email</p>
    </div>
</body>
</html>";

            builder.TextBody = @"Rich HTML Email with Inline Images

This is the plain text version of the email.
It contains the same information but without formatting.

Features tested:
- HTML Support: Active - Full HTML rendering with styles
- Inline Images: Active - Images embedded using Content-ID
- CSS Styling: Active - Internal CSS for rich formatting

© 2024 MailVoid Test Suite";

            message.Body = builder.ToMessageBody();

            using var client = await CreateSmtpClient(host, port, useSsl);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine("✓ Email with inline images sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send email with inline images: {ex.Message}");
        }
    }

    static async Task SendLargeEmail(string host, int port, bool useSsl, string from, string to)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Large Email Test", from));
            message.To.Add(new MailboxAddress("Large Email Recipient", to));
            message.Subject = "Large Email Test - Performance and Handling";

            var builder = new BodyBuilder();
            
            // Generate large content
            var largeText = new System.Text.StringBuilder();
            largeText.AppendLine("This is a large email to test performance and handling of big messages.\n");
            
            // Add 1000 paragraphs of Lorem Ipsum
            var loremIpsum = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";
            
            for (int i = 0; i < 1000; i++)
            {
                largeText.AppendLine($"\nParagraph {i + 1}:");
                largeText.AppendLine(loremIpsum);
            }
            
            builder.TextBody = largeText.ToString();
            
            // Also create large HTML content
            var htmlBuilder = new System.Text.StringBuilder();
            htmlBuilder.Append(@"<html><body style='font-family: Arial, sans-serif;'><h1>Large Email Test</h1>");
            htmlBuilder.Append("<p>This email contains a large amount of content to test handling of big messages.</p>");
            
            for (int i = 0; i < 1000; i++)
            {
                htmlBuilder.Append($"<div style='margin: 10px 0; padding: 10px; background-color: #{(i % 2 == 0 ? "f0f0f0" : "ffffff")};'>");
                htmlBuilder.Append($"<h3>Section {i + 1}</h3>");
                htmlBuilder.Append($"<p>{loremIpsum}</p>");
                htmlBuilder.Append("</div>");
            }
            
            htmlBuilder.Append("</body></html>");
            builder.HtmlBody = htmlBuilder.ToString();

            // Add a large attachment (1MB of random data)
            var largeData = new byte[1024 * 1024]; // 1MB
            new Random().NextBytes(largeData);
            builder.Attachments.Add("large-file.bin", largeData, ContentType.Parse("application/octet-stream"));

            message.Body = builder.ToMessageBody();

            using var client = await CreateSmtpClient(host, port, useSsl);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine("✓ Large email sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send large email: {ex.Message}");
        }
    }

    static async Task SendEmailsWithDifferentEncodings(string host, int port, bool useSsl, string from, string to)
    {
        try
        {
            // Test 1: UTF-8 with various Unicode characters
            var message1 = new MimeMessage();
            message1.From.Add(new MailboxAddress("UTF-8 Test", from));
            message1.To.Add(new MailboxAddress("Encoding Test", to));
            message1.Subject = "UTF-8 Encoding: 你好世界 🌍 مرحبا بالعالم";
            message1.Body = new TextPart("plain")
            {
                Text = @"UTF-8 Encoding Test

Chinese: 你好世界 (Hello World)
Japanese: こんにちは世界 (Konnichiwa Sekai)
Arabic: مرحبا بالعالم (Marhaban Bialealam)
Russian: Привет мир (Privet mir)
Greek: Γεια σου κόσμε (Geia sou kosme)
Emoji: 😀 😃 😄 😁 😆 🌍 🌎 🌏 ✉️ 📧
Mathematical: ∑ ∏ ∫ ∂ ∆ ∇ ∈ ∉ ∞
Currency: $ € £ ¥ ₹ ₽ ¢ ₩

All characters should display correctly."
            };

            using (var client = await CreateSmtpClient(host, port, useSsl))
            {
                await client.SendAsync(message1);
                await client.DisconnectAsync(true);
            }
            Console.WriteLine("  ✓ UTF-8 encoded email sent");

            // Test 2: ISO-8859-1 (Latin-1)
            var message2 = new MimeMessage();
            message2.From.Add(new MailboxAddress("ISO-8859-1 Test", from));
            message2.To.Add(new MailboxAddress("Encoding Test", to));
            message2.Subject = "ISO-8859-1: café, naïve, résumé";
            var textPart = new TextPart("plain")
            {
                Text = "ISO-8859-1 (Latin-1) Test\n\nFrench: café, naïve, résumé, château\nSpanish: ñ, ¿Cómo estás?\nGerman: ä, ö, ü, ß\nSpecial: © ® ™ ° ± µ"
            };
            textPart.ContentType.Charset = "iso-8859-1";
            message2.Body = textPart;

            using (var client = await CreateSmtpClient(host, port, useSsl))
            {
                await client.SendAsync(message2);
                await client.DisconnectAsync(true);
            }
            Console.WriteLine("  ✓ ISO-8859-1 encoded email sent");

            // Test 3: Base64 encoded content
            var message3 = new MimeMessage();
            message3.From.Add(new MailboxAddress("Base64 Test", from));
            message3.To.Add(new MailboxAddress("Encoding Test", to));
            message3.Subject = "Base64 Encoded Content Test";
            var base64Part = new TextPart("plain")
            {
                Text = "This is a Base64 encoded message with special characters: ñ ü ö ® © ™",
                ContentTransferEncoding = ContentEncoding.Base64
            };
            message3.Body = base64Part;

            using (var client = await CreateSmtpClient(host, port, useSsl))
            {
                await client.SendAsync(message3);
                await client.DisconnectAsync(true);
            }
            Console.WriteLine("  ✓ Base64 encoded email sent");

            // Test 4: Quoted-Printable encoding
            var message4 = new MimeMessage();
            message4.From.Add(new MailboxAddress("QP Test", from));
            message4.To.Add(new MailboxAddress("Encoding Test", to));
            message4.Subject = "Quoted-Printable Encoding Test";
            var qpPart = new TextPart("plain")
            {
                Text = "This is a Quoted-Printable encoded message.\nIt handles special chars: café résumé naïve\nAnd long lines that need to be wrapped at the 76th character position exactly here.",
                ContentTransferEncoding = ContentEncoding.QuotedPrintable
            };
            message4.Body = qpPart;

            using (var client = await CreateSmtpClient(host, port, useSsl))
            {
                await client.SendAsync(message4);
                await client.DisconnectAsync(true);
            }
            Console.WriteLine("  ✓ Quoted-Printable encoded email sent");

            Console.WriteLine("✓ All encoding test emails sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send encoding test emails: {ex.Message}");
        }
    }

    static async Task SendEdgeCaseEmails(string host, int port, bool useSsl, string from, string to)
    {
        try
        {
            // Test 1: Empty subject and body
            var message1 = new MimeMessage();
            message1.From.Add(new MailboxAddress("Edge Case Test", from));
            message1.To.Add(new MailboxAddress("Edge Case Recipient", to));
            message1.Subject = ""; // Empty subject
            message1.Body = new TextPart("plain") { Text = "" }; // Empty body

            using (var client = await CreateSmtpClient(host, port, useSsl))
            {
                await client.SendAsync(message1);
                await client.DisconnectAsync(true);
            }
            Console.WriteLine("  ✓ Empty subject/body email sent");

            // Test 2: Very long subject line
            var message2 = new MimeMessage();
            message2.From.Add(new MailboxAddress("Long Subject Test", from));
            message2.To.Add(new MailboxAddress("Edge Case Recipient", to));
            message2.Subject = string.Concat(Enumerable.Repeat("This is a very long subject line that exceeds normal limits. ", 10));
            message2.Body = new TextPart("plain") { Text = "Testing very long subject line handling." };

            using (var client = await CreateSmtpClient(host, port, useSsl))
            {
                await client.SendAsync(message2);
                await client.DisconnectAsync(true);
            }
            Console.WriteLine("  ✓ Long subject email sent");

            // Test 3: Special characters in sender/recipient names
            var message3 = new MimeMessage();
            message3.From.Add(new MailboxAddress("Sender \"Special\" <Name>", from));
            message3.To.Add(new MailboxAddress("Recipient; Name (Test)", to));
            message3.Subject = "Special Characters in Names Test";
            message3.Body = new TextPart("plain") { Text = "Testing special characters in sender/recipient names." };

            using (var client = await CreateSmtpClient(host, port, useSsl))
            {
                await client.SendAsync(message3);
                await client.DisconnectAsync(true);
            }
            Console.WriteLine("  ✓ Special chars in names email sent");

            // Test 4: Multiple From addresses (edge case)
            var message4 = new MimeMessage();
            message4.From.Add(new MailboxAddress("Sender 1", from));
            message4.From.Add(new MailboxAddress("Sender 2", "sender2@test.com"));
            message4.To.Add(new MailboxAddress("Edge Case Recipient", to));
            message4.Subject = "Multiple From Addresses Test";
            message4.Body = new TextPart("plain") { Text = "Testing email with multiple From addresses." };

            using (var client = await CreateSmtpClient(host, port, useSsl))
            {
                await client.SendAsync(message4);
                await client.DisconnectAsync(true);
            }
            Console.WriteLine("  ✓ Multiple From addresses email sent");

            // Test 5: Reply-To and custom headers
            var message5 = new MimeMessage();
            message5.From.Add(new MailboxAddress("Custom Headers Test", from));
            message5.To.Add(new MailboxAddress("Edge Case Recipient", to));
            message5.ReplyTo.Add(new MailboxAddress("Reply Here", "reply@test.com"));
            message5.Subject = "Custom Headers Test";
            message5.Headers.Add("X-Custom-Header", "CustomValue");
            message5.Headers.Add("X-Priority", "1");
            message5.Headers.Add("X-Mailer", "MailVoid SMTP Test Suite");
            message5.Importance = MessageImportance.High;
            message5.Priority = MessagePriority.Urgent;
            message5.Body = new TextPart("plain") { Text = "Testing custom headers and Reply-To functionality." };

            using (var client = await CreateSmtpClient(host, port, useSsl))
            {
                await client.SendAsync(message5);
                await client.DisconnectAsync(true);
            }
            Console.WriteLine("  ✓ Custom headers email sent");

            // Test 6: Mixed content multipart
            var message6 = new MimeMessage();
            message6.From.Add(new MailboxAddress("Multipart Test", from));
            message6.To.Add(new MailboxAddress("Edge Case Recipient", to));
            message6.Subject = "Complex Multipart Message";

            var multipart = new Multipart("mixed");
            
            // Add text part
            multipart.Add(new TextPart("plain") { Text = "This is the plain text part." });
            
            // Add HTML part
            multipart.Add(new TextPart("html") { Text = "<p>This is the <b>HTML</b> part.</p>" });
            
            // Add another text part
            multipart.Add(new TextPart("plain") { Text = "This is another text part in the same message." });

            message6.Body = multipart;

            using (var client = await CreateSmtpClient(host, port, useSsl))
            {
                await client.SendAsync(message6);
                await client.DisconnectAsync(true);
            }
            Console.WriteLine("  ✓ Complex multipart email sent");

            Console.WriteLine("✓ All edge case emails sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send edge case emails: {ex.Message}");
        }
    }
}

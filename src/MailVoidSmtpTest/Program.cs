using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

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

        Console.WriteLine("\nAll tests completed!");
        Console.WriteLine("Check the MailVoid web interface to see if emails were received.");
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
}

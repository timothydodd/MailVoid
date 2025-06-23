using MailKit.Net.Smtp;
using MimeKit;

namespace MailVoidSmtpTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("MailVoid SMTP Test");
        Console.WriteLine("=================");

        // Test configuration
        var smtpHost = "139.144.16.146";
        var smtpPort = 25;
        var fromEmail = "test@example.com";
        var toEmail = "recipient@example.com";

        Console.WriteLine($"Testing SMTP server at {smtpHost}:{smtpPort}");
        Console.WriteLine();

        // Test 1: Simple text email
        Console.WriteLine("Test 1: Sending simple text email...");
        await SendSimpleTextEmail(smtpHost, smtpPort, fromEmail, toEmail);

        // Test 2: HTML email
        Console.WriteLine("\nTest 2: Sending HTML email...");
        await SendHtmlEmail(smtpHost, smtpPort, fromEmail, toEmail);

        // Test 3: Email with special characters in subject
        Console.WriteLine("\nTest 3: Sending email with encoded subject...");
        await SendEmailWithEncodedSubject(smtpHost, smtpPort, fromEmail, toEmail);

        // Test 4: Email with multiple recipients
        Console.WriteLine("\nTest 4: Sending email with multiple recipients...");
        await SendEmailWithMultipleRecipients(smtpHost, smtpPort, fromEmail,
            new[] { toEmail, "cc@example.com", "bcc@example.com" });

        Console.WriteLine("\nAll tests completed!");
        Console.WriteLine("Check the MailVoid web interface to see if emails were received.");
    }

    static async Task SendSimpleTextEmail(string host, int port, string from, string to)
    {
        try
        {
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

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, false);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine("✓ Simple text email sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send simple text email: {ex.Message}");
        }
    }

    static async Task SendHtmlEmail(string host, int port, string from, string to)
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

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, false);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine("✓ HTML email sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send HTML email: {ex.Message}");
        }
    }

    static async Task SendEmailWithEncodedSubject(string host, int port, string from, string to)
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

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, false);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine("✓ Email with encoded subject sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send email with encoded subject: {ex.Message}");
        }
    }

    static async Task SendEmailWithMultipleRecipients(string host, int port, string from, string[] recipients)
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

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, false);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine("✓ Email with multiple recipients sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send email with multiple recipients: {ex.Message}");
        }
    }
}

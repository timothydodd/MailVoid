using System.Buffers;
using Microsoft.Extensions.Logging;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using MailVoidSmtpServer.Models;

namespace MailVoidSmtpServer.Services;

public class MailMessageStore : MessageStore
{
    private readonly ILogger<MailMessageStore> _logger;
    private readonly IInboundEmailQueueService _inboundQueue;

    public MailMessageStore(ILogger<MailMessageStore> logger, IInboundEmailQueueService inboundQueue)
    {
        _logger = logger;
        _inboundQueue = inboundQueue;
    }

    public override async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            // SECURITY: This server is for receiving emails only - never relay or send emails
            _logger.LogDebug("Email relay is disabled - this server only receives emails for testing");

            var isSecure = context.EndpointDefinition.IsSecure;
            var securityInfo = isSecure ? "SSL/TLS" : "Plain Text";
            var endpoint = context.EndpointDefinition.Endpoint;
            var portInfo = endpoint?.Port.ToString() ?? "unknown";

            _logger.LogDebug("Processing new email from session {SessionId}", context.SessionId);
            _logger.LogDebug("Connection details - Remote: {RemoteEndPoint}, Port: {Port}, Security: {Security}",
                endpoint, portInfo, securityInfo);
            // Convert buffer to raw email string
            await using var stream = new MemoryStream();
            foreach (var segment in buffer)
            {
                await stream.WriteAsync(segment.Span.ToArray(), cancellationToken);
            }

            var rawEmail = System.Text.Encoding.UTF8.GetString(stream.ToArray());

            // Parse the message for metadata
            stream.Position = 0;
            var message = await MimeMessage.LoadAsync(stream, cancellationToken);

            _logger.LogInformation("Received message from {From} to {To} with subject: {Subject} [Security: {Security}]",
                message.From.ToString(),
                message.To.ToString(),
                message.Subject,
                securityInfo);

            _logger.LogDebug("Message details - Size: {Size} bytes, Attachments: {AttachmentCount}, MessageId: {MessageId}, BodyParts: {BodyPartCount}",
                stream.Length,
                message.Attachments.Count(),
                message.MessageId,
                message.BodyParts.Count());

            // Log body part types for debugging
            foreach (var part in message.BodyParts.Take(5)) // Limit to first 5 parts
            {
                if (part is TextPart textPart)
                {
                    _logger.LogDebug("Body part found - ContentType: {ContentType}, Encoding: {Encoding}, Size: {Size}",
                        textPart.ContentType.MimeType,
                        textPart.ContentTransferEncoding,
                        textPart.Text?.Length ?? 0);
                }
            }

            // Queue email for background processing
            var htmlBody = GetDecodedHtmlBody(message);
            var textBody = GetDecodedTextBody(message);
            
            var emailData = new EmailWebhookData
            {
                From = message.From.Mailboxes.FirstOrDefault()?.Address ?? "unknown@unknown.com",
                To = message.To.Mailboxes.Select(m => m.Address).ToList(),
                Subject = message.Subject ?? "(no subject)",
                Html = htmlBody,
                Text = !string.IsNullOrEmpty(textBody) ? textBody : null,
                Headers = message.Headers
                    .GroupBy(h => h.Field)
                    .ToDictionary(g => g.Key, g => string.Join("; ", g.Select(h => h.Value))),
                Attachments = ExtractAttachments(message),
                MessageId = message.MessageId,
                Date = message.Date.UtcDateTime,
                RawEmail = rawEmail
            };

            // Queue the email for background processing instead of processing immediately
            var queueId = await _inboundQueue.EnqueueEmailAsync(emailData, priority: 0);

            _logger.LogInformation("✓ Email queued for processing - MessageId: {MessageId}, QueueId: {QueueId}, From: {From}, Security: {Security}",
                message.MessageId, queueId, emailData.From, securityInfo);
            
            return SmtpResponse.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing email message");
            return new SmtpResponse(SmtpReplyCode.TransactionFailed, "Error processing message");
        }
    }

    private List<AttachmentData> ExtractAttachments(MimeMessage message)
    {
        var attachments = new List<AttachmentData>();
        _logger.LogDebug("Extracting {Count} attachments from message", message.Attachments.Count());

        foreach (var attachment in message.Attachments)
        {
            if (attachment is MimePart mimePart)
            {
                using var memory = new MemoryStream();
                mimePart.Content.DecodeTo(memory);

                var attachmentData = new AttachmentData
                {
                    Filename = mimePart.FileName ?? "attachment",
                    ContentType = mimePart.ContentType.MimeType,
                    Content = Convert.ToBase64String(memory.ToArray())
                };

                _logger.LogDebug("Extracted attachment: {Filename} ({ContentType}, {Size} bytes)",
                    attachmentData.Filename, attachmentData.ContentType, memory.Length);

                attachments.Add(attachmentData);
            }
        }

        return attachments;
    }

    private string? GetDecodedHtmlBody(MimeMessage message)
    {
        try
        {
            // First try the built-in HtmlBody property - this handles decoding properly
            var htmlBody = message.HtmlBody;
            if (!string.IsNullOrEmpty(htmlBody))
            {
                _logger.LogDebug("Using message.HtmlBody property");
                return htmlBody;
            }

            // Check if the body is directly an HTML part
            var body = message.Body;
            if (body is TextPart htmlBodyPart && htmlBodyPart.IsHtml)
            {
                _logger.LogDebug("Found HTML TextPart in message.Body");
                return htmlBodyPart.Text;
            }

            // For multipart messages, find the HTML part
            if (body is Multipart multipart)
            {
                _logger.LogDebug("Processing multipart message for HTML");
                var htmlText = GetTextFromMultipart(multipart, "text/html");
                if (!string.IsNullOrEmpty(htmlText))
                    return htmlText;
            }

            // Last resort - scan all body parts
            var htmlPart = message.BodyParts.OfType<TextPart>()
                .FirstOrDefault(part => part.ContentType.IsMimeType("text", "html"));

            if (htmlPart != null)
            {
                _logger.LogDebug("HTML part encoding: {Encoding}, Charset: {Charset}",
                    htmlPart.ContentTransferEncoding, htmlPart.ContentType.Charset);

                // The Text property should automatically decode quoted-printable and base64
                return htmlPart.Text;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting HTML body");
            return null;
        }
    }

    private string GetDecodedTextBody(MimeMessage message)
    {
        try
        {
            // First try the built-in TextBody property - this handles multipart properly
            var textBody = message.TextBody;
            if (!string.IsNullOrEmpty(textBody))
            {
                _logger.LogDebug("Using message.TextBody property");
                return textBody;
            }

            // If body is null, try to get text from the Body structure
            var body = message.Body;
            if (body is TextPart textBodyPart && textBodyPart.IsPlain)
            {
                _logger.LogDebug("Found TextPart in message.Body");
                return textBodyPart.Text ?? "";
            }

            // For multipart messages, find the best text representation
            if (body is Multipart multipart)
            {
                _logger.LogDebug("Processing multipart message with {Count} parts", multipart.Count);
                var plainText = GetTextFromMultipart(multipart, "text/plain");
                if (!string.IsNullOrEmpty(plainText))
                    return plainText;
            }

            // Last resort - scan all body parts
            var textPart = message.BodyParts.OfType<TextPart>()
                .FirstOrDefault(part => part.ContentType.IsMimeType("text", "plain"));

            if (textPart?.Text != null)
            {
                _logger.LogDebug("Found text part in BodyParts scan");
                return textPart.Text;
            }

            _logger.LogDebug("No plain text found, falling back to HTML");
            // Fallback to HTML body if no text body
            return GetDecodedHtmlBody(message) ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text body");
            return "";
        }
    }

    private string? GetTextFromMultipart(Multipart multipart, string mimeType)
    {
        foreach (var part in multipart)
        {
            if (part is TextPart textPart)
            {
                var parts = mimeType.Split('/');
                if (parts.Length == 2 && textPart.ContentType.IsMimeType(parts[0], parts[1]))
                {
                    return textPart.Text;
                }
            }
            else if (part is Multipart nestedMultipart)
            {
                var result = GetTextFromMultipart(nestedMultipart, mimeType);
                if (!string.IsNullOrEmpty(result))
                    return result;
            }
        }
        return null;
    }
}


using System.Buffers;
using Microsoft.Extensions.Logging;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;

namespace MailVoidSmtpServer.Services;

public class MailMessageStore : MessageStore
{
    private readonly ILogger<MailMessageStore> _logger;
    private readonly MailForwardingService _forwardingService;

    public MailMessageStore(ILogger<MailMessageStore> logger, MailForwardingService forwardingService)
    {
        _logger = logger;
        _forwardingService = forwardingService;
    }

    public override async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
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

            _logger.LogDebug("Message details - Size: {Size} bytes, Attachments: {AttachmentCount}, MessageId: {MessageId}",
                stream.Length,
                message.Attachments.Count(),
                message.MessageId);

            // Forward to MailVoid API with both raw and parsed data
            var emailData = new EmailWebhookData
            {
                From = message.From.Mailboxes.FirstOrDefault()?.Address ?? "unknown@unknown.com",
                To = message.To.Mailboxes.Select(m => m.Address).ToList(),
                Subject = message.Subject ?? "(no subject)",
                Html = message.HtmlBody,
                Text = message.TextBody ?? message.HtmlBody ?? "",
                Headers = message.Headers
                    .GroupBy(h => h.Field)
                    .ToDictionary(g => g.Key, g => string.Join("; ", g.Select(h => h.Value))),
                Attachments = ExtractAttachments(message),
                MessageId = message.MessageId,
                Date = message.Date.UtcDateTime,
                RawEmail = rawEmail
            };

            var success = await _forwardingService.ForwardEmailAsync(emailData, cancellationToken);

            if (success)
            {
                _logger.LogInformation("✓ Email processed successfully - MessageId: {MessageId}, From: {From}, Security: {Security}",
                    message.MessageId, emailData.From, securityInfo);
                return SmtpResponse.Ok;
            }
            else
            {
                _logger.LogError("✗ Failed to forward email to MailVoid API - MessageId: {MessageId}, From: {From}, Security: {Security}",
                    message.MessageId, emailData.From, securityInfo);
                return new SmtpResponse(SmtpReplyCode.MailboxUnavailable, "Failed to process message");
            }
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
}

public class EmailWebhookData
{
    public string From { get; set; } = "";
    public List<string> To { get; set; } = new();
    public string Subject { get; set; } = "";
    public string? Html { get; set; }
    public string Text { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
    public List<AttachmentData> Attachments { get; set; } = new();
    public string? MessageId { get; set; }
    public DateTime Date { get; set; }
    public string RawEmail { get; set; } = "";
}

public class AttachmentData
{
    public string Filename { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Content { get; set; } = ""; // Base64 encoded
}

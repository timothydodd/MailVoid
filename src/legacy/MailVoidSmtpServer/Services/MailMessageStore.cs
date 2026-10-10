using System.Buffers;
using System.Text;
using Microsoft.Extensions.Logging;
using MimeKit;
using SmtpServer;
using SmtpServer.Mail;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using MailVoidSmtpServer.Models;

namespace MailVoidSmtpServer.Services;

public class MailMessageStore : MessageStore
{
    private readonly ILogger<MailMessageStore> _logger;
    private readonly IEmailQueueService _queue;

    public MailMessageStore(ILogger<MailMessageStore> logger, IEmailQueueService queue)
    {
        _logger = logger;
        _queue = queue;
    }

    public override async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            // SECURITY: This server only receives emails for testing - it never relays or sends.
            var isSecure = context.EndpointDefinition.IsSecure;
            var securityInfo = isSecure ? "SSL/TLS" : "Plain Text";

            // Keep the raw bytes intact. Latin-1 (iso-8859-1) is a lossless byte<->char mapping,
            // so binary / non-UTF-8 content survives the round-trip into the raw-source view.
            var rawBytes = buffer.ToArray();
            var rawEmail = Encoding.Latin1.GetString(rawBytes);

            // The envelope recipients (RCPT TO) are authoritative - BCC recipients never appear in
            // the message headers, so we must forward every one of them.
            var envelopeRecipients = transaction.To
                .Select(m => m.AsAddress())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            EmailWebhookData emailData;

            try
            {
                using var stream = new MemoryStream(rawBytes, writable: false);
                var message = await MimeMessage.LoadAsync(stream, cancellationToken);

                _logger.LogInformation("Received message from {From} to {To} with subject: {Subject} [Security: {Security}]",
                    message.From.ToString(), string.Join(", ", envelopeRecipients), message.Subject, securityInfo);

                emailData = new EmailWebhookData
                {
                    From = message.From.Mailboxes.FirstOrDefault()?.Address
                           ?? transaction.From?.AsAddress()
                           ?? "unknown@unknown.com",
                    To = envelopeRecipients,
                    Subject = message.Subject ?? "(no subject)",
                    Html = MimeExtractor.GetDecodedHtmlBody(message),
                    Text = NullIfEmpty(MimeExtractor.GetDecodedTextBody(message)),
                    Headers = FlattenHeaders(message.Headers),
                    Attachments = MimeExtractor.ExtractAttachments(message),
                    MessageId = message.MessageId,
                    Date = message.Date.UtcDateTime,
                    RawEmail = rawEmail
                };
            }
            catch (Exception parseEx)
            {
                // A developer tool must still capture broken emails - forward the raw bytes with a marker.
                _logger.LogWarning(parseEx, "Failed to parse MIME message; forwarding raw content with parse-failed marker");

                emailData = new EmailWebhookData
                {
                    From = transaction.From?.AsAddress() ?? "unknown@unknown.com",
                    To = envelopeRecipients,
                    Subject = "[parse failed]",
                    Text = "This message could not be parsed as MIME. See the raw source.",
                    RawEmail = rawEmail,
                    ParseFailed = true
                };
            }

            if (emailData.To.Count == 0)
            {
                _logger.LogWarning("No envelope recipients for message from {From}; rejecting", emailData.From);
                return new SmtpResponse(SmtpReplyCode.MailboxUnavailable, "No valid recipients");
            }

            // EnqueueAsync only returns once the message is durable on disk, so the 250 below is safe.
            var queueId = await _queue.EnqueueAsync(emailData, cancellationToken);

            _logger.LogInformation("✓ Email queued - MessageId: {MessageId}, QueueId: {QueueId}, From: {From}, Recipients: {Count}, Security: {Security}",
                emailData.MessageId, queueId, emailData.From, emailData.To.Count, securityInfo);

            return SmtpResponse.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing email message");
            return new SmtpResponse(SmtpReplyCode.TransactionFailed, "Error processing message");
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    // Preserve every header value. Multiple same-name headers (e.g. Received) are joined with a
    // newline rather than "; " so each header stays intact and machine-parseable downstream.
    private static Dictionary<string, string> FlattenHeaders(HeaderList headers)
    {
        return headers
            .GroupBy(h => h.Field)
            .ToDictionary(g => g.Key, g => string.Join("\n", g.Select(h => h.Value)));
    }
}

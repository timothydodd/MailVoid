using MimeKit;
using MailVoidSmtpServer.Models;

namespace MailVoidSmtpServer.Services;

/// <summary>
/// Pure MIME-to-webhook extraction helpers. Kept free of logging/DI so the logic is unit-testable.
/// </summary>
public static class MimeExtractor
{
    public static List<AttachmentData> ExtractAttachments(MimeMessage message)
    {
        var attachments = new List<AttachmentData>();

        foreach (var attachment in message.Attachments)
        {
            if (attachment is MimePart mimePart)
            {
                using var memory = new MemoryStream();
                mimePart.Content.DecodeTo(memory);

                attachments.Add(new AttachmentData
                {
                    Filename = mimePart.FileName ?? "attachment",
                    ContentType = mimePart.ContentType.MimeType,
                    Content = Convert.ToBase64String(memory.ToArray())
                });
            }
        }

        return attachments;
    }

    public static string? GetDecodedHtmlBody(MimeMessage message)
    {
        try
        {
            var htmlBody = message.HtmlBody;
            if (!string.IsNullOrEmpty(htmlBody))
            {
                return htmlBody;
            }

            var body = message.Body;
            if (body is TextPart htmlBodyPart && htmlBodyPart.IsHtml)
            {
                return htmlBodyPart.Text;
            }

            if (body is Multipart multipart)
            {
                var htmlText = GetTextFromMultipart(multipart, "text/html");
                if (!string.IsNullOrEmpty(htmlText))
                    return htmlText;
            }

            var htmlPart = message.BodyParts.OfType<TextPart>()
                .FirstOrDefault(part => part.ContentType.IsMimeType("text", "html"));

            return htmlPart?.Text;
        }
        catch
        {
            return null;
        }
    }

    public static string GetDecodedTextBody(MimeMessage message)
    {
        try
        {
            var textBody = message.TextBody;
            if (!string.IsNullOrEmpty(textBody))
            {
                return textBody;
            }

            var body = message.Body;
            if (body is TextPart textBodyPart && textBodyPart.IsPlain)
            {
                return textBodyPart.Text ?? "";
            }

            if (body is Multipart multipart)
            {
                var plainText = GetTextFromMultipart(multipart, "text/plain");
                if (!string.IsNullOrEmpty(plainText))
                    return plainText;
            }

            var textPart = message.BodyParts.OfType<TextPart>()
                .FirstOrDefault(part => part.ContentType.IsMimeType("text", "plain"));

            if (textPart?.Text != null)
            {
                return textPart.Text;
            }

            return GetDecodedHtmlBody(message) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string? GetTextFromMultipart(Multipart multipart, string mimeType)
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

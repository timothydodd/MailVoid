using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Web;
using MailVoidWeb;

namespace MailVoidApi.Services;

public interface IMailDataExtractionService
{
    Task<Mail> ExtractMailFromDataAsync(MailData mailData);
}

public class MailDataExtractionService : IMailDataExtractionService
{
    private readonly ILogger<MailDataExtractionService> _logger;

    public MailDataExtractionService(ILogger<MailDataExtractionService> logger)
    {
        _logger = logger;
    }

    public async Task<Mail> ExtractMailFromDataAsync(MailData mailData)
    {
        try
        {
            var parsedData = ParseRawEmailData(mailData.Raw);
            
            var mail = new Mail
            {
                From = ExtractEmailAddress(mailData.From),
                To = ExtractEmailAddress(mailData.To),
                Subject = parsedData.Subject ?? ExtractSubjectFromHeaders(mailData.Headers) ?? "No Subject",
                Text = parsedData.Body,
                IsHtml = parsedData.IsHtml,
                FromName = ExtractDisplayName(mailData.From),
                ToOthers = ExtractAdditionalRecipientsFromHeaders(mailData.Headers, mailData.To),
                Charsets = ExtractCharsetFromHeaders(mailData.Headers),
                CreatedOn = parsedData.Date ?? DateTime.UtcNow
            };

            return await Task.FromResult(mail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse raw email data, falling back to basic extraction");
            return await FallbackExtractionAsync(mailData);
        }
    }

    private async Task<Mail> FallbackExtractionAsync(MailData mailData)
    {
        var subject = ExtractSubjectFromHeaders(mailData.Headers) ?? "No Subject";
        var contentType = ExtractContentTypeFromHeaders(mailData.Headers);
        var isHtml = contentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;

        return new Mail
        {
            From = ExtractEmailAddress(mailData.From),
            To = ExtractEmailAddress(mailData.To),
            Subject = subject,
            Text = mailData.Raw,
            IsHtml = isHtml,
            FromName = ExtractDisplayName(mailData.From),
            ToOthers = null,
            Charsets = ExtractCharsetFromContentType(contentType),
            CreatedOn = DateTime.UtcNow
        };
    }

    private string ExtractEmailAddress(string emailField)
    {
        if (string.IsNullOrEmpty(emailField))
            return string.Empty;

        var emailMatch = Regex.Match(emailField, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        return emailMatch.Success ? emailMatch.Value : emailField;
    }

    private string? ExtractDisplayName(string emailField)
    {
        if (string.IsNullOrEmpty(emailField))
            return null;

        var displayNameMatch = Regex.Match(emailField, @"^([^<>]+)\s*<");
        return displayNameMatch.Success ? displayNameMatch.Groups[1].Value.Trim().Trim('"') : null;
    }

    private (string Body, bool IsHtml, string? Subject, DateTime? Date) ParseRawEmailData(string rawData)
    {
        var lines = rawData.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var headerEndIndex = Array.FindIndex(lines, string.IsNullOrEmpty);
        
        if (headerEndIndex == -1)
        {
            // No empty line found, assume entire content is body
            return (rawData, DetectHtmlContent(rawData), null, null);
        }

        var headerLines = lines.Take(headerEndIndex).ToArray();
        var bodyLines = lines.Skip(headerEndIndex + 1).ToArray();
        var body = string.Join("\n", bodyLines);

        var subject = DecodeMimeHeader(ExtractHeaderValue(headerLines, "Subject"));
        var dateStr = ExtractHeaderValue(headerLines, "Date");
        var date = ParseDate(dateStr);
        var isHtml = DetectHtmlContent(body) || 
                    ExtractHeaderValue(headerLines, "Content-Type")?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;

        return (body, isHtml, subject, date);
    }

    private string? ExtractHeaderValue(string[] headerLines, string headerName)
    {
        for (int i = 0; i < headerLines.Length; i++)
        {
            var line = headerLines[i];
            if (line.StartsWith(headerName + ":", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Substring(headerName.Length + 1).Trim();
                
                // Handle header continuation (lines starting with whitespace)
                for (int j = i + 1; j < headerLines.Length; j++)
                {
                    if (headerLines[j].StartsWith(" ") || headerLines[j].StartsWith("\t"))
                    {
                        value += " " + headerLines[j].Trim();
                    }
                    else
                    {
                        break;
                    }
                }
                
                return value;
            }
        }
        return null;
    }

    private DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
            return null;

        // Try common date formats
        var formats = new[]
        {
            "ddd, d MMM yyyy HH:mm:ss zzz",
            "d MMM yyyy HH:mm:ss zzz",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss zzz"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format, null, System.Globalization.DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        if (DateTime.TryParse(dateStr, out var parsedDate))
        {
            return parsedDate;
        }

        return null;
    }

    private bool DetectHtmlContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        return content.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("<body", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("<div", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("<p>", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("<br>", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("<span", StringComparison.OrdinalIgnoreCase);
    }

    private string? ExtractAdditionalRecipientsFromHeaders(Dictionary<string, string> headers, string primaryTo)
    {
        var allRecipients = new List<string>();
        var primaryEmail = ExtractEmailAddress(primaryTo);

        // Check To header
        if (headers.TryGetValue("To", out var toHeader))
        {
            var toEmails = ExtractEmailsFromHeader(toHeader);
            allRecipients.AddRange(toEmails.Where(email => 
                !email.Equals(primaryEmail, StringComparison.OrdinalIgnoreCase)));
        }

        // Check CC header
        if (headers.TryGetValue("Cc", out var ccHeader))
        {
            var ccEmails = ExtractEmailsFromHeader(ccHeader);
            allRecipients.AddRange(ccEmails);
        }

        return allRecipients.Any() ? string.Join(", ", allRecipients.Distinct()) : null;
    }

    private List<string> ExtractEmailsFromHeader(string headerValue)
    {
        var emails = new List<string>();
        var emailMatches = Regex.Matches(headerValue, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        
        foreach (Match match in emailMatches)
        {
            emails.Add(match.Value);
        }

        return emails;
    }

    private string? ExtractCharsetFromHeaders(Dictionary<string, string> headers)
    {
        var contentType = ExtractContentTypeFromHeaders(headers);
        return ExtractCharsetFromContentType(contentType);
    }

    private string? ExtractSubjectFromHeaders(Dictionary<string, string> headers)
    {
        var subject = headers.TryGetValue("Subject", out var subjectValue) ? subjectValue : 
                     headers.TryGetValue("subject", out subjectValue) ? subjectValue : null;
        
        return subject != null ? DecodeMimeHeader(subject) : null;
    }

    private string? ExtractContentTypeFromHeaders(Dictionary<string, string> headers)
    {
        return headers.TryGetValue("Content-Type", out var contentType) ? contentType :
               headers.TryGetValue("content-type", out contentType) ? contentType : null;
    }

    private string? ExtractCharsetFromContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return null;

        var charsetMatch = Regex.Match(contentType, @"charset=([^;]+)", RegexOptions.IgnoreCase);
        return charsetMatch.Success ? charsetMatch.Groups[1].Value.Trim() : null;
    }

    private string? DecodeMimeHeader(string? encodedHeader)
    {
        if (string.IsNullOrEmpty(encodedHeader))
            return encodedHeader;

        // Pattern for MIME encoded-word: =?charset?encoding?encoded-text?=
        var mimePattern = @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=";
        var matches = Regex.Matches(encodedHeader, mimePattern);

        if (!matches.Any())
            return encodedHeader; // Not MIME encoded

        var result = encodedHeader;
        foreach (Match match in matches)
        {
            var charset = match.Groups[1].Value;
            var encoding = match.Groups[2].Value.ToUpper();
            var encodedText = match.Groups[3].Value;

            string decodedText;
            try
            {
                if (encoding == "B")
                {
                    // Base64 decoding
                    var bytes = Convert.FromBase64String(encodedText);
                    var textEncoding = GetEncoding(charset);
                    decodedText = textEncoding.GetString(bytes);
                }
                else if (encoding == "Q")
                {
                    // Quoted-printable decoding
                    decodedText = DecodeQuotedPrintable(encodedText, charset);
                }
                else
                {
                    decodedText = encodedText; // Unknown encoding, keep as-is
                }

                result = result.Replace(match.Value, decodedText);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decode MIME header part: {EncodedPart}", match.Value);
                // Keep the original encoded part if decoding fails
            }
        }

        return result;
    }

    private Encoding GetEncoding(string charset)
    {
        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch
        {
            // Fallback to UTF-8 if charset is not recognized
            return Encoding.UTF8;
        }
    }

    private string DecodeQuotedPrintable(string encodedText, string charset)
    {
        // Replace underscore with space (RFC 2047 specific for encoded-words)
        var text = encodedText.Replace('_', ' ');
        
        // Decode =XX sequences
        var result = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '=' && i + 2 < text.Length)
            {
                var hexString = text.Substring(i + 1, 2);
                if (int.TryParse(hexString, System.Globalization.NumberStyles.HexNumber, null, out int value))
                {
                    result.Append((char)value);
                    i += 2; // Skip the next two characters
                }
                else
                {
                    result.Append(text[i]);
                }
            }
            else
            {
                result.Append(text[i]);
            }
        }

        // Convert to proper encoding
        var bytes = Encoding.GetEncoding("iso-8859-1").GetBytes(result.ToString());
        var targetEncoding = GetEncoding(charset);
        return targetEncoding.GetString(bytes);
    }
}
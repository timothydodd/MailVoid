using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public class MailForwardingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MailForwardingService> _logger;
    private readonly MailVoidApiOptions _options;

    public MailForwardingService(
        HttpClient httpClient, 
        ILogger<MailForwardingService> logger,
        IOptions<MailVoidApiOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<bool> ForwardEmailAsync(EmailWebhookData emailData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Starting email forwarding for message from {From} to {To}", 
                emailData.From, string.Join(", ", emailData.To));
            var url = $"{_options.BaseUrl.TrimEnd('/')}{_options.WebhookEndpoint}";
            
            // Convert to MailData format that the webhook expects
            var mailData = ConvertToMailData(emailData);
            var json = JsonSerializer.Serialize(mailData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            // Add API key header if configured
            if (!string.IsNullOrEmpty(_options.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("ApiKey", _options.ApiKey);
            }
            
            _logger.LogDebug("Forwarding email to {Url} with payload size: {Size} bytes", url, json.Length);
            _logger.LogTrace("Email JSON payload: {Payload}", json);
            
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully forwarded email to MailVoid API. Status: {StatusCode}", response.StatusCode);
                return true;
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to forward email. Status: {StatusCode}, Response: {Response}", 
                    response.StatusCode, responseBody);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while forwarding email to MailVoid API");
            return false;
        }
    }

    private MailData ConvertToMailData(EmailWebhookData emailData)
    {
        // Use the original raw email if available, otherwise reconstruct it
        var rawEmail = !string.IsNullOrEmpty(emailData.RawEmail) 
            ? emailData.RawEmail 
            : BuildRawEmail(emailData);
        
        var mailData = new MailData
        {
            From = emailData.From,
            To = emailData.To.FirstOrDefault() ?? "",
            Headers = emailData.Headers,
            Raw = rawEmail,
            RawSize = Encoding.UTF8.GetByteCount(rawEmail)
        };
        
        _logger.LogDebug("Converted email data - From: {From}, To: {To}, RawSize: {RawSize} bytes", 
            mailData.From, mailData.To, mailData.RawSize);
            
        return mailData;
    }

    private string BuildRawEmail(EmailWebhookData emailData)
    {
        var sb = new StringBuilder();
        
        // Add headers
        foreach (var header in emailData.Headers)
        {
            sb.AppendLine($"{header.Key}: {header.Value}");
        }
        
        // Add Message-ID if not present
        if (!emailData.Headers.ContainsKey("Message-ID") && !string.IsNullOrEmpty(emailData.MessageId))
        {
            sb.AppendLine($"Message-ID: {emailData.MessageId}");
        }
        
        // Add Date if not present
        if (!emailData.Headers.ContainsKey("Date"))
        {
            sb.AppendLine($"Date: {emailData.Date:R}");
        }
        
        // Add From if not in headers
        if (!emailData.Headers.ContainsKey("From"))
        {
            sb.AppendLine($"From: {emailData.From}");
        }
        
        // Add To if not in headers
        if (!emailData.Headers.ContainsKey("To"))
        {
            sb.AppendLine($"To: {string.Join(", ", emailData.To)}");
        }
        
        // Add Subject if not in headers
        if (!emailData.Headers.ContainsKey("Subject"))
        {
            sb.AppendLine($"Subject: {emailData.Subject}");
        }
        
        // Add Content-Type if we have both HTML and text
        if (!string.IsNullOrEmpty(emailData.Html) && !string.IsNullOrEmpty(emailData.Text))
        {
            sb.AppendLine("Content-Type: multipart/alternative; boundary=\"boundary123\"");
            sb.AppendLine();
            sb.AppendLine("--boundary123");
            sb.AppendLine("Content-Type: text/plain; charset=utf-8");
            sb.AppendLine();
            sb.AppendLine(emailData.Text);
            sb.AppendLine();
            sb.AppendLine("--boundary123");
            sb.AppendLine("Content-Type: text/html; charset=utf-8");
            sb.AppendLine();
            sb.AppendLine(emailData.Html);
            sb.AppendLine();
            sb.AppendLine("--boundary123--");
        }
        else if (!string.IsNullOrEmpty(emailData.Html))
        {
            sb.AppendLine("Content-Type: text/html; charset=utf-8");
            sb.AppendLine();
            sb.AppendLine(emailData.Html);
        }
        else
        {
            sb.AppendLine("Content-Type: text/plain; charset=utf-8");
            sb.AppendLine();
            sb.AppendLine(emailData.Text);
        }
        
        return sb.ToString();
    }
}

// MailData class for API compatibility
public class MailData
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public Dictionary<string, string> Headers { get; init; } = new();
    public string Raw { get; init; } = string.Empty;
    public int RawSize { get; init; }
}
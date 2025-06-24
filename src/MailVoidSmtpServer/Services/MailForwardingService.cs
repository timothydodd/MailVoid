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
        var mailData = new MailData
        {
            From = emailData.From,
            To = emailData.To.FirstOrDefault() ?? "",
            Headers = emailData.Headers,
            Html = emailData.Html,  // Decoded HTML from MimeKit
            Text = emailData.Text   // Decoded plain text from MimeKit
        };

        _logger.LogDebug("Converted email data - From: {From}, To: {To}, Has HTML: {HasHtml}, Has Text: {HasText}",
            mailData.From, mailData.To, !string.IsNullOrEmpty(mailData.Html), !string.IsNullOrEmpty(mailData.Text));

        return mailData;
    }


    // MailData class for API compatibility
    public class MailData
    {
        public string From { get; init; } = string.Empty;
        public string To { get; init; } = string.Empty;
        public Dictionary<string, string> Headers { get; init; } = new();
        public string? Html { get; init; }
        public string? Text { get; init; }
    }
}

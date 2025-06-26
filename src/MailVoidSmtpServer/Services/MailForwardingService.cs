using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailVoidSmtpServer.Models;

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
        var url = $"{_options.BaseUrl.TrimEnd('/')}{_options.WebhookEndpoint}";
        
        try
        {
            _logger.LogInformation("🚀 Forwarding email from {From} to {To} via {Url}",
                emailData.From, string.Join(", ", emailData.To), url);

            // Convert to MailData format that the webhook expects
            var mailData = ConvertToMailData(emailData);
            var json = JsonSerializer.Serialize(mailData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Add API key header if configured
            if (!string.IsNullOrEmpty(_options.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("ApiKey", _options.ApiKey);
                _logger.LogDebug("Using API key: {ApiKey}", _options.ApiKey);
            }
            else
            {
                _logger.LogWarning("⚠️ No API key configured for MailVoid API forwarding");
            }

            _logger.LogDebug("Payload size: {Size} bytes", json.Length);

            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("✅ Successfully forwarded email to MailVoid API - Status: {StatusCode}, From: {From}, Subject: {Subject}",
                    response.StatusCode, emailData.From, emailData.Subject ?? "(no subject)");
                return true;
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("❌ Failed to forward email to MailVoid API - Status: {StatusCode}, From: {From}, Subject: {Subject}, URL: {Url}, Response: {Response}",
                    response.StatusCode, emailData.From, emailData.Subject ?? "(no subject)", url, responseBody);
                return false;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "🔌 Network error forwarding email to MailVoid API - URL: {Url}, From: {From}, Error: {Error}",
                url, emailData.From, ex.Message);
            return false;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "⏰ Timeout forwarding email to MailVoid API - URL: {Url}, From: {From}",
                url, emailData.From);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Unexpected error forwarding email to MailVoid API - URL: {Url}, From: {From}, Error: {Error}",
                url, emailData.From, ex.Message);
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

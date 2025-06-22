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
            var url = $"{_options.BaseUrl.TrimEnd('/')}{_options.WebhookEndpoint}";
            
            // Convert to SendGrid-like format that the webhook expects
            var webhookPayload = new List<object>
            {
                new
                {
                    email = emailData.To.FirstOrDefault() ?? "",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    @event = "processed",
                    sg_event_id = Guid.NewGuid().ToString(),
                    sg_message_id = emailData.MessageId ?? Guid.NewGuid().ToString(),
                    from = emailData.From,
                    to = emailData.To,
                    subject = emailData.Subject,
                    html = emailData.Html ?? emailData.Text,
                    text = emailData.Text,
                    headers = JsonSerializer.Serialize(emailData.Headers),
                    attachments = emailData.Attachments.Count,
                    attachment_info = JsonSerializer.Serialize(emailData.Attachments.Select(a => new 
                    { 
                        filename = a.Filename, 
                        type = a.ContentType,
                        content_id = Guid.NewGuid().ToString()
                    }))
                }
            };

            var json = JsonSerializer.Serialize(webhookPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            // Add API key header if configured
            if (!string.IsNullOrEmpty(_options.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("ApiKey", _options.ApiKey);
            }
            
            _logger.LogDebug("Forwarding email to {Url}", url);
            
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
}
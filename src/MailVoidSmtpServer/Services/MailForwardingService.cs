using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailVoidSmtpServer.Models;

namespace MailVoidSmtpServer.Services;

public class MailForwardingService
{
    private const int MaxErrorBodyChars = 2048;
    private const int MaxTransientRetries = 2;

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
        var enabledTargets = _options.Targets.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.BaseUrl)).ToList();
        if (enabledTargets.Count == 0)
        {
            _logger.LogError("❌ No enabled MailVoidApi targets configured — dropping forward for {From} -> {To}",
                emailData.From, string.Join(", ", emailData.To));
            return false;
        }

        var recipients = emailData.To.Count > 0 ? emailData.To : new List<string> { "" };

        var allSucceeded = true;
        // The API keys the inbox off a single recipient, so fan out one webhook per envelope
        // recipient. BCC and multi-recipient mail therefore lands in every correct inbox.
        foreach (var recipient in recipients)
        {
            var payload = JsonSerializer.Serialize(ConvertToMailData(emailData, recipient));
            foreach (var target in enabledTargets)
            {
                var ok = await ForwardToTargetAsync(target, emailData, recipient, payload, cancellationToken);
                if (!ok)
                {
                    allSucceeded = false;
                }
            }
        }

        // Returning false retries the whole message against all targets/recipients, which can
        // duplicate already-succeeded deliveries — accepted for now (a dev tool tolerates dupes).
        return allSucceeded;
    }

    private async Task<bool> ForwardToTargetAsync(
        MailVoidApiTarget target,
        EmailWebhookData emailData,
        string recipient,
        string payload,
        CancellationToken cancellationToken)
    {
        var url = $"{target.BaseUrl.TrimEnd('/')}{target.WebhookEndpoint}";

        for (var attempt = 1; attempt <= MaxTransientRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("🚀 [{Target}] Forwarding email from {From} to {To} via {Url} (attempt {Attempt})",
                    target.Name, emailData.From, recipient, url, attempt);

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };

                if (!string.IsNullOrEmpty(target.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", target.ApiKey);
                }
                else
                {
                    _logger.LogWarning("⚠️ [{Target}] No API key configured", target.Name);
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ [{Target}] Forwarded - Status: {StatusCode}, From: {From}, To: {To}",
                        target.Name, response.StatusCode, emailData.From, recipient);
                    return true;
                }

                var responseBody = await ReadCappedAsync(response, cancellationToken);
                _logger.LogError("❌ [{Target}] Failed - Status: {StatusCode}, URL: {Url}, Response: {Response}",
                    target.Name, response.StatusCode, url, responseBody);

                // Retry only server-side (5xx) failures; client errors won't succeed on retry.
                if ((int)response.StatusCode < 500 || attempt == MaxTransientRetries)
                {
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "🔌 [{Target}] Network error - URL: {Url} (attempt {Attempt})", target.Name, url, attempt);
                if (attempt == MaxTransientRetries)
                {
                    return false;
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "⏰ [{Target}] Timeout - URL: {Url} (attempt {Attempt})", target.Name, url, attempt);
                if (attempt == MaxTransientRetries)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 [{Target}] Unexpected error - URL: {Url}", target.Name, url);
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
        }

        return false;
    }

    private static async Task<string> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length <= MaxErrorBodyChars ? body : body[..MaxErrorBodyChars] + "…(truncated)";
        }
        catch
        {
            return "(unreadable response body)";
        }
    }

    private MailData ConvertToMailData(EmailWebhookData emailData, string recipient)
    {
        return new MailData
        {
            From = emailData.From,
            To = recipient,
            Headers = emailData.Headers,
            Html = emailData.Html,
            Text = emailData.Text,
            RawEmail = emailData.RawEmail,
            MessageId = emailData.MessageId,
            Attachments = emailData.Attachments?.Select(a => new MailAttachmentDto
            {
                Filename = a.Filename,
                ContentType = a.ContentType,
                Content = a.Content
            }).ToList()
        };
    }

    // MailData class for API compatibility
    public class MailData
    {
        public string From { get; init; } = string.Empty;
        public string To { get; init; } = string.Empty;
        public Dictionary<string, string> Headers { get; init; } = new();
        public string? Html { get; init; }
        public string? Text { get; init; }
        public List<MailAttachmentDto>? Attachments { get; init; }
        public string? RawEmail { get; init; }
        public string? MessageId { get; init; }
    }

    public class MailAttachmentDto
    {
        public string Filename { get; init; } = "";
        public string ContentType { get; init; } = "";
        public string Content { get; init; } = "";
    }
}

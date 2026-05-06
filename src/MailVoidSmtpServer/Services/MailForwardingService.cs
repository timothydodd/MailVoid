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
        var enabledTargets = _options.Targets.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.BaseUrl)).ToList();
        if (enabledTargets.Count == 0)
        {
            _logger.LogError("❌ No enabled MailVoidApi targets configured — dropping forward for {From} -> {To}",
                emailData.From, string.Join(", ", emailData.To));
            return false;
        }

        var mailData = ConvertToMailData(emailData);
        var json = JsonSerializer.Serialize(mailData);

        var allSucceeded = true;
        foreach (var target in enabledTargets)
        {
            var ok = await ForwardToTargetAsync(target, emailData, json, cancellationToken);
            if (!ok)
            {
                allSucceeded = false;
            }
        }

        // Returning false triggers a retry of the whole message against all targets.
        // That can cause duplicates on already-succeeded targets — accept that for now;
        // the alternative is per-target persistence in the queue.
        return allSucceeded;
    }

    private async Task<bool> ForwardToTargetAsync(
        MailVoidApiTarget target,
        EmailWebhookData emailData,
        string payload,
        CancellationToken cancellationToken)
    {
        var url = $"{target.BaseUrl.TrimEnd('/')}{target.WebhookEndpoint}";

        try
        {
            _logger.LogInformation("🚀 [{Target}] Forwarding email from {From} to {To} via {Url}",
                target.Name, emailData.From, string.Join(", ", emailData.To), url);

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
                _logger.LogInformation("✅ [{Target}] Forwarded - Status: {StatusCode}, From: {From}, Subject: {Subject}",
                    target.Name, response.StatusCode, emailData.From, emailData.Subject ?? "(no subject)");
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("❌ [{Target}] Failed - Status: {StatusCode}, URL: {Url}, Response: {Response}",
                target.Name, response.StatusCode, url, responseBody);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "🔌 [{Target}] Network error - URL: {Url}, Error: {Error}",
                target.Name, url, ex.Message);
            return false;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "⏰ [{Target}] Timeout - URL: {Url}", target.Name, url);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [{Target}] Unexpected error - URL: {Url}, Error: {Error}",
                target.Name, url, ex.Message);
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
            Html = emailData.Html,
            Text = emailData.Text
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

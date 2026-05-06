using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public class DiagnosticsService : BackgroundService
{
    private readonly ILogger<DiagnosticsService> _logger;
    private readonly HttpClient _httpClient;
    private readonly MailVoidApiOptions _apiOptions;

    public DiagnosticsService(
        ILogger<DiagnosticsService> logger,
        HttpClient httpClient,
        IOptions<MailVoidApiOptions> apiOptions)
    {
        _logger = logger;
        _httpClient = httpClient;
        _apiOptions = apiOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit for services to start up
        await Task.Delay(5000, stoppingToken);

        await RunDiagnostics(stoppingToken);

        // Run diagnostics every 25 minutes
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(25), stoppingToken);
            await RunDiagnostics(stoppingToken);
        }
    }

    private async Task RunDiagnostics(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔧 Running SMTP Server Diagnostics...");

        if (_apiOptions.Targets.Count == 0)
        {
            _logger.LogError("⚠️ No MailVoidApi targets configured. Set MailVoidApi:Targets in appsettings.json");
            return;
        }

        foreach (var target in _apiOptions.Targets)
        {
            await CheckApiConnectivity(target, stoppingToken);
            CheckApiKeyConfiguration(target);
        }
    }

    private async Task CheckApiConnectivity(MailVoidApiTarget target, CancellationToken stoppingToken)
    {
        if (!target.Enabled)
        {
            _logger.LogInformation("⏸️ [{Target}] Disabled, skipping connectivity check", target.Name);
            return;
        }

        try
        {
            var healthUrl = $"{target.BaseUrl.TrimEnd('/')}/api/health";
            _logger.LogInformation("🏥 [{Target}] Testing MailVoid API connectivity: {Url}", target.Name, healthUrl);

            var response = await _httpClient.GetAsync(healthUrl, stoppingToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(stoppingToken);
                _logger.LogInformation("✅ [{Target}] MailVoid API is reachable - Status: {Status}, Response: {Response}",
                    target.Name, response.StatusCode, content);
            }
            else
            {
                _logger.LogWarning("⚠️ [{Target}] MailVoid API health check failed - Status: {Status}",
                    target.Name, response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("❌ [{Target}] Cannot reach MailVoid API at {Url} - Network Error: {Error}",
                target.Name, target.BaseUrl, ex.Message);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError("⏰ [{Target}] Timeout connecting to MailVoid API at {Url}",
                target.Name, target.BaseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [{Target}] Unexpected error checking MailVoid API connectivity: {Error}",
                target.Name, ex.Message);
        }
    }

    private void CheckApiKeyConfiguration(MailVoidApiTarget target)
    {
        if (string.IsNullOrEmpty(target.ApiKey))
        {
            _logger.LogWarning("⚠️ [{Target}] No API key configured. Set MailVoidApi:Targets[*]:ApiKey",
                target.Name);
        }
        else if (target.ApiKey == "smtp-server-key-change-this-in-production")
        {
            _logger.LogWarning("⚠️ [{Target}] Using default API key. Change in production!", target.Name);
        }
        else
        {
            _logger.LogInformation("🔑 [{Target}] API key is configured (length: {Length} chars)",
                target.Name, target.ApiKey.Length);
        }

        _logger.LogInformation("🌐 [{Target}] Configuration: BaseUrl={BaseUrl}, Endpoint={Endpoint}, Enabled={Enabled}",
            target.Name, target.BaseUrl, target.WebhookEndpoint, target.Enabled);
    }
}

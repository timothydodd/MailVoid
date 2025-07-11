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

        // Check MailVoid API connectivity
        await CheckApiConnectivity(stoppingToken);

        // Check API key configuration
        CheckApiKeyConfiguration();
    }

    private async Task CheckApiConnectivity(CancellationToken stoppingToken)
    {
        try
        {
            var healthUrl = $"{_apiOptions.BaseUrl.TrimEnd('/')}/api/health";
            _logger.LogInformation("🏥 Testing MailVoid API connectivity: {Url}", healthUrl);

            var response = await _httpClient.GetAsync(healthUrl, stoppingToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(stoppingToken);
                _logger.LogInformation("✅ MailVoid API is reachable - Status: {Status}, Response: {Response}",
                    response.StatusCode, content);
            }
            else
            {
                _logger.LogWarning("⚠️ MailVoid API health check failed - Status: {Status}", response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("❌ Cannot reach MailVoid API at {Url} - Network Error: {Error}. Make sure the MailVoid API is running.",
                _apiOptions.BaseUrl, ex.Message);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError("⏰ Timeout connecting to MailVoid API at {Url}. Check if the API is responding.",
                _apiOptions.BaseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Unexpected error checking MailVoid API connectivity: {Error}", ex.Message);
        }
    }

    private void CheckApiKeyConfiguration()
    {
        if (string.IsNullOrEmpty(_apiOptions.ApiKey))
        {
            _logger.LogWarning("⚠️ No API key configured. Set MailVoidApi:ApiKey in appsettings.json");
        }
        else if (_apiOptions.ApiKey == "smtp-server-key-change-this-in-production")
        {
            _logger.LogWarning("⚠️ Using default API key. Change MailVoidApi:ApiKey in production!");
        }
        else
        {
            _logger.LogInformation("🔑 API key is configured (length: {Length} chars)", _apiOptions.ApiKey.Length);
        }

        _logger.LogInformation("🌐 API Configuration: BaseUrl={BaseUrl}, Endpoint={Endpoint}",
            _apiOptions.BaseUrl, _apiOptions.WebhookEndpoint);
    }
}

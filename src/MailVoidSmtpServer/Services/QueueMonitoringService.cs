using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public class QueueMonitoringService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueueMonitoringService> _logger;
    private readonly QueueMonitoringOptions _options;

    public QueueMonitoringService(
        IServiceScopeFactory scopeFactory,
        ILogger<QueueMonitoringService> logger,
        IOptions<QueueMonitoringOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue monitoring service started - Reporting interval: {Interval}s", 
            _options.ReportingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReportQueueStatistics();
                await Task.Delay(TimeSpan.FromSeconds(_options.ReportingIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in queue monitoring service: {Error}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // Wait before retrying
            }
        }

        _logger.LogInformation("Queue monitoring service stopped");
    }

    private Task ReportQueueStatistics()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IEmailQueueService>();

            var pending = queue.PendingCount;
            var failed = queue.FailedEmails;
            var failedCount = failed.Count;

            if (pending > 0 || failedCount > 0)
            {
                _logger.LogInformation("📊 Queue Statistics - {Pending} pending, {Failed} dead-lettered",
                    pending, failedCount);
            }
            else if (_options.LogWhenEmpty)
            {
                _logger.LogDebug("📊 Queue is empty");
            }

            if (failedCount > 0)
            {
                var oldFailed = failed.Count(e => e.LastAttemptAt.HasValue &&
                    DateTime.UtcNow - e.LastAttemptAt.Value > TimeSpan.FromHours(1));
                if (oldFailed > 0)
                {
                    _logger.LogWarning("⚠️ {Count} emails have been dead-lettered for over 1 hour", oldFailed);
                }
            }

            if (pending > _options.HighQueueDepthThreshold)
            {
                _logger.LogWarning("🚨 High queue depth: {Count} emails pending", pending);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report queue statistics: {Error}", ex.Message);
        }

        return Task.CompletedTask;
    }
}

public class QueueMonitoringOptions
{
    public int ReportingIntervalSeconds { get; set; } = 60; // Report every minute
    public int HighQueueDepthThreshold { get; set; } = 50; // Warn when queue depth exceeds this
    public bool LogWhenEmpty { get; set; } = false; // Whether to log when queues are empty
}
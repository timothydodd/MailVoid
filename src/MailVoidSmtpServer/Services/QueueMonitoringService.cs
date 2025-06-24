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

    private async Task ReportQueueStatistics()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inboundQueue = scope.ServiceProvider.GetRequiredService<IInboundEmailQueueService>();
            var outboundQueue = scope.ServiceProvider.GetRequiredService<IOutboundEmailQueueService>();

            var inboundCount = await inboundQueue.GetQueueCountAsync();
            var outboundCount = await outboundQueue.GetQueueCountAsync();
            var inboundFailed = await inboundQueue.GetFailedEmailsAsync();
            var outboundFailed = await outboundQueue.GetFailedEmailsAsync();
            var outboundRetryable = await outboundQueue.GetRetryableEmailsAsync();

            var inboundFailedCount = inboundFailed.Count();
            var outboundFailedCount = outboundFailed.Count();
            var outboundRetryableCount = outboundRetryable.Count();

            // Log basic statistics
            if (inboundCount > 0 || outboundCount > 0 || inboundFailedCount > 0 || outboundFailedCount > 0 || outboundRetryableCount > 0)
            {
                _logger.LogInformation("📊 Queue Statistics - Inbound: {InboundCount} pending, {InboundFailed} failed | " +
                                     "Outbound: {OutboundCount} pending, {OutboundRetryable} retrying, {OutboundFailed} failed",
                    inboundCount, inboundFailedCount, outboundCount, outboundRetryableCount, outboundFailedCount);
            }
            else if (_options.LogWhenEmpty)
            {
                _logger.LogDebug("📊 All queues are empty");
            }

            // Report on old failed emails
            if (inboundFailedCount > 0)
            {
                var oldFailedInbound = inboundFailed
                    .Where(e => e.LastAttemptAt.HasValue && 
                               DateTime.UtcNow - e.LastAttemptAt.Value > TimeSpan.FromHours(1))
                    .Count();
                
                if (oldFailedInbound > 0)
                {
                    _logger.LogWarning("⚠️ {Count} inbound emails have been failed for over 1 hour", oldFailedInbound);
                }
            }

            if (outboundFailedCount > 0)
            {
                var oldFailedOutbound = outboundFailed
                    .Where(e => DateTime.UtcNow - e.QueuedAt > TimeSpan.FromHours(1))
                    .Count();
                
                if (oldFailedOutbound > 0)
                {
                    _logger.LogWarning("⚠️ {Count} outbound emails have been failed for over 1 hour", oldFailedOutbound);
                }
            }

            // Report on high queue depths
            if (inboundCount > _options.HighQueueDepthThreshold)
            {
                _logger.LogWarning("🚨 High inbound queue depth: {Count} emails pending", inboundCount);
            }

            if (outboundCount > _options.HighQueueDepthThreshold)
            {
                _logger.LogWarning("🚨 High outbound queue depth: {Count} emails pending", outboundCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report queue statistics: {Error}", ex.Message);
        }
    }
}

public class QueueMonitoringOptions
{
    public int ReportingIntervalSeconds { get; set; } = 60; // Report every minute
    public int HighQueueDepthThreshold { get; set; } = 50; // Warn when queue depth exceeds this
    public bool LogWhenEmpty { get; set; } = false; // Whether to log when queues are empty
}
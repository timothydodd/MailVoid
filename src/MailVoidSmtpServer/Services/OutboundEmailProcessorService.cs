using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public class OutboundEmailProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboundEmailProcessorService> _logger;
    private readonly EmailQueueOptions _options;

    public OutboundEmailProcessorService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboundEmailProcessorService> logger,
        IOptions<EmailQueueOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbound email processor service started");

        // Create multiple concurrent processing tasks
        var tasks = new List<Task>();
        for (int i = 0; i < _options.MaxConcurrentProcessing; i++)
        {
            tasks.Add(ProcessOutboundQueueAsync(i, stoppingToken));
        }

        await Task.WhenAll(tasks);
        _logger.LogInformation("Outbound email processor service stopped");
    }

    private async Task ProcessOutboundQueueAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogDebug("Outbound processor worker {WorkerId} started", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var outboundQueue = scope.ServiceProvider.GetRequiredService<IOutboundEmailQueueService>();
                var forwardingService = scope.ServiceProvider.GetRequiredService<MailForwardingService>();

                var queueItem = await outboundQueue.DequeueEmailAsync(stoppingToken);
                if (queueItem == null)
                {
                    // No emails to process, wait a bit
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                _logger.LogDebug("Worker {WorkerId} processing outbound email - ID: {EmailId}, From: {From}, Attempt: {Attempt}",
                    workerId, queueItem.Id, queueItem.EmailData.From, queueItem.RetryAttempts);

                try
                {
                    // Forward email to API
                    var success = await forwardingService.ForwardEmailAsync(queueItem.EmailData, stoppingToken);
                    
                    if (success)
                    {
                        await outboundQueue.MarkEmailAsSentAsync(queueItem.Id);
                        _logger.LogDebug("Worker {WorkerId} successfully sent outbound email - ID: {EmailId}",
                            workerId, queueItem.Id);
                    }
                    else
                    {
                        await outboundQueue.MarkEmailAsFailedAsync(queueItem.Id, "API forwarding returned failure");
                        _logger.LogWarning("Worker {WorkerId} failed to send outbound email - ID: {EmailId}, API returned failure",
                            workerId, queueItem.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker {WorkerId} failed to send outbound email - ID: {EmailId}, Error: {Error}",
                        workerId, queueItem.Id, ex.Message);

                    await outboundQueue.MarkEmailAsFailedAsync(queueItem.Id, ex.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker {WorkerId} encountered unexpected error: {Error}", workerId, ex.Message);
                await Task.Delay(5000, stoppingToken); // Wait before retrying
            }
        }

        _logger.LogDebug("Outbound processor worker {WorkerId} stopped", workerId);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping outbound email processor service...");
        await base.StopAsync(cancellationToken);
    }
}
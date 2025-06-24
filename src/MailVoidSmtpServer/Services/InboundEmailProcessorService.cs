using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public class InboundEmailProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InboundEmailProcessorService> _logger;
    private readonly EmailQueueOptions _options;

    public InboundEmailProcessorService(
        IServiceScopeFactory scopeFactory,
        ILogger<InboundEmailProcessorService> logger,
        IOptions<EmailQueueOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Inbound email processor service started");

        // Create multiple concurrent processing tasks
        var tasks = new List<Task>();
        for (int i = 0; i < _options.MaxConcurrentProcessing; i++)
        {
            tasks.Add(ProcessEmailQueueAsync(i, stoppingToken));
        }

        await Task.WhenAll(tasks);
        _logger.LogInformation("Inbound email processor service stopped");
    }

    private async Task ProcessEmailQueueAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogDebug("Email processor worker {WorkerId} started", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var queueService = scope.ServiceProvider.GetRequiredService<IInboundEmailQueueService>();
                var outboundQueue = scope.ServiceProvider.GetRequiredService<IOutboundEmailQueueService>();

                var queuedEmail = await queueService.DequeueEmailAsync(stoppingToken);
                if (queuedEmail == null)
                {
                    // No emails to process, wait a bit
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                _logger.LogDebug("Worker {WorkerId} processing email - ID: {EmailId}, From: {From}",
                    workerId, queuedEmail.Id, queuedEmail.EmailData.From);

                try
                {
                    // Queue email for outbound processing (API forwarding)
                    await outboundQueue.EnqueueEmailAsync(queuedEmail.EmailData);
                    
                    // Mark as successfully processed
                    await queueService.MarkEmailAsProcessedAsync(queuedEmail.Id);

                    _logger.LogDebug("Worker {WorkerId} successfully processed email - ID: {EmailId}",
                        workerId, queuedEmail.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker {WorkerId} failed to process email - ID: {EmailId}, Error: {Error}",
                        workerId, queuedEmail.Id, ex.Message);

                    await queueService.MarkEmailAsFailedAsync(queuedEmail.Id, ex.Message);
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

        _logger.LogDebug("Email processor worker {WorkerId} stopped", workerId);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping inbound email processor service...");
        await base.StopAsync(cancellationToken);
    }
}
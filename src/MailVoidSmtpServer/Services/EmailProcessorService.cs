using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public class EmailProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailQueueService _queue;
    private readonly ILogger<EmailProcessorService> _logger;
    private readonly EmailQueueOptions _options;

    public EmailProcessorService(
        IServiceScopeFactory scopeFactory,
        IEmailQueueService queue,
        ILogger<EmailProcessorService> logger,
        IOptions<EmailQueueOptions> options)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email processor service started");

        // Start the workers first so they are draining the channel, then recover any messages
        // persisted from a previous run. (Loading first could block if the spool exceeds channel
        // capacity, since a bounded channel's writer waits for a reader.)
        var workers = new List<Task>();
        for (int i = 0; i < Math.Max(1, _options.MaxConcurrentProcessing); i++)
        {
            workers.Add(ProcessAsync(i, stoppingToken));
        }

        await _queue.LoadSpoolAsync(stoppingToken);

        await Task.WhenAll(workers);
        _logger.LogInformation("Email processor service stopped");
    }

    private async Task ProcessAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogDebug("Email processor worker {WorkerId} started", workerId);

        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var forwarding = scope.ServiceProvider.GetRequiredService<MailForwardingService>();

                    var success = await forwarding.ForwardEmailAsync(item.EmailData, stoppingToken);
                    if (success)
                    {
                        await _queue.MarkProcessedAsync(item);
                    }
                    else
                    {
                        await _queue.MarkFailedAsync(item, "API forwarding returned failure");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker {WorkerId} failed to process email - ID: {EmailId}", workerId, item.Id);
                    await _queue.MarkFailedAsync(item, ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown
        }

        _logger.LogDebug("Email processor worker {WorkerId} stopped", workerId);
    }
}

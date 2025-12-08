using MailVoidApi.Data;
using MailVoidApi.Models;
using RoboDodd.OrmLite;

namespace MailVoidApi.Services;

public class WebhookCleanupService : BackgroundService
{
    private readonly IDatabaseService _dbService;
    private readonly ILogger<WebhookCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

    public WebhookCleanupService(
        IDatabaseService dbService,
        ILogger<WebhookCleanupService> logger)
    {
        _dbService = dbService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook Cleanup Service starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformCleanup(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during webhook cleanup");
            }

            await Task.Delay(_cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("Webhook Cleanup Service stopping...");
    }

    private async Task PerformCleanup(CancellationToken cancellationToken)
    {
        using var db = await _dbService.GetConnectionAsync();

        _logger.LogInformation("Starting webhook cleanup cycle");

        // Get all buckets with retention settings
        var bucketsWithRetention = await db.SelectAsync<WebhookBucket>(b => b.RetentionDays != null && b.RetentionDays > 0);

        _logger.LogInformation("Found {Count} webhook buckets with retention settings", bucketsWithRetention.Count);

        foreach (var bucket in bucketsWithRetention)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-bucket.RetentionDays!.Value);

                // Find webhooks to delete
                var webhooksToDelete = await db.SelectAsync<Webhook>(w => w.BucketName == bucket.Name && w.CreatedOn < cutoffDate);

                if (webhooksToDelete.Any())
                {
                    _logger.LogInformation(
                        "Deleting {Count} webhooks from bucket '{Name}' older than {Days} days",
                        webhooksToDelete.Count, bucket.Name, bucket.RetentionDays);

                    foreach (var webhook in webhooksToDelete)
                    {
                        await db.DeleteAsync(webhook);
                    }

                    _logger.LogInformation(
                        "Successfully deleted {Count} webhooks from bucket '{Name}'",
                        webhooksToDelete.Count, bucket.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error cleaning up webhook bucket '{Name}' with retention {Days} days",
                    bucket.Name, bucket.RetentionDays);
            }
        }

        _logger.LogInformation("Webhook cleanup cycle completed");
    }
}

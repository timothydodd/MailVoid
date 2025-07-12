using MailVoidApi.Data;
using MailVoidWeb;
using MailVoidWeb.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MailVoidApi.Services
{
    public class MailCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MailCleanupService> _logger;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1); // Run every hour

        public MailCleanupService(
            IServiceProvider serviceProvider,
            ILogger<MailCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Mail Cleanup Service starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformCleanup(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during mail cleanup");
                }

                await Task.Delay(_cleanupInterval, stoppingToken);
            }

            _logger.LogInformation("Mail Cleanup Service stopping...");
        }

        private async Task PerformCleanup(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MailVoidDbContext>();

            _logger.LogInformation("Starting mail cleanup cycle");

            // Get all mailboxes with retention settings
            var mailboxesWithRetention = await dbContext.MailGroups
                .Where(mg => mg.RetentionDays.HasValue && mg.RetentionDays > 0)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} mailboxes with retention settings", mailboxesWithRetention.Count);

            foreach (var mailbox in mailboxesWithRetention)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var cutoffDate = DateTime.UtcNow.AddDays(-mailbox.RetentionDays!.Value);
                    
                    // Find emails to delete
                    var emailsToDelete = await dbContext.Mails
                        .Where(m => m.MailGroupPath == mailbox.Path && m.CreatedOn < cutoffDate)
                        .ToListAsync(cancellationToken);

                    if (emailsToDelete.Any())
                    {
                        _logger.LogInformation(
                            "Deleting {Count} emails from mailbox '{Path}' older than {Days} days",
                            emailsToDelete.Count, mailbox.Path, mailbox.RetentionDays);

                        // Delete associated read records first
                        var mailIds = emailsToDelete.Select(m => m.Id).ToList();
                        var readRecordsToDelete = await dbContext.UserMailReads
                            .Where(r => mailIds.Contains(r.MailId))
                            .ToListAsync(cancellationToken);

                        if (readRecordsToDelete.Any())
                        {
                            dbContext.UserMailReads.RemoveRange(readRecordsToDelete);
                        }

                        // Delete the emails
                        dbContext.Mails.RemoveRange(emailsToDelete);
                        
                        await dbContext.SaveChangesAsync(cancellationToken);

                        _logger.LogInformation(
                            "Successfully deleted {Count} emails from mailbox '{Path}'",
                            emailsToDelete.Count, mailbox.Path);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, 
                        "Error cleaning up mailbox '{Path}' with retention {Days} days",
                        mailbox.Path, mailbox.RetentionDays);
                }
            }

            _logger.LogInformation("Mail cleanup cycle completed");
        }
    }
}
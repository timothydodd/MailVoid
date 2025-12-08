using MailVoidApi.Data;
using MailVoidWeb;
using MailVoidWeb.Data.Models;
using RoboDodd.OrmLite;

namespace MailVoidApi.Services
{
    public class MailCleanupService : BackgroundService
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<MailCleanupService> _logger;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1); // Run every hour

        public MailCleanupService(
            IDatabaseService dbService,
            ILogger<MailCleanupService> logger)
        {
            _dbService = dbService;
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
            using var db = await _dbService.GetConnectionAsync();

            _logger.LogInformation("Starting mail cleanup cycle");

            // Get all mailboxes with retention settings
            var mailboxesWithRetention = await db.SelectAsync<MailGroup>(mg => mg.RetentionDays != null && mg.RetentionDays > 0);

            _logger.LogInformation("Found {Count} mailboxes with retention settings", mailboxesWithRetention.Count);

            foreach (var mailbox in mailboxesWithRetention)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var cutoffDate = DateTime.UtcNow.AddDays(-mailbox.RetentionDays!.Value);

                    // Find emails to delete
                    var emailsToDelete = await db.SelectAsync<Mail>(m => m.MailGroupPath == mailbox.Path && m.CreatedOn < cutoffDate);

                    if (emailsToDelete.Any())
                    {
                        _logger.LogInformation(
                            "Deleting {Count} emails from mailbox '{Path}' older than {Days} days",
                            emailsToDelete.Count, mailbox.Path, mailbox.RetentionDays);

                        // Delete associated read records first
                        var mailIds = emailsToDelete.Select(m => m.Id).ToList();
                        foreach (var mailId in mailIds)
                        {
                            await db.DeleteAsync<UserMailRead>(r => r.MailId == mailId);
                        }

                        // Delete the emails
                        foreach (var email in emailsToDelete)
                        {
                            await db.DeleteAsync(email);
                        }

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
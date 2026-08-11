using Dapper;
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

        // Global backstop: mail whose MailGroup was deleted (orphaned) is not covered by any
        // per-mailbox retention rule, so it would otherwise live forever. Reclaim it after this many days.
        private const int GlobalOrphanMaxAgeDays = 30;

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

                        // Single batched delete. Associated UserMailRead rows are removed by the
                        // declared ON DELETE CASCADE (enforced on both MySQL InnoDB and SQLite w/ PRAGMA).
                        await db.DeleteAsync<Mail>(m => m.MailGroupPath == mailbox.Path && m.CreatedOn < cutoffDate);

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

            await CleanupOrphanedMail(db, cancellationToken);

            _logger.LogInformation("Mail cleanup cycle completed");
        }

        // Backstop for orphaned mail (MailGroupPath no longer maps to a MailGroup). Such mail is
        // otherwise unreachable by per-mailbox retention, so delete it once it exceeds the global max age.
        private async Task CleanupOrphanedMail(System.Data.Common.DbConnection db, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-GlobalOrphanMaxAgeDays);
                var deleted = await db.ExecuteAsync(
                    @"DELETE FROM Mail
                      WHERE CreatedOn < @Cutoff
                        AND (MailGroupPath IS NULL
                             OR NOT EXISTS (SELECT 1 FROM MailGroup mg WHERE mg.Path = Mail.MailGroupPath))",
                    new { Cutoff = cutoffDate });

                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "Deleted {Count} orphaned emails older than {Days} days", deleted, GlobalOrphanMaxAgeDays);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up orphaned mail");
            }
        }
    }
}
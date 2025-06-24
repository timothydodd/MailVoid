using System.Collections.Concurrent;
using MailVoidSmtpServer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public interface IOutboundEmailQueueService
{
    Task<string> EnqueueEmailAsync(EmailWebhookData emailData, int priority = 0);
    Task<OutboundQueueItem?> DequeueEmailAsync(CancellationToken cancellationToken = default);
    Task MarkEmailAsSentAsync(string emailId);
    Task MarkEmailAsFailedAsync(string emailId, string error);
    Task<int> GetQueueCountAsync();
    Task<IEnumerable<OutboundQueueItem>> GetFailedEmailsAsync();
    Task<IEnumerable<OutboundQueueItem>> GetRetryableEmailsAsync();
}

public class OutboundEmailQueueService : IOutboundEmailQueueService
{
    private readonly ConcurrentQueue<OutboundQueueItem> _queue = new();
    private readonly ConcurrentDictionary<string, OutboundQueueItem> _processingEmails = new();
    private readonly ConcurrentDictionary<string, OutboundQueueItem> _failedEmails = new();
    private readonly ConcurrentDictionary<string, OutboundQueueItem> _retryQueue = new();
    private readonly ILogger<OutboundEmailQueueService> _logger;
    private readonly EmailQueueOptions _options;
    private readonly SemaphoreSlim _queueSemaphore = new(0);
    private readonly Timer _retryTimer;

    public OutboundEmailQueueService(
        ILogger<OutboundEmailQueueService> logger,
        IOptions<EmailQueueOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        
        // Timer to check for emails ready for retry every 30 seconds
        _retryTimer = new Timer(ProcessRetryQueue, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public Task<string> EnqueueEmailAsync(EmailWebhookData emailData, int priority = 0)
    {
        var queueItem = new OutboundQueueItem
        {
            EmailData = emailData,
            Priority = priority,
            QueuedAt = DateTime.UtcNow
        };

        _queue.Enqueue(queueItem);
        _queueSemaphore.Release();

        _logger.LogDebug("Enqueued outbound email - ID: {EmailId}, From: {From}, To: {To}, Priority: {Priority}",
            queueItem.Id, emailData.From, string.Join(",", emailData.To), priority);

        return Task.FromResult(queueItem.Id);
    }

    public async Task<OutboundQueueItem?> DequeueEmailAsync(CancellationToken cancellationToken = default)
    {
        await _queueSemaphore.WaitAsync(cancellationToken);

        // Find the highest priority email in the queue
        var emails = new List<OutboundQueueItem>();
        while (_queue.TryDequeue(out var email))
        {
            emails.Add(email);
        }

        if (!emails.Any())
        {
            return null;
        }

        // Sort by priority (highest first), then by queue time (oldest first)
        var selectedEmail = emails
            .OrderByDescending(e => e.Priority)
            .ThenBy(e => e.QueuedAt)
            .First();

        // Put back the other emails
        foreach (var email in emails.Where(e => e.Id != selectedEmail.Id))
        {
            _queue.Enqueue(email);
            _queueSemaphore.Release();
        }

        // Mark as processing
        selectedEmail.IsProcessing = true;
        selectedEmail.RetryAttempts++;
        _processingEmails[selectedEmail.Id] = selectedEmail;

        _logger.LogDebug("Dequeued outbound email - ID: {EmailId}, Attempt: {Attempt}",
            selectedEmail.Id, selectedEmail.RetryAttempts);

        return selectedEmail;
    }

    public Task MarkEmailAsSentAsync(string emailId)
    {
        if (_processingEmails.TryRemove(emailId, out var email))
        {
            _logger.LogInformation("Outbound email sent successfully - ID: {EmailId}, From: {From}, Attempts: {Attempts}",
                emailId, email.EmailData.From, email.RetryAttempts);
        }
        return Task.CompletedTask;
    }

    public Task MarkEmailAsFailedAsync(string emailId, string error)
    {
        if (_processingEmails.TryRemove(emailId, out var email))
        {
            email.LastError = error;
            email.IsProcessing = false;

            if (email.RetryAttempts < _options.MaxRetryAttempts)
            {
                // Schedule for retry with exponential backoff
                var delay = CalculateRetryDelay(email.RetryAttempts);
                email.NextRetryAt = DateTime.UtcNow.Add(delay);
                _retryQueue[emailId] = email;

                _logger.LogWarning("Outbound email failed, will retry - ID: {EmailId}, Attempt: {Attempt}/{MaxAttempts}, Next retry at: {NextRetry}, Error: {Error}",
                    emailId, email.RetryAttempts, _options.MaxRetryAttempts, email.NextRetryAt, error);
            }
            else
            {
                // Max retries exceeded, move to permanently failed queue
                _failedEmails[emailId] = email;
                _logger.LogError("Outbound email failed permanently - ID: {EmailId}, From: {From}, Final error: {Error}",
                    emailId, email.EmailData.From, error);
            }
        }
        return Task.CompletedTask;
    }

    public Task<int> GetQueueCountAsync()
    {
        return Task.FromResult(_queue.Count + _processingEmails.Count + _retryQueue.Count);
    }

    public Task<IEnumerable<OutboundQueueItem>> GetFailedEmailsAsync()
    {
        return Task.FromResult<IEnumerable<OutboundQueueItem>>(_failedEmails.Values.ToList());
    }

    public Task<IEnumerable<OutboundQueueItem>> GetRetryableEmailsAsync()
    {
        return Task.FromResult<IEnumerable<OutboundQueueItem>>(_retryQueue.Values.ToList());
    }

    private void ProcessRetryQueue(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var readyForRetry = _retryQueue.Values
                .Where(item => item.NextRetryAt.HasValue && item.NextRetryAt.Value <= now)
                .ToList();

            foreach (var item in readyForRetry)
            {
                if (_retryQueue.TryRemove(item.Id, out _))
                {
                    _queue.Enqueue(item);
                    _queueSemaphore.Release();

                    _logger.LogDebug("Moved email from retry queue back to main queue - ID: {EmailId}, Attempt: {Attempt}",
                        item.Id, item.RetryAttempts);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing retry queue: {Error}", ex.Message);
        }
    }

    private TimeSpan CalculateRetryDelay(int attemptNumber)
    {
        // Exponential backoff: 5s, 10s, 20s, 40s, etc. with jitter
        var baseDelay = TimeSpan.FromSeconds(_options.BaseRetryDelaySeconds);
        var exponentialDelay = TimeSpan.FromTicks(baseDelay.Ticks * (long)Math.Pow(2, attemptNumber - 1));
        
        // Add jitter to prevent thundering herd (±25% randomization)
        var random = new Random();
        var jitter = 1.0 + (random.NextDouble() - 0.5) * 0.5; // 0.75 to 1.25 multiplier
        
        return TimeSpan.FromTicks((long)(exponentialDelay.Ticks * jitter));
    }

    public void Dispose()
    {
        _retryTimer?.Dispose();
        _queueSemaphore?.Dispose();
    }
}
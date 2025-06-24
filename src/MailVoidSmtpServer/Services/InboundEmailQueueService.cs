using System.Collections.Concurrent;
using MailVoidSmtpServer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public interface IInboundEmailQueueService
{
    Task<string> EnqueueEmailAsync(EmailWebhookData emailData, int priority = 0);
    Task<QueuedEmail?> DequeueEmailAsync(CancellationToken cancellationToken = default);
    Task MarkEmailAsProcessedAsync(string emailId);
    Task MarkEmailAsFailedAsync(string emailId, string error);
    Task<int> GetQueueCountAsync();
    Task<IEnumerable<QueuedEmail>> GetFailedEmailsAsync();
}

public class InboundEmailQueueService : IInboundEmailQueueService
{
    private readonly ConcurrentQueue<QueuedEmail> _queue = new();
    private readonly ConcurrentDictionary<string, QueuedEmail> _processingEmails = new();
    private readonly ConcurrentDictionary<string, QueuedEmail> _failedEmails = new();
    private readonly ILogger<InboundEmailQueueService> _logger;
    private readonly EmailQueueOptions _options;
    private readonly SemaphoreSlim _queueSemaphore = new(0);

    public InboundEmailQueueService(
        ILogger<InboundEmailQueueService> logger,
        IOptions<EmailQueueOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public Task<string> EnqueueEmailAsync(EmailWebhookData emailData, int priority = 0)
    {
        var queuedEmail = new QueuedEmail
        {
            EmailData = emailData,
            Priority = priority,
            QueuedAt = DateTime.UtcNow
        };

        _queue.Enqueue(queuedEmail);
        _queueSemaphore.Release();

        _logger.LogDebug("Enqueued email for processing - ID: {EmailId}, From: {From}, To: {To}, Priority: {Priority}",
            queuedEmail.Id, emailData.From, string.Join(",", emailData.To), priority);

        return Task.FromResult(queuedEmail.Id);
    }

    public async Task<QueuedEmail?> DequeueEmailAsync(CancellationToken cancellationToken = default)
    {
        await _queueSemaphore.WaitAsync(cancellationToken);

        // Find the highest priority email in the queue
        var emails = new List<QueuedEmail>();
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
        selectedEmail.ProcessingAttempts++;
        selectedEmail.LastAttemptAt = DateTime.UtcNow;
        _processingEmails[selectedEmail.Id] = selectedEmail;

        _logger.LogDebug("Dequeued email for processing - ID: {EmailId}, Attempt: {Attempt}",
            selectedEmail.Id, selectedEmail.ProcessingAttempts);

        return selectedEmail;
    }

    public Task MarkEmailAsProcessedAsync(string emailId)
    {
        if (_processingEmails.TryRemove(emailId, out var email))
        {
            _logger.LogInformation("Email processed successfully - ID: {EmailId}, From: {From}, Attempts: {Attempts}",
                emailId, email.EmailData.From, email.ProcessingAttempts);
        }
        return Task.CompletedTask;
    }

    public Task MarkEmailAsFailedAsync(string emailId, string error)
    {
        if (_processingEmails.TryRemove(emailId, out var email))
        {
            email.LastError = error;
            email.IsProcessing = false;

            if (email.ProcessingAttempts < _options.MaxRetryAttempts)
            {
                // Re-queue for retry with exponential backoff
                var delay = TimeSpan.FromSeconds(Math.Pow(2, email.ProcessingAttempts - 1) * _options.BaseRetryDelaySeconds);
                
                _logger.LogWarning("Email processing failed, will retry - ID: {EmailId}, Attempt: {Attempt}/{MaxAttempts}, Next retry in: {Delay}",
                    emailId, email.ProcessingAttempts, _options.MaxRetryAttempts, delay);

                // Schedule re-queue after delay
                _ = Task.Delay(delay).ContinueWith(async _ =>
                {
                    await EnqueueEmailAsync(email.EmailData, email.Priority);
                });
            }
            else
            {
                // Max retries exceeded, move to failed queue
                _failedEmails[emailId] = email;
                _logger.LogError("Email processing failed permanently - ID: {EmailId}, From: {From}, Final error: {Error}",
                    emailId, email.EmailData.From, error);
            }
        }
        return Task.CompletedTask;
    }

    public Task<int> GetQueueCountAsync()
    {
        return Task.FromResult(_queue.Count + _processingEmails.Count);
    }

    public Task<IEnumerable<QueuedEmail>> GetFailedEmailsAsync()
    {
        return Task.FromResult<IEnumerable<QueuedEmail>>(_failedEmails.Values.ToList());
    }
}

public class EmailQueueOptions
{
    public int MaxRetryAttempts { get; set; } = 3;
    public int BaseRetryDelaySeconds { get; set; } = 5;
    public int MaxConcurrentProcessing { get; set; } = 5;
}
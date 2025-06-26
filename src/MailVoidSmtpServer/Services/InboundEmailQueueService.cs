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
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue email - From: {From}, To: {To}, Subject: {Subject}",
                emailData.From, string.Join(",", emailData.To), emailData.Subject);
            throw;
        }
    }

    public async Task<QueuedEmail?> DequeueEmailAsync(CancellationToken cancellationToken = default)
    {
        try
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
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Dequeue operation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dequeue email from processing queue");
            throw;
        }
    }

    public Task MarkEmailAsProcessedAsync(string emailId)
    {
        try
        {
            if (_processingEmails.TryRemove(emailId, out var email))
            {
                _logger.LogInformation("Email processed successfully - ID: {EmailId}, From: {From}, Attempts: {Attempts}",
                    emailId, email.EmailData.From, email.ProcessingAttempts);
            }
            else
            {
                _logger.LogWarning("Attempted to mark unknown email as processed - ID: {EmailId}", emailId);
            }
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark email as processed - ID: {EmailId}", emailId);
            throw;
        }
    }

    public Task MarkEmailAsFailedAsync(string emailId, string error)
    {
        try
        {
            if (_processingEmails.TryRemove(emailId, out var email))
            {
                email.LastError = error;
                email.IsProcessing = false;

                if (email.ProcessingAttempts < _options.MaxRetryAttempts)
                {
                    // Re-queue for retry with exponential backoff
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, email.ProcessingAttempts - 1) * _options.BaseRetryDelaySeconds);
                    
                    _logger.LogWarning("Email processing failed, will retry - ID: {EmailId}, From: {From}, Subject: {Subject}, Attempt: {Attempt}/{MaxAttempts}, Error: {Error}, Next retry in: {Delay}",
                        emailId, email.EmailData.From, email.EmailData.Subject, email.ProcessingAttempts, _options.MaxRetryAttempts, error, delay);

                    // Schedule re-queue after delay
                    _ = Task.Delay(delay).ContinueWith(async _ =>
                    {
                        try
                        {
                            await EnqueueEmailAsync(email.EmailData, email.Priority);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to re-enqueue email for retry - ID: {EmailId}", emailId);
                        }
                    });
                }
                else
                {
                    // Max retries exceeded, move to failed queue
                    _failedEmails[emailId] = email;
                    _logger.LogError("Email processing failed permanently - ID: {EmailId}, From: {From}, Subject: {Subject}, Final error: {Error}, Total attempts: {Attempts}",
                        emailId, email.EmailData.From, email.EmailData.Subject, error, email.ProcessingAttempts);
                }
            }
            else
            {
                _logger.LogWarning("Attempted to mark unknown email as failed - ID: {EmailId}, Error: {Error}", emailId, error);
            }
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark email as failed - ID: {EmailId}, Original error: {OriginalError}", emailId, error);
            throw;
        }
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
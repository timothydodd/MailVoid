using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MailVoidSmtpServer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public interface IEmailQueueService
{
    /// <summary>
    /// Persists the email to the disk spool and enqueues it for processing.
    /// Returns only after the message is durable on disk, so the caller can safely
    /// acknowledge the SMTP transaction (250 OK).
    /// </summary>
    Task<string> EnqueueAsync(EmailWebhookData emailData, CancellationToken cancellationToken = default);

    ChannelReader<QueuedEmail> Reader { get; }

    Task MarkProcessedAsync(QueuedEmail item);
    Task MarkFailedAsync(QueuedEmail item, string error);

    /// <summary>Reloads any spool files left over from a previous run.</summary>
    Task LoadSpoolAsync(CancellationToken cancellationToken = default);

    int PendingCount { get; }
    IReadOnlyCollection<QueuedEmail> FailedEmails { get; }
}

public class EmailSpoolQueue : IEmailQueueService, IDisposable
{
    private readonly ILogger<EmailSpoolQueue> _logger;
    private readonly EmailQueueOptions _options;
    private readonly Channel<QueuedEmail> _channel;
    private readonly ConcurrentDictionary<string, QueuedEmail> _retry = new();
    private readonly ConcurrentDictionary<string, QueuedEmail> _failed = new();
    private readonly Timer _retryTimer;
    private readonly string _spoolDir;
    private readonly string _deadLetterDir;
    private int _pending;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public EmailSpoolQueue(ILogger<EmailSpoolQueue> logger, IOptions<EmailQueueOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        _spoolDir = Path.GetFullPath(_options.SpoolDirectory);
        _deadLetterDir = Path.Combine(_spoolDir, "dead-letter");
        Directory.CreateDirectory(_spoolDir);
        Directory.CreateDirectory(_deadLetterDir);

        _channel = Channel.CreateBounded<QueuedEmail>(new BoundedChannelOptions(_options.MaxQueueDepth)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        _retryTimer = new Timer(ProcessRetryQueue, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public ChannelReader<QueuedEmail> Reader => _channel.Reader;

    public int PendingCount => Volatile.Read(ref _pending) + _retry.Count;

    public IReadOnlyCollection<QueuedEmail> FailedEmails => _failed.Values.ToList();

    public async Task<string> EnqueueAsync(EmailWebhookData emailData, CancellationToken cancellationToken = default)
    {
        var item = new QueuedEmail
        {
            EmailData = emailData,
            QueuedAt = DateTime.UtcNow
        };

        // Durability: persist to disk BEFORE the item is considered accepted.
        item.SpoolPath = await PersistAsync(item, cancellationToken);

        Interlocked.Increment(ref _pending);
        await _channel.Writer.WriteAsync(item, cancellationToken);

        _logger.LogDebug("Enqueued email - ID: {EmailId}, From: {From}, To: {To}",
            item.Id, emailData.From, string.Join(",", emailData.To));

        return item.Id;
    }

    public async Task MarkProcessedAsync(QueuedEmail item)
    {
        Interlocked.Decrement(ref _pending);
        await DeleteSpoolAsync(item);
        _logger.LogInformation("Email processed successfully - ID: {EmailId}, From: {From}, Attempts: {Attempts}",
            item.Id, item.EmailData.From, item.Attempts + 1);
    }

    public async Task MarkFailedAsync(QueuedEmail item, string error)
    {
        item.Attempts++;
        item.LastError = error;
        item.LastAttemptAt = DateTime.UtcNow;

        if (item.Attempts < _options.MaxRetryAttempts)
        {
            await PersistAsync(item);
            item.NextRetryAt = DateTime.UtcNow.Add(CalculateRetryDelay(item.Attempts));
            _retry[item.Id] = item;

            _logger.LogWarning("Email processing failed, scheduling retry - ID: {EmailId}, From: {From}, Attempt: {Attempt}/{Max}, Next retry: {Next}, Error: {Error}",
                item.Id, item.EmailData.From, item.Attempts, _options.MaxRetryAttempts, item.NextRetryAt, error);
        }
        else
        {
            Interlocked.Decrement(ref _pending);
            await MoveToDeadLetterAsync(item);
            _failed[item.Id] = item;
            _logger.LogError("Email processing failed permanently - ID: {EmailId}, From: {From}, Subject: {Subject}, Attempts: {Attempts}, Final error: {Error}",
                item.Id, item.EmailData.From, item.EmailData.Subject, item.Attempts, error);
        }
    }

    public async Task LoadSpoolAsync(CancellationToken cancellationToken = default)
    {
        var files = Directory.EnumerateFiles(_spoolDir, "*.json", SearchOption.TopDirectoryOnly).ToList();
        if (files.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Reloading {Count} spooled email(s) from disk", files.Count);

        foreach (var file in files)
        {
            try
            {
                await using var fs = File.OpenRead(file);
                var item = await JsonSerializer.DeserializeAsync<QueuedEmail>(fs, JsonOptions, cancellationToken);
                if (item == null)
                {
                    continue;
                }

                item.SpoolPath = file;
                Interlocked.Increment(ref _pending);
                await _channel.Writer.WriteAsync(item, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload spool file {File}", file);
            }
        }
    }

    private void ProcessRetryQueue(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            foreach (var item in _retry.Values.Where(i => i.NextRetryAt.HasValue && i.NextRetryAt.Value <= now).ToList())
            {
                if (_channel.Writer.TryWrite(item))
                {
                    _retry.TryRemove(item.Id, out _);
                    _logger.LogInformation("Retry time reached, re-queued email - ID: {EmailId}, Attempt: {Attempt}",
                        item.Id, item.Attempts);
                }
                // If the channel is full, leave the item for the next tick (backpressure).
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing retry queue");
        }
    }

    private async Task<string> PersistAsync(QueuedEmail item, CancellationToken cancellationToken = default)
    {
        var path = item.SpoolPath ?? Path.Combine(_spoolDir, $"{item.Id}.json");
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(fs, item, JsonOptions, cancellationToken);
            await fs.FlushAsync(cancellationToken);
        }
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    private Task DeleteSpoolAsync(QueuedEmail item)
    {
        TryDelete(item.SpoolPath);
        return Task.CompletedTask;
    }

    private async Task MoveToDeadLetterAsync(QueuedEmail item)
    {
        try
        {
            var target = Path.Combine(_deadLetterDir, $"{item.Id}.json");
            await PersistAsync(item);
            if (item.SpoolPath != null && File.Exists(item.SpoolPath))
            {
                File.Move(item.SpoolPath, target, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move email {EmailId} to dead-letter", item.Id);
        }
    }

    private void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete spool file {File}", path);
        }
    }

    private TimeSpan CalculateRetryDelay(int attempt)
    {
        var baseDelay = TimeSpan.FromSeconds(_options.BaseRetryDelaySeconds);
        var exponential = TimeSpan.FromTicks(baseDelay.Ticks * (long)Math.Pow(2, attempt - 1));
        var jitter = 1.0 + (Random.Shared.NextDouble() - 0.5) * 0.5; // 0.75 - 1.25
        return TimeSpan.FromTicks((long)(exponential.Ticks * jitter));
    }

    public void Dispose()
    {
        _retryTimer.Dispose();
        _channel.Writer.TryComplete();
    }
}

public class EmailQueueOptions
{
    public int MaxRetryAttempts { get; set; } = 3;
    public int BaseRetryDelaySeconds { get; set; } = 5;
    public int MaxConcurrentProcessing { get; set; } = 5;
    public int MaxQueueDepth { get; set; } = 1000;
    public string SpoolDirectory { get; set; } = "spool";
}

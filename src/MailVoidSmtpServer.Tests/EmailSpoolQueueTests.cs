using MailVoidSmtpServer.Models;
using MailVoidSmtpServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Tests;

public class EmailSpoolQueueTests : IDisposable
{
    private readonly string _spoolDir;

    public EmailSpoolQueueTests()
    {
        _spoolDir = Path.Combine(Path.GetTempPath(), "mailvoid-spool-tests", Guid.NewGuid().ToString("N"));
    }

    private EmailSpoolQueue CreateQueue(int maxRetries = 3)
    {
        var options = Options.Create(new EmailQueueOptions
        {
            MaxRetryAttempts = maxRetries,
            BaseRetryDelaySeconds = 1,
            MaxQueueDepth = 100,
            SpoolDirectory = _spoolDir
        });
        return new EmailSpoolQueue(NullLogger<EmailSpoolQueue>.Instance, options);
    }

    private static EmailWebhookData Sample() => new()
    {
        From = "sender@example.com",
        To = new List<string> { "recipient@mailvoid.com" },
        Subject = "test"
    };

    [Fact]
    public async Task Enqueue_PersistsToDiskBeforeReturning()
    {
        using var queue = CreateQueue();
        await queue.EnqueueAsync(Sample());

        var files = Directory.GetFiles(_spoolDir, "*.json");
        Assert.Single(files);
    }

    [Fact]
    public async Task MarkFailed_IncrementsAttempts_AndDeadLettersAfterMaxRetries()
    {
        using var queue = CreateQueue(maxRetries: 3);
        await queue.EnqueueAsync(Sample());

        var item = await queue.Reader.ReadAsync();
        Assert.Equal(0, item.Attempts);

        // Attempts must accumulate across retries (regression: previously reset to 0 => infinite retries).
        await queue.MarkFailedAsync(item, "boom");
        Assert.Equal(1, item.Attempts);
        Assert.Empty(queue.FailedEmails);

        await queue.MarkFailedAsync(item, "boom");
        Assert.Equal(2, item.Attempts);
        Assert.Empty(queue.FailedEmails);

        await queue.MarkFailedAsync(item, "boom");
        Assert.Equal(3, item.Attempts);

        // Now dead-lettered.
        var failed = Assert.Single(queue.FailedEmails);
        Assert.Equal(item.Id, failed.Id);

        var deadLetterFiles = Directory.GetFiles(Path.Combine(_spoolDir, "dead-letter"), "*.json");
        Assert.Single(deadLetterFiles);
    }

    [Fact]
    public async Task MarkProcessed_DeletesSpoolFile()
    {
        using var queue = CreateQueue();
        await queue.EnqueueAsync(Sample());
        var item = await queue.Reader.ReadAsync();

        await queue.MarkProcessedAsync(item);

        Assert.Empty(Directory.GetFiles(_spoolDir, "*.json"));
    }

    [Fact]
    public async Task LoadSpool_RecoversPersistedItems()
    {
        // First queue persists an item and is disposed without processing it.
        using (var queue = CreateQueue())
        {
            await queue.EnqueueAsync(Sample());
        }

        // A fresh queue over the same directory must recover it (durability across restart).
        using var recovered = CreateQueue();
        await recovered.LoadSpoolAsync();

        var item = await recovered.Reader.ReadAsync();
        Assert.Equal("sender@example.com", item.EmailData.From);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_spoolDir))
            {
                Directory.Delete(_spoolDir, recursive: true);
            }
        }
        catch { /* best effort */ }
    }
}

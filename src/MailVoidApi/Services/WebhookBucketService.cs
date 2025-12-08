using MailVoidApi.Data;
using MailVoidApi.Models;
using MailVoidWeb.Data.Models;
using RoboDodd.OrmLite;

namespace MailVoidApi.Services;

public interface IWebhookBucketService
{
    Task<WebhookBucket> GetOrCreateBucket(string name);
    Task<bool> HasUserAccess(long bucketId, Guid userId);
    Task<List<WebhookBucket>> GetAllBuckets();
    Task<WebhookBucket?> GetBucketByName(string name);
}

public class WebhookBucketService : IWebhookBucketService
{
    private readonly ILogger<WebhookBucketService> _logger;
    private readonly IDatabaseService _db;

    public WebhookBucketService(ILogger<WebhookBucketService> logger, IDatabaseService db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task<WebhookBucket> GetOrCreateBucket(string name)
    {
        using var db = await _db.GetConnectionAsync();

        var existingBucket = await db.SingleAsync<WebhookBucket>(b => b.Name == name);

        if (existingBucket != null)
        {
            return existingBucket;
        }

        // Get admin user as default owner
        var adminUser = await db.SingleAsync<User>(u => u.UserName == "admin");
        if (adminUser == null)
        {
            throw new InvalidOperationException("Admin user not found");
        }

        var newBucket = new WebhookBucket
        {
            Name = name,
            Description = $"Auto-created bucket for {name}",
            OwnerUserId = adminUser.Id,
            IsPublic = true,
            CreatedAt = DateTime.UtcNow
        };

        await db.InsertAsync(newBucket);

        _logger.LogInformation("Created new webhook bucket: {Name}", name);

        // Fetch the inserted bucket to get the ID
        return await db.SingleAsync<WebhookBucket>(b => b.Name == name) ?? newBucket;
    }

    public async Task<bool> HasUserAccess(long bucketId, Guid userId)
    {
        using var db = await _db.GetConnectionAsync();

        var bucket = await db.SingleByIdAsync<WebhookBucket>(bucketId);

        if (bucket == null)
            return false;

        // Owner always has access
        if (bucket.OwnerUserId == userId)
            return true;

        // Public buckets are accessible to all users
        if (bucket.IsPublic)
            return true;

        return false;
    }

    public async Task<List<WebhookBucket>> GetAllBuckets()
    {
        using var db = await _db.GetConnectionAsync();
        return await db.SelectAsync<WebhookBucket>();
    }

    public async Task<WebhookBucket?> GetBucketByName(string name)
    {
        using var db = await _db.GetConnectionAsync();
        return await db.SingleAsync<WebhookBucket>(b => b.Name == name);
    }
}

using MailVoidApi.Data;
using MailVoidWeb;
using MailVoidWeb.Data.Models;
using RoboDodd.OrmLite;

namespace MailVoidApi.Services;

public interface IMailGroupService
{
    Task SetMailPath(Mail m);
    Task<MailGroup> GetOrCreateMailGroup(string subdomain, Guid? userId = null);
    Task<bool> HasUserAccess(long mailGroupId, Guid userId);
    Task GrantUserAccess(long mailGroupId, Guid userId);
    Task RevokeUserAccess(long mailGroupId, Guid userId);
    Task<MailGroup> CreateUserPrivateMailGroup(Guid userId, bool isDefault = false);
    Task<List<MailGroup>> GetUserPrivateMailGroups(Guid userId);
}

public class MailGroupService : IMailGroupService
{
    private readonly ILogger<MailGroupService> _logger;
    private readonly IDatabaseService _db;

    public MailGroupService(ILogger<MailGroupService> logger, IDatabaseService db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task SetMailPath(Mail m)
    {
        // Extract subdomain to check if this is a user's private mailbox
        var subdomain = EmailSubdomainHelper.ExtractSubdomain(m.To);

        using var db = await _db.GetConnectionAsync();

        // Check if this email belongs to a user's private mailbox (subdomain matches username)
        var privateMailGroup = await db.SingleAsync<MailGroup>(mg => mg.IsUserPrivate && mg.Subdomain == subdomain);

        if (privateMailGroup != null)
        {
            m.MailGroupPath = privateMailGroup.Path;
            return;
        }

        // For non-private emails, create/assign to mail group
        m.MailGroupPath = EmailSubdomainHelper.GenerateMailGroupPath(subdomain);

        // Ensure the mail group exists for this subdomain
        await GetOrCreateMailGroup(subdomain);
    }

    public async Task<MailGroup> GetOrCreateMailGroup(string subdomain, Guid? userId = null)
    {
        var path = EmailSubdomainHelper.GenerateMailGroupPath(subdomain);

        using var db = await _db.GetConnectionAsync();

        var existingGroup = await db.SingleAsync<MailGroup>(mg => mg.Subdomain == subdomain);

        if (existingGroup != null)
        {
            return existingGroup;
        }

        // Create new mail group with admin user as owner if provided
        User? adminUser;
        if (userId.HasValue)
        {
            adminUser = await db.SingleByIdAsync<User>(userId.Value);
        }
        else
        {
            adminUser = await db.SingleAsync<User>(u => u.UserName == "admin");
        }

        if (adminUser == null)
        {
            throw new InvalidOperationException("Admin user not found");
        }

        var newGroup = new MailGroup
        {
            Path = path,
            Subdomain = subdomain,
            OwnerUserId = adminUser.Id,
            IsPublic = true,
            Description = $"Auto-generated group for {subdomain} subdomain"
        };

        await db.InsertAsync(newGroup);

        _logger.LogInformation("Created new mail group for subdomain: {Subdomain}", subdomain);
        return newGroup;
    }

    public async Task<bool> HasUserAccess(long mailGroupId, Guid userId)
    {
        using var db = await _db.GetConnectionAsync();

        var mailGroup = await db.SingleByIdAsync<MailGroup>(mailGroupId);

        if (mailGroup == null)
            return false;

        // Owner always has access
        if (mailGroup.OwnerUserId == userId)
            return true;

        // Public groups are accessible to all users
        if (mailGroup.IsPublic)
            return true;

        // Check explicit user access
        return await db.ExistsAsync<MailGroupUser>(mgu => mgu.MailGroupId == mailGroupId && mgu.UserId == userId);
    }

    public async Task GrantUserAccess(long mailGroupId, Guid userId)
    {
        using var db = await _db.GetConnectionAsync();

        var existingAccess = await db.SingleAsync<MailGroupUser>(mgu => mgu.MailGroupId == mailGroupId && mgu.UserId == userId);

        if (existingAccess == null)
        {
            var mailGroupUser = new MailGroupUser
            {
                MailGroupId = mailGroupId,
                UserId = userId
            };

            await db.InsertAsync(mailGroupUser);

            _logger.LogInformation("Granted user {UserId} access to mail group {MailGroupId}", userId, mailGroupId);
        }
    }

    public async Task RevokeUserAccess(long mailGroupId, Guid userId)
    {
        using var db = await _db.GetConnectionAsync();

        var existingAccess = await db.SingleAsync<MailGroupUser>(mgu => mgu.MailGroupId == mailGroupId && mgu.UserId == userId);

        if (existingAccess != null)
        {
            await db.DeleteAsync(existingAccess);

            _logger.LogInformation("Revoked user {UserId} access from mail group {MailGroupId}", userId, mailGroupId);
        }
    }

    public async Task<MailGroup> CreateUserPrivateMailGroup(Guid userId, bool isDefault = false)
    {
        using var db = await _db.GetConnectionAsync();

        var user = await db.SingleByIdAsync<User>(userId);
        if (user == null)
        {
            throw new InvalidOperationException($"User {userId} not found");
        }

        var path = MailGroup.GetUserPrivatePath(user.UserName);
        var subdomain = user.UserName; // Use username as subdomain

        var privateMailGroup = new MailGroup
        {
            Path = path,
            Subdomain = subdomain,
            Description = $"Private mailbox for {user.UserName}",
            OwnerUserId = userId,
            IsPublic = false,
            IsUserPrivate = true,
            IsDefaultMailbox = isDefault, // Mark as default only if specified
            CreatedAt = DateTime.UtcNow
        };

        await db.InsertAsync(privateMailGroup);

        _logger.LogInformation("Created private mail group for user {UserId} with subdomain {Subdomain}", userId, subdomain);
        return privateMailGroup;
    }

    public async Task<List<MailGroup>> GetUserPrivateMailGroups(Guid userId)
    {
        using var db = await _db.GetConnectionAsync();
        return await db.SelectAsync<MailGroup>(mg => mg.OwnerUserId == userId && mg.IsUserPrivate);
    }
}

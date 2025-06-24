using MailVoidApi.Data;
using MailVoidWeb;
using Microsoft.EntityFrameworkCore;

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
    private readonly MailVoidDbContext _context;
    
    public MailGroupService(ILogger<MailGroupService> logger, MailVoidDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task SetMailPath(Mail m)
    {
        // Extract subdomain to check if this is a user's private mailbox
        var subdomain = EmailSubdomainHelper.ExtractSubdomain(m.To);
        
        // Check if this email belongs to a user's private mailbox (subdomain matches username)
        var privateMailGroup = await _context.MailGroups
            .FirstOrDefaultAsync(mg => mg.IsUserPrivate && mg.Subdomain == subdomain);

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
        
        var existingGroup = await _context.MailGroups
            .FirstOrDefaultAsync(mg => mg.Subdomain == subdomain);

        if (existingGroup != null)
        {
            return existingGroup;
        }

        // Create new mail group with admin user as owner if provided
        var adminUser = userId.HasValue ? await _context.Users.FirstAsync(u => u.Id == userId.Value) :
                       await _context.Users.FirstAsync(u => u.UserName == "admin");

        var newGroup = new MailGroup
        {
            Path = path,
            Subdomain = subdomain,
            OwnerUserId = adminUser.Id,
            IsPublic = true,
            Description = $"Auto-generated group for {subdomain} subdomain"
        };

        _context.MailGroups.Add(newGroup);
        await _context.SaveChangesAsync();

        _logger.LogInformation($"Created new mail group for subdomain: {subdomain}");
        return newGroup;
    }

    public async Task<bool> HasUserAccess(long mailGroupId, Guid userId)
    {
        var mailGroup = await _context.MailGroups
            .Include(mg => mg.MailGroupUsers)
            .FirstOrDefaultAsync(mg => mg.Id == mailGroupId);

        if (mailGroup == null)
            return false;

        // Owner always has access
        if (mailGroup.OwnerUserId == userId)
            return true;

        // Public groups are accessible to all users
        if (mailGroup.IsPublic)
            return true;

        // Check explicit user access
        return mailGroup.MailGroupUsers.Any(mgu => mgu.UserId == userId);
    }

    public async Task GrantUserAccess(long mailGroupId, Guid userId)
    {
        var existingAccess = await _context.MailGroupUsers
            .FirstOrDefaultAsync(mgu => mgu.MailGroupId == mailGroupId && mgu.UserId == userId);

        if (existingAccess == null)
        {
            var mailGroupUser = new MailGroupUser
            {
                MailGroupId = mailGroupId,
                UserId = userId
            };

            _context.MailGroupUsers.Add(mailGroupUser);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation($"Granted user {userId} access to mail group {mailGroupId}");
        }
    }

    public async Task RevokeUserAccess(long mailGroupId, Guid userId)
    {
        var existingAccess = await _context.MailGroupUsers
            .FirstOrDefaultAsync(mgu => mgu.MailGroupId == mailGroupId && mgu.UserId == userId);

        if (existingAccess != null)
        {
            _context.MailGroupUsers.Remove(existingAccess);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation($"Revoked user {userId} access from mail group {mailGroupId}");
        }
    }

    public async Task<MailGroup> CreateUserPrivateMailGroup(Guid userId, bool isDefault = false)
    {
        var user = await _context.Users.FindAsync(userId);
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

        _context.MailGroups.Add(privateMailGroup);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created private mail group for user {UserId} with subdomain {Subdomain}", userId, subdomain);
        return privateMailGroup;
    }

    public async Task<List<MailGroup>> GetUserPrivateMailGroups(Guid userId)
    {
        return await _context.MailGroups
            .Where(mg => mg.OwnerUserId == userId && mg.IsUserPrivate)
            .OrderBy(mg => mg.Subdomain)
            .ToListAsync();
    }

}

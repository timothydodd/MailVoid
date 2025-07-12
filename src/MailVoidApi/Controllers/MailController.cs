using MailVoidApi.Common;
using MailVoidApi.Data;
using MailVoidApi.Services;
using MailVoidWeb;
using MailVoidWeb.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace MailVoidApi.Controllers;
[Authorize]
[ApiController]
[Route("api/mail")]
public class MailController : ControllerBase
{
    private readonly ILogger<MailController> _logger;
    private readonly MailVoidDbContext _context;
    private readonly IMailGroupService _mailGroupService;
    private readonly IUserService _userService;

    public MailController(ILogger<MailController> logger, MailVoidDbContext context, IMailGroupService mailGroupService, IUserService userService)
    {
        _logger = logger;
        _context = context;
        _mailGroupService = mailGroupService;
        _userService = userService;
    }
    [HttpGet("{id}")]
    public async Task<IActionResult> GetMail(long id, [FromQuery] bool markAsRead = false)
    {
        var email = await _context.Mails.FindAsync(id);
        if (email == null)
        {
            return NotFound();
        }

        if (markAsRead)
        {
            var currentUserId = _userService.GetUserId();

            // Use raw SQL to perform "INSERT IGNORE" or "INSERT ON DUPLICATE KEY" equivalent
            // This is a single database operation that handles the check and insert atomically
            await _context.Database.ExecuteSqlRawAsync(
                @"INSERT IGNORE INTO UserMailRead (UserId, MailId, ReadAt) 
                  VALUES ({0}, {1}, {2})",
                currentUserId, id, DateTime.UtcNow);
        }

        return Ok(email);
    }

    [HttpGet("boxes")]
    public async Task<IEnumerable<MailBox>> GetBoxes(bool showAll = false)
    {
        var currentUserId = _userService.GetUserId();
        var role = _userService.GetRole();
        var isAdmin = role == "Admin";

        // Get distinct mailboxes (by To address) with their mail group info
        var mailboxes = await _context.Mails
            .GroupJoin(_context.MailGroups,
                       mail => mail.MailGroupPath,
                       mailGroup => mailGroup.Path,
                       (mail, mailGroups) => new
                       {
                           mail.To,
                           mail.MailGroupPath,
                           MailGroup = mailGroups.FirstOrDefault()
                       })
            .Where(x => (isAdmin && showAll) ||
                        (x.MailGroup != null &&
                         (x.MailGroup.IsPublic ||
                          x.MailGroup.OwnerUserId == currentUserId ||
                          x.MailGroup.MailGroupUsers != null && x.MailGroup.MailGroupUsers.Any(mgu => mgu.UserId == currentUserId))) ||
                        (x.MailGroup == null && isAdmin))
            .GroupBy(x => x.To)
            .Select(g => g.First())
            .ToListAsync();

        // For each mailbox, calculate unread count
        var result = new List<MailBox>();
        foreach (var mailbox in mailboxes)
        {
            // Count unread emails for this mailbox (emails not in UserMailRead for current user)
            var unreadCount = await _context.Mails
                .Where(m => m.To == mailbox.To)
                .Where(m => !_context.UserMailReads.Any(umr => umr.MailId == m.Id && umr.UserId == currentUserId))
                .CountAsync();

            result.Add(new MailBox()
            {
                Name = mailbox.To,
                Path = mailbox.MailGroupPath,
                MailBoxName = mailbox.MailGroup?.Subdomain,
                IsPublic = mailbox.MailGroup?.IsPublic ?? false,
                IsOwner = mailbox.MailGroup != null && (mailbox.MailGroup.OwnerUserId == currentUserId),
                UnreadCount = unreadCount
            });
        }

        return result;
    }
    [HttpPost]
    public async Task<PagedResults<MailWithReadStatus>> GetMails([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FilterOptions? options = null)
    {
        var currentUserId = _userService.GetUserId();
        var role = _userService.GetRole();
        var isAdmin = role == "Admin";

        var results = new PagedResults<MailWithReadStatus>();
        options ??= new FilterOptions();

        var query = _context.Mails
                    .GroupJoin(_context.MailGroups,
                       mail => mail.MailGroupPath,
                       mailGroup => mailGroup.Path,
                       (mail, mailGroups) => new
                       {
                           Mail = mail,
                           MailGroup = mailGroups.FirstOrDefault()
                       })
            .AsQueryable();

        if (!string.IsNullOrEmpty(options.To))
        {
            query = query.Where(m => m.Mail.To == options.To);
        }

        if (options.PageSize == 1)
        {
            results.TotalCount = await query.CountAsync();
        }

        query = query.OrderByDescending(m => m.Mail.CreatedOn);
        var mails = await query
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .Select(m => m.Mail)
            .ToListAsync();

        // Get read status for each mail
        var mailIds = mails.Select(m => m.Id).ToList();
        var readMails = await _context.UserMailReads
            .Where(umr => umr.UserId == currentUserId && mailIds.Contains(umr.MailId))
            .Select(umr => umr.MailId)
            .ToListAsync();

        results.Items = mails.Select(mail => new MailWithReadStatus
        {
            Id = mail.Id,
            To = mail.To,
            From = mail.From,
            Subject = mail.Subject,
            Text = mail.IsHtml ? null : mail.Text,
            Html = mail.IsHtml ? mail.Text : null,
            CreatedOn = mail.CreatedOn,
            MailGroupPath = mail.MailGroupPath,
            IsRead = readMails.Contains(mail.Id)
        }).ToList();

        return results;
    }
    [HttpDelete("boxes")]
    public async Task<IActionResult> DeleteBox([FromBody] FilterOptions options)
    {
        if (options == null || string.IsNullOrEmpty(options.To))
            return BadRequest();

        var mailsToDelete = await _context.Mails
            .Where(m => m.To == options.To)
            .ToListAsync();

        _context.Mails.RemoveRange(mailsToDelete);
        await _context.SaveChangesAsync();

        return Ok();
    }
    [HttpGet("groups")]
    public async Task<IActionResult> GetMailGroups()
    {
        var userId = _userService.GetUserId();
        var groups = await _context.MailGroups
            .Where(mg => mg.Subdomain != null && !mg.IsUserPrivate && (mg.IsPublic || mg.OwnerUserId == userId ||
                        mg.MailGroupUsers.Any(mgu => mgu.UserId == userId)))
            .Select(mg => new
            {
                mg.Id,
                Path = mg.Path ?? "",
                Subdomain = mg.Subdomain ?? "",
                Description = mg.Description ?? "",
                mg.IsPublic,
                mg.CreatedAt,
                mg.LastActivity,
                IsOwner = mg.OwnerUserId == userId,
                mg.IsUserPrivate
            })
            .ToListAsync();
        return Ok(groups);
    }

    [HttpPost("groups/{mailGroupId}/access")]
    public async Task<IActionResult> GrantAccess(long mailGroupId, [FromBody] GrantAccessRequest request)
    {
        var currentUserId = _userService.GetUserId();

        // Check if current user is owner or admin editing public group
        var mailGroup = await _context.MailGroups.FindAsync(mailGroupId);
        if (mailGroup == null)
            return NotFound();

        var isOwner = mailGroup.OwnerUserId == currentUserId;
        var isAdminEditingPublic = User.IsInRole("Admin") && mailGroup.IsPublic;

        if (!isOwner && !isAdminEditingPublic)
            return Forbid();

        // Prevent sharing private user mailboxes
        if (mailGroup.IsUserPrivate)
            return BadRequest(new { message = "Private user mailboxes cannot be shared." });

        await _mailGroupService.GrantUserAccess(mailGroupId, request.UserId);
        return Ok();
    }

    [HttpDelete("groups/{mailGroupId}/access/{userId}")]
    public async Task<IActionResult> RevokeAccess(long mailGroupId, Guid userId)
    {
        var currentUserId = _userService.GetUserId();

        // Check if current user is owner or admin editing public group
        var mailGroup = await _context.MailGroups.FindAsync(mailGroupId);
        if (mailGroup == null)
            return NotFound();

        var isOwner = mailGroup.OwnerUserId == currentUserId;
        var isAdminEditingPublic = User.IsInRole("Admin") && mailGroup.IsPublic;

        if (!isOwner && !isAdminEditingPublic)
            return Forbid();

        // Prevent modifying access to private user mailboxes
        if (mailGroup.IsUserPrivate)
            return BadRequest(new { message = "Private user mailboxes cannot have access modified." });

        await _mailGroupService.RevokeUserAccess(mailGroupId, userId);
        return Ok();
    }

    [HttpPut("groups/{id}")]
    public async Task<IActionResult> UpdateMailGroup(long id, [FromBody] UpdateMailGroupRequest request)
    {
        var currentUserId = _userService.GetUserId();

        var mailGroup = await _context.MailGroups.FindAsync(id);
        if (mailGroup == null)
            return NotFound();

        // Allow owner or admin to edit public groups
        var isOwner = mailGroup.OwnerUserId == currentUserId;
        var isAdminEditingPublic = User.IsInRole("Admin") && mailGroup.IsPublic;

        if (!isOwner && !isAdminEditingPublic)
            return Forbid();

        // Prevent modifying private user mailboxes public status
        if (mailGroup.IsUserPrivate)
        {
            // Only allow description updates for private mailboxes
            mailGroup.Description = request.Description;
        }
        else
        {
            mailGroup.Description = request.Description;
            mailGroup.IsPublic = request.IsPublic;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            mailGroup.Id,
            Path = mailGroup.Path ?? "",
            Subdomain = mailGroup.Subdomain ?? "",
            Description = mailGroup.Description ?? "",
            mailGroup.IsPublic,
            mailGroup.CreatedAt,
            IsOwner = isOwner,
            mailGroup.IsUserPrivate
        });
    }

    [HttpGet("groups/{mailGroupId}/users")]
    public async Task<IActionResult> GetMailGroupUsers(long mailGroupId)
    {
        var currentUserId = _userService.GetUserId();

        var mailGroup = await _context.MailGroups.FindAsync(mailGroupId);
        if (mailGroup == null)
            return NotFound();

        var isOwner = mailGroup.OwnerUserId == currentUserId;
        var isAdminEditingPublic = User.IsInRole("Admin") && mailGroup.IsPublic;

        if (!isOwner && !isAdminEditingPublic)
            return Forbid();

        var groupUsers = await _context.MailGroupUsers
            .Include(mgu => mgu.User)
            .Where(mgu => mgu.MailGroupId == mailGroupId)
            .Select(mgu => new
            {
                mgu.Id,
                mgu.MailGroupId,
                mgu.UserId,
                mgu.GrantedAt,
                User = new
                {
                    mgu.User.Id,
                    mgu.User.UserName,
                    mgu.User.Role,
                    mgu.User.TimeStamp
                }
            })
            .ToListAsync();

        return Ok(groupUsers);
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _context.Users
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.Role,
                u.TimeStamp
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost("mail-groups")]
    public async Task<IActionResult> CreateMailGroup([FromBody] CreateMailGroupRequest request)
    {
        var userId = _userService.GetUserId();

        // Check if subdomain already exists
        var existingGroup = await _context.MailGroups
            .FirstOrDefaultAsync(mg => mg.Subdomain == request.Subdomain);

        if (existingGroup != null)
        {
            return BadRequest(new { message = "A mail group with this subdomain already exists." });
        }

        var mailGroup = new MailGroup
        {
            Path = EmailSubdomainHelper.GenerateMailGroupPath(request.Subdomain),
            Subdomain = request.Subdomain,
            Description = request.Description ?? $"Mail group for {request.Subdomain}",
            OwnerUserId = userId,
            IsPublic = request.IsPublic,
            IsUserPrivate = false,
            IsDefaultMailbox = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.MailGroups.Add(mailGroup);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            id = mailGroup.Id,
            path = mailGroup.Path,
            subdomain = mailGroup.Subdomain,
            description = mailGroup.Description,
            isPublic = mailGroup.IsPublic,
            createdAt = mailGroup.CreatedAt
        });
    }

    [HttpDelete("mail-groups/{id}")]
    public async Task<IActionResult> DeleteMailGroup(long id)
    {
        var userId = _userService.GetUserId();
        var mailGroup = await _context.MailGroups.FindAsync(id);

        if (mailGroup == null)
        {
            return NotFound();
        }

        // Prevent deleting default mailboxes
        if (mailGroup.IsDefaultMailbox)
        {
            return BadRequest(new { message = "Cannot delete default mailbox." });
        }

        // Only owner or admin can delete
        if (mailGroup.OwnerUserId != userId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        _context.MailGroups.Remove(mailGroup);
        await _context.SaveChangesAsync();

        return Ok();
    }

    [HttpGet("mailbox/{id}/retention")]
    public async Task<IActionResult> GetRetentionSettings(long id)
    {
        var userId = _userService.GetUserId();
        var mailGroup = await _context.MailGroups.FindAsync(id);
        
        if (mailGroup == null)
        {
            return NotFound();
        }

        // Check if user has access to this mailbox
        if (!mailGroup.IsPublic && 
            mailGroup.OwnerUserId != userId && 
            !await _context.MailGroupUsers.AnyAsync(mgu => mgu.MailGroupId == id && mgu.UserId == userId))
        {
            return Forbid();
        }

        return Ok(new RetentionSettingsResponse
        {
            MailGroupId = mailGroup.Id,
            RetentionDays = mailGroup.RetentionDays,
            Path = mailGroup.Path
        });
    }

    [HttpPut("mailbox/{id}/retention")]
    public async Task<IActionResult> UpdateRetentionSettings(long id, [FromBody] UpdateRetentionRequest request)
    {
        var userId = _userService.GetUserId();
        var mailGroup = await _context.MailGroups.FindAsync(id);
        
        if (mailGroup == null)
        {
            return NotFound();
        }

        // Only the owner can update retention settings
        if (mailGroup.OwnerUserId != userId)
        {
            return Forbid();
        }

        // Validate retention days (0-365)
        if (request.RetentionDays.HasValue && (request.RetentionDays < 0 || request.RetentionDays > 365))
        {
            return BadRequest(new { message = "Retention days must be between 0 and 365." });
        }

        mailGroup.RetentionDays = request.RetentionDays;
        await _context.SaveChangesAsync();

        _logger.LogInformation($"Updated retention settings for mailbox {mailGroup.Path} to {request.RetentionDays} days");

        return Ok(new RetentionSettingsResponse
        {
            MailGroupId = mailGroup.Id,
            RetentionDays = mailGroup.RetentionDays,
            Path = mailGroup.Path
        });
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllAsRead([FromBody] MarkAllAsReadRequest request)
    {
        var userId = _userService.GetUserId();
        
        // Get emails for the specified mailbox that are accessible to the user
        var emailsQuery = _context.Mails.AsQueryable();
        
        if (!string.IsNullOrEmpty(request.MailboxPath))
        {
            emailsQuery = emailsQuery.Where(m => m.MailGroupPath == request.MailboxPath);
            
            // Check if user has access to this mailbox
            var mailGroup = await _context.MailGroups
                .FirstOrDefaultAsync(mg => mg.Path == request.MailboxPath);
                
            if (mailGroup != null)
            {
                var isAdmin = _userService.IsAdmin();
                if (!mailGroup.IsPublic && 
                    mailGroup.OwnerUserId != userId && 
                    !await _context.MailGroupUsers.AnyAsync(mgu => mgu.MailGroupId == mailGroup.Id && mgu.UserId == userId) &&
                    !isAdmin)
                {
                    return Forbid();
                }
            }
        }
        
        var emailIds = await emailsQuery.Select(m => m.Id).ToListAsync();
        
        if (!emailIds.Any())
        {
            return Ok(new { message = "No emails found to mark as read.", markedCount = 0 });
        }
        
        // Get emails that are not already marked as read by this user
        var alreadyReadIds = await _context.UserMailReads
            .Where(r => r.UserId == userId && emailIds.Contains(r.MailId))
            .Select(r => r.MailId)
            .ToListAsync();
            
        var unreadEmailIds = emailIds.Except(alreadyReadIds).ToList();
        
        if (!unreadEmailIds.Any())
        {
            return Ok(new { message = "All emails are already marked as read.", markedCount = 0 });
        }
        
        // Create read records for unread emails
        var readRecords = unreadEmailIds.Select(emailId => new UserMailRead
        {
            UserId = userId,
            MailId = emailId,
            ReadAt = DateTime.UtcNow
        }).ToList();
        
        _context.UserMailReads.AddRange(readRecords);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation(
            "User {UserId} marked {Count} emails as read in mailbox '{MailboxPath}'",
            userId, unreadEmailIds.Count, request.MailboxPath ?? "all");
        
        return Ok(new { message = $"Marked {unreadEmailIds.Count} emails as read.", markedCount = unreadEmailIds.Count });
    }

}

public record GrantAccessRequest
{
    public required Guid UserId { get; set; }
}

public record UpdateMailGroupRequest
{
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
}

public record CreateMailGroupRequest
{
    public required string Subdomain { get; set; }
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
}
public class FilterOptions
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? To { get; set; }
}
public class MailBox
{
    public string? Path { get; set; }
    public required string Name { get; set; }
    public string? MailBoxName { get; set; }
    public bool IsPublic { get; set; }
    public bool IsOwner { get; set; }
    public int UnreadCount { get; set; }
}

public class MailWithReadStatus
{
    public long Id { get; set; }
    public required string To { get; set; }
    public required string From { get; set; }
    public required string Subject { get; set; }
    public string? Text { get; set; }
    public string? Html { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? MailGroupPath { get; set; }
    public bool IsRead { get; set; }
}

public record UpdateRetentionRequest
{
    public int? RetentionDays { get; set; }
}

public record RetentionSettingsResponse
{
    public long MailGroupId { get; set; }
    public int? RetentionDays { get; set; }
    public string? Path { get; set; }
}

public record MarkAllAsReadRequest
{
    public string? MailboxPath { get; set; }
}

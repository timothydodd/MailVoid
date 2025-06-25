using MailVoidApi.Common;
using MailVoidApi.Data;
using MailVoidApi.Services;
using MailVoidWeb;
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
    public async Task<IActionResult> GetMail(long id)
    {
        var email = await _context.Mails.FindAsync(id);
        if (email == null)
        {
            return NotFound();
        }
        return Ok(email);
    }
    [HttpGet("boxes")]
    public async Task<IEnumerable<MailBox>> GetBoxes()
    {
        var boxes = await _context.Mails
            .Select(m => new { m.To, m.MailGroupPath })
            .Distinct()
            .ToListAsync();
            
        return boxes.Select(x => new MailBox()
        {
            Name = x.To,
            Path = x.MailGroupPath
        });
    }
    [HttpPost]
    public async Task<PagedResults<Mail>> GetMails([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FilterOptions? options = null)
    {
        var results = new PagedResults<Mail>();
        options ??= new FilterOptions();

        var query = _context.Mails.AsQueryable();

        if (!string.IsNullOrEmpty(options.To))
        {
            query = query.Where(m => m.To == options.To);
        }

        if (options.PageSize == 1)
        {
            results.TotalCount = await query.CountAsync();
        }

        query = query.OrderByDescending(m => m.CreatedOn);
        results.Items = await query
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .ToListAsync();

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
            .Select(mg => new { 
                mg.Id, 
                Path = mg.Path ?? "",
                Subdomain = mg.Subdomain ?? "",
                Description = mg.Description ?? "",
                mg.IsPublic, 
                mg.CreatedAt,
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
        
        return Ok(new {
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
            .Select(mgu => new {
                mgu.Id,
                mgu.MailGroupId,
                mgu.UserId,
                mgu.GrantedAt,
                User = new {
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
            .Select(u => new {
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
        
        return Ok(new {
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

}

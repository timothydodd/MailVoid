using Dapper;
using MailVoidApi.Common;
using MailVoidApi.Data;
using MailVoidApi.Services;
using MailVoidWeb;
using MailVoidWeb.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RoboDodd.OrmLite;

namespace MailVoidApi.Controllers;
[Authorize]
[ApiController]
[Route("api/mail")]
public class MailController : ControllerBase
{
    private readonly ILogger<MailController> _logger;
    private readonly IDatabaseService _db;
    private readonly IMailGroupService _mailGroupService;
    private readonly IUserService _userService;

    public MailController(ILogger<MailController> logger, IDatabaseService db, IMailGroupService mailGroupService, IUserService userService)
    {
        _logger = logger;
        _db = db;
        _mailGroupService = mailGroupService;
        _userService = userService;
    }
    [HttpGet("{id}")]
    public async Task<IActionResult> GetMail(long id, [FromQuery] bool markAsRead = false)
    {
        using var db = await _db.GetConnectionAsync();
        var email = await db.SingleByIdAsync<Mail>(id);
        if (email == null)
        {
            return NotFound();
        }

        if (markAsRead)
        {
            var currentUserId = _userService.GetUserId();

            // Use raw SQL to perform "INSERT IGNORE"
            await db.ExecuteAsync(
                @"INSERT IGNORE INTO UserMailRead (UserId, MailId, ReadAt)
                  VALUES (@UserId, @MailId, @ReadAt)",
                new { UserId = currentUserId, MailId = id, ReadAt = DateTime.UtcNow });
        }

        return Ok(email);
    }

    [HttpGet("boxes")]
    public async Task<IEnumerable<MailBox>> GetBoxes(bool showAll = false)
    {
        var currentUserId = _userService.GetUserId();
        var role = _userService.GetRole();
        var isAdmin = role == "Admin";

        using var db = await _db.GetConnectionAsync();

        // Get distinct mailboxes with their mail group info using a simpler approach
        string sql;
        if (isAdmin && showAll)
        {
            sql = @"SELECT DISTINCT m.`To`, m.MailGroupPath, mg.Subdomain, mg.IsPublic, mg.OwnerUserId
                    FROM Mail m
                    LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path";
        }
        else
        {
            sql = @"SELECT DISTINCT m.`To`, m.MailGroupPath, mg.Subdomain, mg.IsPublic, mg.OwnerUserId
                    FROM Mail m
                    LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path
                    WHERE (mg.IsPublic = 1 OR mg.OwnerUserId = @UserId
                           OR EXISTS (SELECT 1 FROM MailGroupUser mgu WHERE mgu.MailGroupId = mg.Id AND mgu.UserId = @UserId))
                           OR (mg.Id IS NULL AND @IsAdmin = 1)";
        }

        var mailboxes = await db.QueryAsync<dynamic>(sql, new { UserId = currentUserId, IsAdmin = isAdmin });

        var result = new List<MailBox>();
        foreach (var mailbox in mailboxes)
        {
            string toAddress = mailbox.To;
            // Count unread emails for this mailbox
            var unreadCount = await db.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM Mail m
                  WHERE m.`To` = @To
                  AND NOT EXISTS (SELECT 1 FROM UserMailRead umr WHERE umr.MailId = m.Id AND umr.UserId = @UserId)",
                new { To = toAddress, UserId = currentUserId });

            result.Add(new MailBox()
            {
                Name = toAddress,
                Path = mailbox.MailGroupPath,
                MailBoxName = mailbox.Subdomain,
                IsPublic = Convert.ToBoolean(mailbox.IsPublic),
                IsOwner = mailbox.OwnerUserId != null && (Guid)mailbox.OwnerUserId == currentUserId,
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

        using var db = await _db.GetConnectionAsync();

        var whereClauses = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("UserId", currentUserId);

        if (!string.IsNullOrEmpty(options.To))
        {
            whereClauses.Add("m.`To` = @To");
            parameters.Add("To", options.To);
        }

        var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        if (options.PageSize == 1)
        {
            results.TotalCount = await db.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM Mail m {whereClause}", parameters);
        }

        var offset = (options.Page - 1) * options.PageSize;
        var mails = await db.QueryAsync<Mail>(
            $"SELECT * FROM Mail m {whereClause} ORDER BY CreatedOn DESC LIMIT @Limit OFFSET @Offset",
            new { Limit = options.PageSize, Offset = offset, To = options.To, UserId = currentUserId });

        var mailList = mails.ToList();
        var mailIds = mailList.Select(m => m.Id).ToList();

        // Get read status for each mail
        var readMails = new List<long>();
        if (mailIds.Any())
        {
            readMails = (await db.QueryAsync<long>(
                @"SELECT MailId FROM UserMailRead WHERE UserId = @UserId AND MailId IN @MailIds",
                new { UserId = currentUserId, MailIds = mailIds })).ToList();
        }

        results.Items = mailList.Select(mail => new MailWithReadStatus
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

        using var db = await _db.GetConnectionAsync();
        var mailsToDelete = await db.SelectAsync<Mail>(m => m.To == options.To);

        foreach (var mail in mailsToDelete)
        {
            await db.DeleteAsync(mail);
        }

        return Ok();
    }

    [HttpGet("groups")]
    public async Task<IActionResult> GetMailGroups()
    {
        var userId = _userService.GetUserId();
        using var db = await _db.GetConnectionAsync();

        var groups = await db.QueryAsync<dynamic>(
            @"SELECT mg.Id, mg.Path, mg.Subdomain, mg.Description, mg.IsPublic, mg.CreatedAt, mg.LastActivity, mg.OwnerUserId, mg.IsUserPrivate
              FROM MailGroup mg
              WHERE mg.Subdomain IS NOT NULL AND mg.IsUserPrivate = 0
              AND (mg.IsPublic = 1 OR mg.OwnerUserId = @UserId
                   OR EXISTS (SELECT 1 FROM MailGroupUser mgu WHERE mgu.MailGroupId = mg.Id AND mgu.UserId = @UserId))",
            new { UserId = userId });

        var result = groups.Select(mg => new
        {
            Id = (long)mg.Id,
            Path = mg.Path ?? "",
            Subdomain = mg.Subdomain ?? "",
            Description = mg.Description ?? "",
            IsPublic = Convert.ToBoolean(mg.IsPublic),
            CreatedAt = (DateTime)mg.CreatedAt,
            LastActivity = mg.LastActivity as DateTime?,
            IsOwner = mg.OwnerUserId != null && (Guid)mg.OwnerUserId == userId,
            IsUserPrivate = Convert.ToBoolean(mg.IsUserPrivate)
        }).ToList();

        return Ok(result);
    }

    [HttpPost("groups/{mailGroupId}/access")]
    public async Task<IActionResult> GrantAccess(long mailGroupId, [FromBody] GrantAccessRequest request)
    {
        var currentUserId = _userService.GetUserId();

        using var db = await _db.GetConnectionAsync();
        var mailGroup = await db.SingleByIdAsync<MailGroup>(mailGroupId);
        if (mailGroup == null)
            return NotFound();

        var isOwner = mailGroup.OwnerUserId == currentUserId;
        var isAdminEditingPublic = User.IsInRole("Admin") && mailGroup.IsPublic;

        if (!isOwner && !isAdminEditingPublic)
            return Forbid();

        if (mailGroup.IsUserPrivate)
            return BadRequest(new { message = "Private user mailboxes cannot be shared." });

        await _mailGroupService.GrantUserAccess(mailGroupId, request.UserId);
        return Ok();
    }

    [HttpDelete("groups/{mailGroupId}/access/{userId}")]
    public async Task<IActionResult> RevokeAccess(long mailGroupId, Guid userId)
    {
        var currentUserId = _userService.GetUserId();

        using var db = await _db.GetConnectionAsync();
        var mailGroup = await db.SingleByIdAsync<MailGroup>(mailGroupId);
        if (mailGroup == null)
            return NotFound();

        var isOwner = mailGroup.OwnerUserId == currentUserId;
        var isAdminEditingPublic = User.IsInRole("Admin") && mailGroup.IsPublic;

        if (!isOwner && !isAdminEditingPublic)
            return Forbid();

        if (mailGroup.IsUserPrivate)
            return BadRequest(new { message = "Private user mailboxes cannot have access modified." });

        await _mailGroupService.RevokeUserAccess(mailGroupId, userId);
        return Ok();
    }

    [HttpPut("groups/{id}")]
    public async Task<IActionResult> UpdateMailGroup(long id, [FromBody] UpdateMailGroupRequest request)
    {
        var currentUserId = _userService.GetUserId();

        using var db = await _db.GetConnectionAsync();
        var mailGroup = await db.SingleByIdAsync<MailGroup>(id);
        if (mailGroup == null)
            return NotFound();

        var isOwner = mailGroup.OwnerUserId == currentUserId;
        var isAdminEditingPublic = User.IsInRole("Admin") && mailGroup.IsPublic;

        if (!isOwner && !isAdminEditingPublic)
            return Forbid();

        if (mailGroup.IsUserPrivate)
        {
            mailGroup.Description = request.Description;
        }
        else
        {
            mailGroup.Description = request.Description;
            mailGroup.IsPublic = request.IsPublic;
        }

        await db.UpdateAsync(mailGroup);

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

        using var db = await _db.GetConnectionAsync();
        var mailGroup = await db.SingleByIdAsync<MailGroup>(mailGroupId);
        if (mailGroup == null)
            return NotFound();

        var isOwner = mailGroup.OwnerUserId == currentUserId;
        var isAdminEditingPublic = User.IsInRole("Admin") && mailGroup.IsPublic;

        if (!isOwner && !isAdminEditingPublic)
            return Forbid();

        var groupUsers = await db.QueryAsync<dynamic>(
            @"SELECT mgu.Id, mgu.MailGroupId, mgu.UserId, mgu.GrantedAt,
                     u.Id as User_Id, u.UserName as User_UserName, u.Role as User_Role, u.TimeStamp as User_TimeStamp
              FROM MailGroupUser mgu
              INNER JOIN User u ON mgu.UserId = u.Id
              WHERE mgu.MailGroupId = @MailGroupId",
            new { MailGroupId = mailGroupId });

        var result = groupUsers.Select(mgu => new
        {
            Id = (long)mgu.Id,
            MailGroupId = (long)mgu.MailGroupId,
            UserId = (Guid)mgu.UserId,
            GrantedAt = (DateTime)mgu.GrantedAt,
            User = new
            {
                Id = (Guid)mgu.User_Id,
                UserName = (string)mgu.User_UserName,
                Role = (Role)mgu.User_Role,
                TimeStamp = (DateTime)mgu.User_TimeStamp
            }
        }).ToList();

        return Ok(result);
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        using var db = await _db.GetConnectionAsync();
        var users = await db.SelectAsync<User>();

        var result = users.Select(u => new
        {
            u.Id,
            u.UserName,
            u.Role,
            u.TimeStamp
        }).ToList();

        return Ok(result);
    }

    [HttpPost("mail-groups")]
    public async Task<IActionResult> CreateMailGroup([FromBody] CreateMailGroupRequest request)
    {
        var userId = _userService.GetUserId();

        using var db = await _db.GetConnectionAsync();
        var existingGroup = await db.SingleAsync<MailGroup>(mg => mg.Subdomain == request.Subdomain);

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

        await db.InsertAsync(mailGroup);

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
        using var db = await _db.GetConnectionAsync();
        var mailGroup = await db.SingleByIdAsync<MailGroup>(id);

        if (mailGroup == null)
        {
            return NotFound();
        }

        if (mailGroup.IsDefaultMailbox)
        {
            return BadRequest(new { message = "Cannot delete default mailbox." });
        }

        if (mailGroup.OwnerUserId != userId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        await db.DeleteAsync(mailGroup);

        return Ok();
    }

    [HttpGet("mailbox/{id}/retention")]
    public async Task<IActionResult> GetRetentionSettings(long id)
    {
        var userId = _userService.GetUserId();
        using var db = await _db.GetConnectionAsync();
        var mailGroup = await db.SingleByIdAsync<MailGroup>(id);

        if (mailGroup == null)
        {
            return NotFound();
        }

        if (!mailGroup.IsPublic &&
            mailGroup.OwnerUserId != userId &&
            !await db.ExistsAsync<MailGroupUser>(mgu => mgu.MailGroupId == id && mgu.UserId == userId))
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
        using var db = await _db.GetConnectionAsync();
        var mailGroup = await db.SingleByIdAsync<MailGroup>(id);

        if (mailGroup == null)
        {
            return NotFound();
        }

        if (mailGroup.OwnerUserId != userId)
        {
            return Forbid();
        }

        if (request.RetentionDays.HasValue && (request.RetentionDays < 0 || request.RetentionDays > 365))
        {
            return BadRequest(new { message = "Retention days must be between 0 and 365." });
        }

        mailGroup.RetentionDays = request.RetentionDays;
        await db.UpdateAsync(mailGroup);

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

        using var db = await _db.GetConnectionAsync();

        List<long> emailIds;
        if (!string.IsNullOrEmpty(request.MailboxPath))
        {
            // Check if user has access to this mailbox
            var mailGroup = await db.SingleAsync<MailGroup>(mg => mg.Path == request.MailboxPath);

            if (mailGroup != null)
            {
                var isAdmin = _userService.IsAdmin();
                if (!mailGroup.IsPublic &&
                    mailGroup.OwnerUserId != userId &&
                    !await db.ExistsAsync<MailGroupUser>(mgu => mgu.MailGroupId == mailGroup.Id && mgu.UserId == userId) &&
                    !isAdmin)
                {
                    return Forbid();
                }
            }

            emailIds = (await db.QueryAsync<long>(
                "SELECT Id FROM Mail WHERE MailGroupPath = @MailboxPath",
                new { request.MailboxPath })).ToList();
        }
        else
        {
            emailIds = (await db.QueryAsync<long>("SELECT Id FROM Mail")).ToList();
        }

        if (!emailIds.Any())
        {
            return Ok(new { message = "No emails found to mark as read.", markedCount = 0 });
        }

        // Get emails that are not already marked as read by this user
        var alreadyReadIds = (await db.QueryAsync<long>(
            "SELECT MailId FROM UserMailRead WHERE UserId = @UserId AND MailId IN @EmailIds",
            new { UserId = userId, EmailIds = emailIds })).ToList();

        var unreadEmailIds = emailIds.Except(alreadyReadIds).ToList();

        if (!unreadEmailIds.Any())
        {
            return Ok(new { message = "All emails are already marked as read.", markedCount = 0 });
        }

        // Create read records for unread emails
        foreach (var emailId in unreadEmailIds)
        {
            var readRecord = new UserMailRead
            {
                UserId = userId,
                MailId = emailId,
                ReadAt = DateTime.UtcNow
            };
            await db.InsertAsync(readRecord);
        }

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

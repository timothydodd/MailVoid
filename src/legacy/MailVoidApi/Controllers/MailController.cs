using System.Data.Common;
using System.Globalization;
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

    // SQLite returns dates and GUIDs from dynamic queries as strings; MySQL returns native types
    private static DateTime AsDateTime(object? value) => value switch
    {
        DateTime dt => dt,
        string s => DateTime.Parse(s, CultureInfo.InvariantCulture),
        _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
    };

    private static DateTime? AsNullableDateTime(object? value) => value == null ? null : AsDateTime(value);

    private static Guid? AsNullableGuid(object? value) => value switch
    {
        null => null,
        Guid g => g,
        string s => Guid.Parse(s),
        _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to Guid")
    };

    private static Guid AsGuid(object? value) => AsNullableGuid(value)
        ?? throw new InvalidCastException("Unexpected null GUID value");

    // Shared visibility predicate for mail. Requires a Mail alias `m` LEFT JOINed to MailGroup alias `mg`,
    // and @UserId / @UserSubdomain / @IsAdmin parameters. Admins may additionally see orphaned mail
    // (mail whose MailGroup was deleted, so mg.Id IS NULL).
    private const string MailVisibilityClause = @"(
        mg.OwnerUserId = @UserId
        OR mg.Subdomain = @UserSubdomain
        OR EXISTS (SELECT 1 FROM MailGroupUser mgu WHERE mgu.MailGroupId = mg.Id AND mgu.UserId = @UserId)
        OR (@IsAdmin = 1 AND mg.Subdomain = 'default')
        OR (@IsAdmin = 1 AND mg.Id IS NULL)
    )";

    private async Task<bool> CanAccessMailAsync(DbConnection db, long mailId)
    {
        var count = await db.ExecuteScalarAsync<int>(
            $@"SELECT COUNT(*) FROM Mail m
               LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path
               WHERE m.Id = @MailId AND {MailVisibilityClause}",
            new
            {
                MailId = mailId,
                UserId = _userService.GetUserId(),
                UserSubdomain = _userService.GetSubdomain(),
                IsAdmin = _userService.IsAdmin() ? 1 : 0
            });
        return count > 0;
    }

    private async Task<bool> CanAccessMailboxAsync(DbConnection db, string to)
    {
        var count = await db.ExecuteScalarAsync<int>(
            $@"SELECT COUNT(*) FROM Mail m
               LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path
               WHERE m.`To` = @To AND {MailVisibilityClause}",
            new
            {
                To = to,
                UserId = _userService.GetUserId(),
                UserSubdomain = _userService.GetSubdomain(),
                IsAdmin = _userService.IsAdmin() ? 1 : 0
            });
        return count > 0;
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

        if (!await CanAccessMailAsync(db, id))
        {
            // Return NotFound rather than Forbid to avoid disclosing that the mail exists.
            return NotFound();
        }

        if (markAsRead)
        {
            var currentUserId = _userService.GetUserId();

            // Insert-if-not-exists; relies on the unique (UserId, MailId) index
            var insertIgnore = _db.Provider == DatabaseProvider.SQLite ? "INSERT OR IGNORE" : "INSERT IGNORE";
            await db.ExecuteAsync(
                $@"{insertIgnore} INTO UserMailRead (UserId, MailId, ReadAt)
                  VALUES (@UserId, @MailId, @ReadAt)",
                new { UserId = currentUserId, MailId = id, ReadAt = DateTime.UtcNow });
        }

        return Ok(email);
    }

    [HttpGet("boxes")]
    public async Task<IEnumerable<MailBox>> GetBoxes(bool showAll = false)
    {
        var currentUserId = _userService.GetUserId();
        var isAdmin = _userService.IsAdmin();
        var userSubdomain = _userService.GetSubdomain();

        using var db = await _db.GetConnectionAsync();

        // Exclude mailboxes whose group the user has explicitly hidden (per-user opt-out).
        var sql = $@"SELECT DISTINCT m.`To`, m.MailGroupPath, mg.Subdomain, mg.IsPublic, mg.OwnerUserId
                    FROM Mail m
                    LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path
                    WHERE {MailVisibilityClause}
                    AND NOT EXISTS (SELECT 1 FROM HiddenMailGroup h WHERE h.UserId = @UserId AND h.MailGroupId = mg.Id)";

        var mailboxes = await db.QueryAsync<dynamic>(sql, new { UserId = currentUserId, UserSubdomain = userSubdomain, IsAdmin = isAdmin ? 1 : 0 });

        // Single GROUP BY query for unread counts per mailbox instead of one query per mailbox (N+1).
        var unreadRows = await db.QueryAsync<dynamic>(
            @"SELECT m.`To` AS ToAddress, COUNT(*) AS UnreadCount FROM Mail m
              WHERE NOT EXISTS (SELECT 1 FROM UserMailRead umr WHERE umr.MailId = m.Id AND umr.UserId = @UserId)
              GROUP BY m.`To`",
            new { UserId = currentUserId });

        var unreadCounts = new Dictionary<string, int>();
        foreach (var row in unreadRows)
        {
            unreadCounts[(string)row.ToAddress] = Convert.ToInt32(row.UnreadCount);
        }

        var result = new List<MailBox>();
        foreach (var mailbox in mailboxes)
        {
            string toAddress = mailbox.To;

            result.Add(new MailBox()
            {
                Name = toAddress,
                Path = mailbox.MailGroupPath,
                MailBoxName = mailbox.Subdomain,
                IsPublic = Convert.ToBoolean(mailbox.IsPublic),
                IsOwner = AsNullableGuid((object?)mailbox.OwnerUserId) == currentUserId,
                UnreadCount = unreadCounts.TryGetValue(toAddress, out var count) ? count : 0
            });
        }

        return result;
    }

    [HttpPost]
    public async Task<PagedResults<MailWithReadStatus>> GetMails([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FilterOptions? options = null)
    {
        var currentUserId = _userService.GetUserId();
        var isAdmin = _userService.IsAdmin();
        var userSubdomain = _userService.GetSubdomain();

        var results = new PagedResults<MailWithReadStatus>();
        options ??= new FilterOptions();

        // Bound paging inputs.
        if (options.Page < 1) options.Page = 1;
        options.PageSize = Math.Clamp(options.PageSize, 1, 200);

        using var db = await _db.GetConnectionAsync();

        var whereClauses = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("UserId", currentUserId);
        parameters.Add("UserSubdomain", userSubdomain);
        if (!string.IsNullOrEmpty(options.To))
        {
            whereClauses.Add("m.`To` = @To");
            parameters.Add("To", options.To);
        }
        if (!string.IsNullOrEmpty(options.Search))
        {
            whereClauses.Add("(m.Subject LIKE @Search OR m.`From` LIKE @Search OR m.`To` LIKE @Search OR m.Text LIKE @Search)");
            parameters.Add("Search", $"%{options.Search.Trim()}%");
        }

        parameters.Add("IsAdmin", isAdmin ? 1 : 0);

        whereClauses.Add(MailVisibilityClause);

        // Exclude mail in groups the user has hidden (matches the mailbox-list filter).
        whereClauses.Add("NOT EXISTS (SELECT 1 FROM HiddenMailGroup h WHERE h.UserId = @UserId AND h.MailGroupId = mg.Id)");

        var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        // Compute TotalCount on every page, not just the first.
        results.TotalCount = await db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM Mail m LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path {whereClause}", parameters);

        var offset = (options.Page - 1) * options.PageSize;
        parameters.Add("Limit", options.PageSize);
        parameters.Add("Offset", offset);
        var mails = await db.QueryAsync<Mail>(
            $"SELECT m.* FROM Mail m LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path {whereClause} ORDER BY m.CreatedOn DESC LIMIT @Limit OFFSET @Offset",
            parameters);

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
        if (!mailsToDelete.Any())
        {
            return Ok();
        }

        if (!await CanAccessMailboxAsync(db, options.To))
        {
            return Forbid();
        }

        // Single delete; UserMailRead rows are removed by the declared CASCADE.
        await db.DeleteAsync<Mail>(m => m.To == options.To);

        return Ok();
    }

    [HttpGet("groups")]
    public async Task<IActionResult> GetMailGroups()
    {
        var userId = _userService.GetUserId();
        var isAdmin = _userService.IsAdmin();
        var userSubdomain = _userService.GetSubdomain();
        using var db = await _db.GetConnectionAsync();

        var sql = @"SELECT mg.Id, mg.Path, mg.Subdomain, mg.Description, mg.IsPublic, mg.CreatedAt, mg.LastActivity, mg.OwnerUserId, mg.IsUserPrivate,
                        EXISTS (SELECT 1 FROM HiddenMailGroup h WHERE h.UserId = @UserId AND h.MailGroupId = mg.Id) AS IsHidden
                    FROM MailGroup mg
                    WHERE mg.Subdomain IS NOT NULL
                    AND (
                        (mg.IsUserPrivate = 1 AND mg.OwnerUserId = @UserId)
                        OR (mg.IsUserPrivate = 0 AND (
                            mg.OwnerUserId = @UserId
                            OR mg.Subdomain = @UserSubdomain
                            OR EXISTS (SELECT 1 FROM MailGroupUser mgu WHERE mgu.MailGroupId = mg.Id AND mgu.UserId = @UserId)
                            OR (@IsAdmin = 1 AND mg.Subdomain = 'default')
                        ))
                    )";

        var groups = await db.QueryAsync<dynamic>(sql, new { UserId = userId, UserSubdomain = userSubdomain, IsAdmin = isAdmin ? 1 : 0 });

        var result = groups.Select(mg => new
        {
            Id = (long)mg.Id,
            Path = mg.Path ?? "",
            Subdomain = mg.Subdomain ?? "",
            Description = mg.Description ?? "",
            IsPublic = Convert.ToBoolean(mg.IsPublic),
            CreatedAt = AsDateTime((object?)mg.CreatedAt),
            LastActivity = AsNullableDateTime((object?)mg.LastActivity),
            IsOwner = AsNullableGuid((object?)mg.OwnerUserId) == userId,
            IsUserPrivate = Convert.ToBoolean(mg.IsUserPrivate),
            IsHidden = Convert.ToBoolean(mg.IsHidden)
        }).ToList();

        return Ok(result);
    }

    [HttpPost("groups/{id}/hide")]
    public async Task<IActionResult> HideMailGroup(long id)
    {
        var userId = _userService.GetUserId();
        using var db = await _db.GetConnectionAsync();
        var insertIgnore = _db.Provider == DatabaseProvider.SQLite ? "INSERT OR IGNORE" : "INSERT IGNORE";
        await db.ExecuteAsync(
            $@"{insertIgnore} INTO HiddenMailGroup (UserId, MailGroupId, CreatedAt)
               VALUES (@UserId, @MailGroupId, @CreatedAt)",
            new { UserId = userId, MailGroupId = id, CreatedAt = DateTime.UtcNow });
        return Ok();
    }

    [HttpDelete("groups/{id}/hide")]
    public async Task<IActionResult> UnhideMailGroup(long id)
    {
        var userId = _userService.GetUserId();
        using var db = await _db.GetConnectionAsync();
        await db.DeleteAsync<HiddenMailGroup>(h => h.UserId == userId && h.MailGroupId == id);
        return Ok();
    }

    [HttpGet("groups/all")]
    public async Task<IActionResult> GetAllMailGroups()
    {
        if (!_userService.IsAdmin())
            return Forbid();

        var userId = _userService.GetUserId();
        using var db = await _db.GetConnectionAsync();

        var sql = @"SELECT mg.Id, mg.Path, mg.Subdomain, mg.Description, mg.IsPublic, mg.CreatedAt, mg.LastActivity, mg.OwnerUserId, mg.IsUserPrivate,
                           u.UserName as OwnerUserName,
                           EXISTS (SELECT 1 FROM MailGroupUser mgu WHERE mgu.MailGroupId = mg.Id AND mgu.UserId = @UserId) as HasAccess
                    FROM MailGroup mg
                    LEFT JOIN `User` u ON mg.OwnerUserId = u.Id
                    WHERE mg.Subdomain IS NOT NULL AND mg.IsUserPrivate = 0";

        var groups = await db.QueryAsync<dynamic>(sql, new { UserId = userId });

        var result = groups.Select(mg => new
        {
            Id = (long)mg.Id,
            Path = mg.Path ?? "",
            Subdomain = mg.Subdomain ?? "",
            Description = mg.Description ?? "",
            IsPublic = Convert.ToBoolean(mg.IsPublic),
            CreatedAt = AsDateTime((object?)mg.CreatedAt),
            LastActivity = AsNullableDateTime((object?)mg.LastActivity),
            IsOwner = AsNullableGuid((object?)mg.OwnerUserId) == userId,
            IsUserPrivate = Convert.ToBoolean(mg.IsUserPrivate),
            OwnerUserName = (string?)mg.OwnerUserName ?? "",
            HasAccess = Convert.ToBoolean(mg.HasAccess) || AsNullableGuid((object?)mg.OwnerUserId) == userId
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

        var isAdmin = _userService.IsAdmin();
        if (!isAdmin && !await _mailGroupService.HasUserAccess(mailGroupId, currentUserId))
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

        if (!await _mailGroupService.HasUserAccess(mailGroupId, currentUserId))
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

        if (!await _mailGroupService.HasUserAccess(id, currentUserId))
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
            IsOwner = mailGroup.OwnerUserId == currentUserId,
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

        if (!await _mailGroupService.HasUserAccess(mailGroupId, currentUserId))
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
            UserId = AsGuid((object?)mgu.UserId),
            GrantedAt = AsDateTime((object?)mgu.GrantedAt),
            User = new
            {
                Id = AsGuid((object?)mgu.User_Id),
                UserName = (string)mgu.User_UserName,
                Role = (Role)Convert.ToInt32(mgu.User_Role),
                TimeStamp = AsDateTime((object?)mgu.User_TimeStamp)
            }
        }).ToList();

        return Ok(result);
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        if (!_userService.IsAdmin())
            return Forbid();

        using var db = await _db.GetConnectionAsync();
        var users = await db.SelectAsync<User>();

        var result = users.Select(u => new
        {
            u.Id,
            u.UserName,
            u.Role,
            u.TimeStamp,
            u.Subdomain
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

        if (!mailGroup.IsPublic &&
            mailGroup.OwnerUserId != userId &&
            !await db.ExistsAsync<MailGroupUser>(mgu => mgu.MailGroupId == id && mgu.UserId == userId))
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
                if (!mailGroup.IsPublic &&
                    mailGroup.OwnerUserId != userId &&
                    !await db.ExistsAsync<MailGroupUser>(mgu => mgu.MailGroupId == mailGroup.Id && mgu.UserId == userId))
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
            // Only mail the user is allowed to see, never every row in the table.
            emailIds = (await db.QueryAsync<long>(
                $@"SELECT m.Id FROM Mail m
                   LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path
                   WHERE {MailVisibilityClause}",
                new
                {
                    UserId = userId,
                    UserSubdomain = _userService.GetSubdomain(),
                    IsAdmin = _userService.IsAdmin() ? 1 : 0
                })).ToList();
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

        // Batch the read-record inserts; the unique (UserId, MailId) index makes this idempotent.
        var insertIgnore = _db.Provider == DatabaseProvider.SQLite ? "INSERT OR IGNORE" : "INSERT IGNORE";
        var now = DateTime.UtcNow;
        await db.ExecuteAsync(
            $@"{insertIgnore} INTO UserMailRead (UserId, MailId, ReadAt)
               VALUES (@UserId, @MailId, @ReadAt)",
            unreadEmailIds.Select(emailId => new { UserId = userId, MailId = emailId, ReadAt = now }));

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
    public string? Search { get; set; }
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

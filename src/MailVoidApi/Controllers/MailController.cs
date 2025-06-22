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
        var groups = await _context.MailGroups.ToListAsync();
        return Ok(groups);
    }
    [HttpPost("groups")]
    public async Task<IActionResult> SaveMailGroup([FromBody] MailGroupRequest groupRequest)
    {
        var group = groupRequest.ToMailGroup(_userService.GetUserId());
        if (group.Id == 0)
        {
            _context.MailGroups.Add(group);
            await _context.SaveChangesAsync();
        }
        else
        {
            var existingGroup = await _context.MailGroups.FindAsync(group.Id);
            if (existingGroup != null)
            {
                existingGroup.Path = group.Path;
                existingGroup.Rules = group.Rules;
                await _context.SaveChangesAsync();
            }
        }
        await _mailGroupService.UpdateMailsByMailGroupPattern(group);
        return Ok();
    }

}
public record MailGroupRequest
{
    public long? Id { get; set; }
    public required string Path { get; set; }
    public required string Rules { get; set; }

    public MailGroup ToMailGroup(Guid userId)
    {
        return new MailGroup()
        {
            Id = Id ?? 0,
            Path = Path,
            Rules = Rules,
            OwnerUserId = userId
        };
    }
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

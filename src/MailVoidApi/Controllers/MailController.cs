using MailVoidCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MailVoidApi.Controllers;
[Authorize]
[ApiController]
[Route("api/mail")]
public class MailController : ControllerBase
{
    private readonly ILogger<MailController> _logger;
    private readonly MailDbContext _dbContext;
    public MailController(ILogger<MailController> logger, MailDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }
    [HttpGet("{id}")]
    public async Task<IActionResult> GetMail(long id)
    {
        var email = await _dbContext.Mail.FindAsync(id);
        if (email == null)
        {
            return NotFound();
        }
        return Ok(email);
    }
    [HttpGet("boxes")]
    public async Task<IEnumerable<string>> GetBoxes()
    {
        var boxes = await _dbContext.Mail
                            .Select(m => m.To)
                            .Distinct()
                            .ToListAsync();
        return boxes;

    }
    [HttpPost]
    public async Task<IEnumerable<Mail>> GetMails([FromBody] FilterOptions? options)
    {


        // Get emails based on the selected mailbox
        var emails = string.IsNullOrEmpty(options?.To)
        ? _dbContext.Mail : _dbContext.Mail.Where(m => m.To == options.To);


        return await emails.Select(x => new Mail()
        {
            Id = x.Id,
            To = x.To,
            From = x.From,
            Subject = x.Subject,
            Text = "",
            CreatedOn = x.CreatedOn

        }).ToListAsync();
    }
}
public class FilterOptions
{
    public required string To { get; set; }
}
public class MailViewModel
{
    public required List<string> Mailboxes { get; set; }
    public required List<Mail> Emails { get; set; }
}

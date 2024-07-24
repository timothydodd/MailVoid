using MailVoidCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MailVoidWeb.Controllers;
[ApiController]
[Route("api/mail")]
public class EmailController : ControllerBase
{
    private readonly ILogger<EmailController> _logger;
    private readonly MailDbContext _dbContext;
    public EmailController(ILogger<EmailController> logger, MailDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }
    [AllowAnonymous]
    [HttpPost()]
    public async Task<IActionResult> EmailWebhook([FromForm] EmailModel email)
    {

        var text = email.Text;
        if (!string.IsNullOrWhiteSpace(email.Html))
        {
            text = email.Html;
        }

        float.TryParse(email.Spam_Score, out float score);
        if (score > 4.5)
        {
            text = "SPAM";
        }
        if (_dbContext.Mail.SingleOrDefault(e => e.Id == email.Id) != null)
        {
            return Ok();
        }

        await _dbContext.Mail.AddAsync(new Mail
        {
            Id = email.Id,
            To = email.To,
            From = email.From,
            Text = text,
            IsHtml = !string.IsNullOrWhiteSpace(email?.Html ?? ""),
            Subject = email?.Subject ?? "",
            Charsets = email?.Charsets ?? "",
            CreatedOn = email?.CreatedOn ?? DateTime.UtcNow
        });
        _dbContext.SaveChanges();
        return Ok();
    }
}

using System.Text.Json;
using MailVoidCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace MailVoidWeb.Controllers;
[ApiController]
[Route("external/api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;
    private readonly MailDbContext _dbContext;
    public WebhookController(ILogger<WebhookController> logger, MailDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }
    [AllowAnonymous]
    [HttpPost("mail")]
    public async Task<IActionResult> EmailWebhook([FromForm] EmailModel email)
    {

        _logger.LogInformation(JsonSerializer.Serialize(email));
        var to = email.Envelope?.To?.FirstOrDefault();
        if (to == null)
        {
            return Ok();
        }
        var from = email.Envelope?.From;
        if (from == null)
        {
            return Ok();
        }

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
            To = to,
            From = from,
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

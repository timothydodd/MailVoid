using MailVoidApi.Authentication;
using MailVoidApi.Data;
using MailVoidApi.Hubs;
using MailVoidApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
namespace MailVoidWeb.Controllers;

[ApiController]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;
    private readonly MailVoidDbContext _context;
    private readonly IMailGroupService _mailGroupService;
    private readonly IMailDataExtractionService _mailDataExtractionService;
    private readonly IHubContext<MailNotificationHub> _hubContext;

    public WebhookController(ILogger<WebhookController> logger, MailVoidDbContext context, IMailGroupService mailGroupService, IMailDataExtractionService mailDataExtractionService, IHubContext<MailNotificationHub> hubContext)
    {
        _logger = logger;
        _context = context;
        _mailGroupService = mailGroupService;
        _mailDataExtractionService = mailDataExtractionService;
        _hubContext = hubContext;
    }

    [ApiKey]
    [HttpPost("mail")]
    public async Task<IActionResult> MailDataWebhook([FromBody] MailData mailData)
    {
        _logger.LogInformation("Received MailData from {From} to {To}",
            mailData.From, mailData.To);

        try
        {
            var mail = await _mailDataExtractionService.ExtractMailFromDataAsync(mailData);

            await _mailGroupService.SetMailPath(mail);

            // Update the mailgroup's LastActivity when new mail arrives
            if (!string.IsNullOrEmpty(mail.MailGroupPath))
            {
                var mailGroup = await _context.MailGroups
                    .FirstOrDefaultAsync(mg => mg.Path == mail.MailGroupPath);
                if (mailGroup != null)
                {
                    mailGroup.LastActivity = DateTime.UtcNow;
                }
            }

            _context.Mails.Add(mail);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully processed MailData for {From} to {To}",
                mail.From, mail.To);

            // Send SignalR notification to all connected clients
            await _hubContext.Clients.All.SendAsync("NewMail", new
            {
                id = mail.Id,
                from = mail.From,
                to = mail.To,
                subject = mail.Subject,
                receivedDate = DateTime.UtcNow,
                mailGroupPath = mail.MailGroupPath
            });

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process MailData from {From} to {To}",
                mailData.From, mailData.To);
            return StatusCode(500, "Failed to process email");
        }
    }
}

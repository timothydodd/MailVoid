using Dapper;
using MailVoidApi.Authentication;
using MailVoidApi.Data;
using MailVoidApi.Hubs;
using MailVoidApi.Services;
using MailVoidWeb.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using RoboDodd.OrmLite;

namespace MailVoidWeb.Controllers;

[ApiController]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;
    private readonly IDatabaseService _db;
    private readonly IMailGroupService _mailGroupService;
    private readonly IMailDataExtractionService _mailDataExtractionService;
    private readonly IHubContext<MailNotificationHub> _hubContext;

    public WebhookController(ILogger<WebhookController> logger, IDatabaseService db, IMailGroupService mailGroupService, IMailDataExtractionService mailDataExtractionService, IHubContext<MailNotificationHub> hubContext)
    {
        _logger = logger;
        _db = db;
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

            using var db = await _db.GetConnectionAsync();

            // Update the mailgroup's LastActivity when new mail arrives
            if (!string.IsNullOrEmpty(mail.MailGroupPath))
            {
                var mailGroup = await db.SingleAsync<MailGroup>(mg => mg.Path == mail.MailGroupPath);
                if (mailGroup != null)
                {
                    mailGroup.LastActivity = DateTime.UtcNow;
                    await db.UpdateAsync(mailGroup);
                }
            }

            mail.Id = await db.InsertAsync(mail, selectIdentity: true);

            _logger.LogInformation("Successfully processed MailData for {From} to {To}",
                mail.From, mail.To);

            var payload = new
            {
                id = mail.Id,
                from = mail.From,
                to = mail.To,
                subject = mail.Subject,
                receivedDate = DateTime.UtcNow,
                mailGroupPath = mail.MailGroupPath
            };

            // Notify only users who can access this mail's group (mirrors the mail-list visibility rules),
            // rather than broadcasting to every connected client.
            if (!string.IsNullOrEmpty(mail.MailGroupPath))
            {
                var recipientIds = await db.QueryAsync<Guid>(
                    @"SELECT DISTINCT u.Id
                      FROM `User` u
                      LEFT JOIN MailGroup mg ON mg.Path = @Path
                      WHERE mg.Id IS NOT NULL AND (
                          mg.OwnerUserId = u.Id
                          OR mg.Subdomain = u.Subdomain
                          OR EXISTS (SELECT 1 FROM MailGroupUser mgu WHERE mgu.MailGroupId = mg.Id AND mgu.UserId = u.Id)
                          OR (u.Role = 1 AND mg.Subdomain = 'default')
                      )",
                    new { Path = mail.MailGroupPath });

                foreach (var recipientId in recipientIds)
                {
                    await _hubContext.Clients.Group($"user-{recipientId}").SendAsync("NewMail", payload);
                }
            }

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

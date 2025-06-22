using MailVoidApi.Authentication;
using MailVoidApi.Data;
using MailVoidApi.Services;
using Microsoft.AspNetCore.Mvc;
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
    
    public WebhookController(ILogger<WebhookController> logger, MailVoidDbContext context, IMailGroupService mailGroupService, IMailDataExtractionService mailDataExtractionService)
    {
        _logger = logger;
        _context = context;
        _mailGroupService = mailGroupService;
        _mailDataExtractionService = mailDataExtractionService;
    }

    [ApiKey]
    [HttpPost("mail")]
    public async Task<IActionResult> MailDataWebhook([FromBody] MailData mailData)
    {
        _logger.LogInformation("Received MailData from {From} to {To} with size {Size}", 
            mailData.From, mailData.To, mailData.RawSize);

        try
        {
            var mail = await _mailDataExtractionService.ExtractMailFromDataAsync(mailData);
            
            // Check if mail already exists
            var existingMail = await _context.Mails
                .FirstOrDefaultAsync(m => m.From == mail.From && 
                                        m.To == mail.To && 
                                        m.Subject == mail.Subject &&
                                        m.CreatedOn.Date == mail.CreatedOn.Date);

            if (existingMail == null)
            {
                await _mailGroupService.SetMailPath(mail);
                
                _context.Mails.Add(mail);
                await _context.SaveChangesAsync();
                
                _logger.LogInformation("Successfully processed MailData for {From} to {To}", 
                    mail.From, mail.To);
            }
            else
            {
                _logger.LogInformation("Duplicate mail detected, skipping: {From} to {To} - {Subject}", 
                    mail.From, mail.To, mail.Subject);
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

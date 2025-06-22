using System.Text.Json;
using System.Text.RegularExpressions;
using MailVoidApi.Authentication;
using MailVoidApi.Common;
using MailVoidApi.Data;
using MailVoidApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace MailVoidWeb.Controllers;

[ApiController]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;
    private readonly MailVoidDbContext _context;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IMailGroupService _mailGroupService;
    
    public WebhookController(ILogger<WebhookController> logger, IBackgroundTaskQueue taskQueue, MailVoidDbContext context, IMailGroupService mailGroupService)
    {
        _logger = logger;
        _taskQueue = taskQueue;
        _context = context;
        _mailGroupService = mailGroupService;
    }
    [ApiKey]
    [HttpPost("mail")]
    public IActionResult EmailWebhook([FromForm] EmailModel email)
    {

        _taskQueue.QueueBackgroundWorkItem(async token =>
        {
            _logger.LogInformation("Received email for  {0}", email?.Envelope);
            var envelope = JsonSerializer.Deserialize<Envelope>(email?.Envelope ?? "", new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                TypeInfoResolver = new ApiJsonSerializerContext()
            });
            var to = envelope?.To?.FirstOrDefault();
            if (to == null)
            {
                _logger.LogWarning("Email to is empty");
                return;
            }

            if (string.IsNullOrWhiteSpace(email?.From))
            {
                _logger.LogWarning("Email from is empty");
                return;
            }

            var fromContact = ParseContact(email.From);
            var toOthers = await UpdateContactsAsync(email.To, to);

            var text = email.Text;
            if (!string.IsNullOrWhiteSpace(email.Html))
            {
                text = email.Html;
            }
            if (!string.IsNullOrEmpty(text))
            {
                text = Regex.Replace(text, @"\p{IsCombiningDiacriticalMarks}+", string.Empty, RegexOptions.Compiled);
            }
            float.TryParse(email.Spam_Score, out float score);
            if (score > 4.5)
            {
                text = "SPAM";
            }
            // Check if mail already exists
            var existingMail = await _context.Mails.FindAsync(email.Id);
            if (existingMail == null)
            {
                var mail = new Mail()
                {
                    Id = email.Id,
                    To = to,
                    From = fromContact.From,
                    FromName = fromContact.Name,
                    ToOthers = toOthers.Count > 0 ? string.Join(",", toOthers) : null,
                    Text = text ?? "",
                    IsHtml = !string.IsNullOrWhiteSpace(email?.Html ?? ""),
                    Subject = email?.Subject ?? "",
                    Charsets = email?.Charsets ?? "",
                    CreatedOn = email?.CreatedOn ?? DateTime.UtcNow
                };
                await _mailGroupService.SetMailPath(mail);
                
                _context.Mails.Add(mail);
                await _context.SaveChangesAsync();
            }
            // Replace email text unicode into non-unicode text

        });
        return Ok();
    }
    private async Task<List<string>> UpdateContactsAsync(string toOthers, string to)
    {
        List<string> toOthersList = new List<string>();
        List<Contact> newContacts = new List<Contact>();
        if (!string.IsNullOrEmpty(toOthers))
        {
            var items = toOthers.Split(",");
            foreach (var item in items)
            {
                var contact = ParseContact(item);
                if (!string.Equals(to, contact.From, StringComparison.OrdinalIgnoreCase))
                {
                    toOthersList.Add(contact.From);
                }
                newContacts.Add(contact);
            }
            
            if (newContacts.Count > 0)
            {
                var fromAddresses = newContacts.Select(x => x.From).ToList();
                var existingContacts = await _context.Contacts
                    .Where(c => fromAddresses.Contains(c.From))
                    .ToListAsync();

                var contactsToInsert = new List<Contact>();
                foreach (var contact in newContacts)
                {
                    var existingContact = existingContacts.FirstOrDefault(c => c.From == contact.From);
                    if (existingContact != null && !string.IsNullOrEmpty(contact.Name) && existingContact.Name != contact.Name)
                    {
                        existingContact.Name = contact.Name;
                    }
                    else if (existingContact == null && !string.IsNullOrEmpty(contact.From))
                    {
                        contactsToInsert.Add(contact);
                    }
                }

                if (contactsToInsert.Any())
                {
                    _context.Contacts.AddRange(contactsToInsert);
                }
                
                await _context.SaveChangesAsync();
            }
        }
        return toOthersList;
    }
    private Contact ParseContact(string contact)
    {
        var parts = contact.Split('<');
        if (parts.Length == 1)
        {
            return new Contact
            {
                Name = "",
                From = parts[0].Trim()
            };
        }
        return new Contact
        {
            Name = parts[0].Trim(),
            From = parts[1].Replace(">", "").Trim()
        };
    }
}

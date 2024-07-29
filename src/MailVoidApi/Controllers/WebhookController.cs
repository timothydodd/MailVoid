using System.Text.Json;
using MailVoidCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace MailVoidWeb.Controllers;

[ApiController]
[Route("api/webhook")]
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
        var envelope = JsonSerializer.Deserialize<Envelope>(email?.Envelope ?? "", new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        var to = envelope?.To?.FirstOrDefault();
        if (to == null)
        {
            _logger.LogWarning("Email to is empty");
            return Ok();
        }

        if (string.IsNullOrWhiteSpace(email?.From))
        {
            _logger.LogWarning("Email from is empty");
            return Ok();
        }

        var fromContact = ParseContact(email.From);
        var toOthers = await UpdateContactsAsync(email.To, to);

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
            From = fromContact.From,
            FromName = fromContact.Name,
            ToOthers = toOthers.Count > 0 ? string.Join(",", toOthers) : null,
            Text = text ?? "",
            IsHtml = !string.IsNullOrWhiteSpace(email?.Html ?? ""),
            Subject = email?.Subject ?? "",
            Charsets = email?.Charsets ?? "",
            CreatedOn = email?.CreatedOn ?? DateTime.UtcNow
        });
        _dbContext.SaveChanges();
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
                var existingContactFroms = await _dbContext.Contact
                                         .Where(c => newContacts.Select(nc => nc.From).Contains(c.From))
                                         .ToListAsync();

                var updateContacts = new List<Contact>();
                var contactsToInsert = new List<Contact>();
                foreach (var contact in newContacts)
                {
                    var existingContact = existingContactFroms.FirstOrDefault(c => c.From == contact.From);
                    if (existingContact != null && !string.IsNullOrEmpty(contact.Name) && existingContact.Name != contact.Name)
                    {
                        existingContact.Name = contact.Name;
                        updateContacts.Add(existingContact);
                    }
                    else if (existingContact == null && !string.IsNullOrEmpty(contact.From))
                    {
                        contactsToInsert.Add(contact);
                    }
                }

                if (contactsToInsert.Any())
                {
                    // Step 3: Add remaining contacts
                    _dbContext.Contact.AddRange(contactsToInsert);

                }
                if (updateContacts.Any())
                {
                    // Step 4: Update existing contacts
                    _dbContext.Contact.UpdateRange(updateContacts);
                }
                if (contactsToInsert.Any() || updateContacts.Any())
                    await _dbContext.SaveChangesAsync();
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

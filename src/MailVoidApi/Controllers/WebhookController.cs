using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailVoidApi.Common;
using MailVoidApi.Services;
using MailVoidWeb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceStack.Data;
using ServiceStack.OrmLite.Dapper;
namespace MailVoidWeb.Controllers;

[ApiController]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IBackgroundTaskQueue _taskQueue;
    public WebhookController(ILogger<WebhookController> logger, IBackgroundTaskQueue taskQueue, IDbConnectionFactory dbFactory)
    {
        _logger = logger;
        _taskQueue = taskQueue;
        _dbFactory = dbFactory;
    }
    [AllowAnonymous]
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
            using (var db = _dbFactory.OpenDbConnection())
            {

                var query = @"
INSERT INTO Mail(Id,Mail.To,Mail.From,FromName,ToOthers,Text,IsHtml,Subject,Charsets,CreatedOn)
SELECT @Id,@To,@From,@FromName,@ToOthers,@Text,@IsHtml,@Subject,@Charsets,@CreatedOn FROM DUAL
WHERE
NOT EXISTS (SELECT 1 FROM Mail Where Id=@Id)";

                await db.ExecuteAsync(query, new
                {
                    email.Id,
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

            using (var db = _dbFactory.OpenDbConnection())
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
                    var existingContactFroms = await GetExistingContacts(db, newContacts.Select(x => x.From));



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
                        await InsertContacts(db, contactsToInsert);

                    }
                    if (updateContacts.Any())
                    {
                        // Step 4: Update existing contacts
                        await UpdateContacts(db, updateContacts);
                    }

                }
            }
        }
        return toOthersList;
    }
    private async Task InsertContacts(IDbConnection con, IEnumerable<Contact> contacts)
    {
        if (contacts.Any())
        {
            foreach (var contact in contacts)
            {
                var query = $@"INSERT INTO Contact (Name, Contact.From) VALUES (@Name, @From)";
                await con.ExecuteAsync(query, contact);
            }

        }
    }
    private async Task UpdateContacts(IDbConnection con, IEnumerable<Contact> contacts)
    {
        if (contacts.Any())
        {
            foreach (var contact in contacts)
            {
                var query = $@"UPDATE Contact SET Name = @Name WHERE Id = @Id";
                await con.ExecuteAsync(query, contact);
            }
        }
    }
    private async Task<IEnumerable<Contact>> GetExistingContacts(IDbConnection con, IEnumerable<string> contacts)
    {
        WhereBuilder whereClause = new();
        var dynamicParameters = new DynamicParameters();
        if (contacts.Any())
        {
            List<string> keys = dynamicParameters.AddList(contacts, "cp");
            var ids = string.Join(',', keys);
            whereClause.AppendAnd($"c.FROM IN ({ids})");
        }
        else
        {
            return new List<Contact>();
        }
        var query = $@"SELECT * FROM Contact c {whereClause}";
        return await con.QueryAsync<Contact>(query, dynamicParameters);
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

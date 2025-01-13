using System.Text.RegularExpressions;
using MailVoidWeb;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Dapper;

namespace MailVoidApi.Services;

public class MailGroupService
{

    private readonly ILogger<MailGroupService> _logger;
    private readonly IDbConnectionFactory _dbFactory;

    public MailGroupService(ILogger<MailGroupService> logger, IDbConnectionFactory dbFactory)
    {
        _logger = logger;
        _dbFactory = dbFactory;
    }



    public async Task UpdateMailsByMailGroupPattern(MailGroup group)
    {
        // Convert mailgroup rule into regex
        if (string.IsNullOrEmpty(group.Rules))
        {
            _logger.LogWarning("MailGroup rules are empty or null.");
            return;
        }

        try
        {
            var regex = new Regex(group.Rules);
            using var db = await _dbFactory.OpenDbConnectionAsync();
            IEnumerable<Mail> mails = await db.QueryAsync<Mail>("SELECT * FROM Mail WHERE MailGroupPath IS NULL");

            foreach (var m in mails)
            {
                if (regex.IsMatch(m.To))
                {
                    m.MailGroupPath = group.Path;
                    await db.UpdateAsync(m);
                }

            }

            _logger.LogInformation($"Updated {mails.Count()} mails for MailGroup {group.Id}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating mails by MailGroup pattern.");
        }
    }
}


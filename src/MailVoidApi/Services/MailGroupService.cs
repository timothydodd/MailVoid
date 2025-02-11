using System.Text.Json;
using System.Text.RegularExpressions;
using MailVoidWeb;
using Microsoft.Extensions.Caching.Memory;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Dapper;

namespace MailVoidApi.Services;

public interface IMailGroupService
{
    Task SetMailPath(Mail m);
    Task UpdateMailsByMailGroupPattern(MailGroup group);
}

public class MailGroupService : IMailGroupService
{

    private readonly ILogger<MailGroupService> _logger;
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IMemoryCache _memoryCache;
    public MailGroupService(ILogger<MailGroupService> logger, IDbConnectionFactory dbFactory, IMemoryCache memoryCache)
    {
        _logger = logger;
        _dbFactory = dbFactory;
        _memoryCache = memoryCache;
    }

    private const string MailSetKey = "MAIL_SETS";
    private readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
    private async Task<List<MailRuleSet>> GetMailRuleSets()
    {
        if (_memoryCache.TryGetValue(MailSetKey, out List<MailRuleSet>? ruleset))
        {

            return ruleset ?? new List<MailRuleSet>();
        }
        using (var db = _dbFactory.OpenDbConnection())
        {
            var groups = (await db.SelectAsync<MailGroup>()).Select(group =>
            {
                return new MailRuleSet() { Path = group.Path, Rules = JsonSerializer.Deserialize<List<MailRule>>(group.Rules ?? "[]", JsonOptions) ?? new List<MailRule>() };


            }).ToList();
            _memoryCache.Set(groups, MailSetKey);
            return groups ?? new List<MailRuleSet>();
        }

    }
    public async Task SetMailPath(Mail m)
    {
        var sets = await this.GetMailRuleSets();

        foreach (var s in sets)
        {
            if (CheckMailRules(s, m))
                return;
        }
    }
    private bool CheckMailRules(MailRuleSet set, Mail m)
    {

        foreach (var r in set.Rules)
        {
            bool found = false;
            switch (r.TypeId)
            {
                case (int)MailRuleType.Contains:
                    {
                        found = m.To.Contains(r.Value);

                    }
                    break;
                case (int)MailRuleType.StartsWith:
                    {
                        found = m.To.StartsWith(r.Value);
                    }
                    break;
                case (int)MailRuleType.EndsWith:
                    {
                        found = m.To.EndsWith(r.Value);
                    }
                    break;
                case (int)MailRuleType.RegEx:
                    {
                        var regex = new Regex(r.Value);
                        found = regex.IsMatch(m.To);
                    }
                    break;

            }
            if (found)
            {
                m.MailGroupPath = set.Path;
                return true;
            }

        }
        return false;
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
            var rs = new MailRuleSet() { Path = group.Path, Rules = JsonSerializer.Deserialize<List<MailRule>>(group.Rules, JsonOptions) ?? new List<MailRule>() };


            if (rs.Rules == null || rs.Rules.Count == 0)
                return;

            using var db = await _dbFactory.OpenDbConnectionAsync();
            IEnumerable<Mail> mails = await db.QueryAsync<Mail>("SELECT * FROM Mail WHERE MailGroupPath IS NULL");


            foreach (var m in mails)
            {

                if (CheckMailRules(rs, m))
                {
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
public class MailRule
{
    public required string Value { get; set; }
    public required int TypeId { get; set; }

}
public class MailRuleSet
{
    public required string Path { get; set; }
    public required List<MailRule> Rules { get; set; }
}
public enum MailRuleType
{
    None = 0,
    Contains = 1,
    StartsWith = 2,
    EndsWith = 3,
    RegEx = 4,
}

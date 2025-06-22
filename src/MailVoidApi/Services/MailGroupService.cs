using System.Text.Json;
using System.Text.RegularExpressions;
using MailVoidApi.Data;
using MailVoidWeb;
using MailVoidWeb.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MailVoidApi.Services;

public interface IMailGroupService
{
    Task SetMailPath(Mail m);
    Task UpdateMailsByMailGroupPattern(MailGroup group);
}

public class MailGroupService : IMailGroupService
{
    private readonly ILogger<MailGroupService> _logger;
    private readonly MailVoidDbContext _context;
    private readonly IMemoryCache _memoryCache;
    
    public MailGroupService(ILogger<MailGroupService> logger, MailVoidDbContext context, IMemoryCache memoryCache)
    {
        _logger = logger;
        _context = context;
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
        
        var mailGroups = await _context.MailGroups.ToListAsync();
        var groups = mailGroups.Select(group =>
        {
            return new MailRuleSet() 
            { 
                Path = group.Path, 
                Rules = JsonSerializer.Deserialize<List<MailRule>>(group.Rules ?? "[]", JsonOptions) ?? new List<MailRule>() 
            };
        }).ToList();
        
        _memoryCache.Set(MailSetKey, groups);
        return groups;
    }
    public async Task SetMailPath(Mail m)
    {
        // First check if this email is claimed by a user
        var claimedMailbox = await _context.ClaimedMailboxes
            .Include(cm => cm.User)
            .FirstOrDefaultAsync(cm => cm.EmailAddress == m.To && cm.IsActive);

        if (claimedMailbox != null)
        {
            m.MailGroupPath = claimedMailbox.GetMailGroupPath(claimedMailbox.User!.UserName);
            return;
        }

        // If not claimed, check mail group rules
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
            var rs = new MailRuleSet() 
            { 
                Path = group.Path, 
                Rules = JsonSerializer.Deserialize<List<MailRule>>(group.Rules, JsonOptions) ?? new List<MailRule>() 
            };

            if (rs.Rules == null || rs.Rules.Count == 0)
                return;

            var mails = await _context.Mails
                .Where(m => m.MailGroupPath == null)
                .ToListAsync();

            var updatedCount = 0;
            foreach (var m in mails)
            {
                if (CheckMailRules(rs, m))
                {
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation($"Updated {updatedCount} mails for MailGroup {group.Id}.");
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

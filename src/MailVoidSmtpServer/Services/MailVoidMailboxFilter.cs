using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmtpServer;
using SmtpServer.Mail;
using SmtpServer.Storage;
using MailVoidSmtpServer.Configuration;

namespace MailVoidSmtpServer.Services;

public class MailVoidMailboxFilter : IMailboxFilter, IMailboxFilterFactory
{
    private readonly ILogger<MailVoidMailboxFilter> _logger;
    private readonly MailboxFilterOptions _options;

    public MailVoidMailboxFilter(ILogger<MailVoidMailboxFilter> logger, IOptions<MailboxFilterOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Determines whether an email from a given sender can be accepted.
    /// </summary>
    public Task<bool> CanAcceptFromAsync(
        ISessionContext context,
        IMailbox from,
        int size,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking if can accept email from: {From}, Size: {Size}", from.AsAddress(), size);

        // Security checks for sender
        if (from == null || string.IsNullOrWhiteSpace(from.AsAddress()))
        {
            _logger.LogWarning("Rejected email with empty sender address");
            return Task.FromResult(false);
        }

        var fromAddress = from.AsAddress();

        // Block configured domains
        var blockedDomains = _options.BlockedDomains;

        var domain = GetDomainFromEmail(fromAddress);
        if (blockedDomains.Any(blocked => blocked.Equals(domain, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Rejected email from blocked domain: {Domain}", domain);
            return Task.FromResult(false);
        }

        // Check for basic email format validity
        if (!IsValidEmailFormat(fromAddress))
        {
            _logger.LogWarning("Rejected email with invalid format: {Email}", fromAddress);
            return Task.FromResult(false);
        }

        // Check message size limits
        if (size > _options.MaxMessageSizeBytes)
        {
            _logger.LogWarning("Rejected email exceeding size limit. Size: {Size}, Max: {MaxSize}", size, _options.MaxMessageSizeBytes);
            return Task.FromResult(false);
        }

        _logger.LogInformation("Accepted email from: {From}", fromAddress);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Determines whether an email can be delivered to a given recipient.
    /// </summary>
    public Task<bool> CanDeliverToAsync(
        ISessionContext context,
        IMailbox to,
        IMailbox from,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking if can deliver to: {To} from: {From}", to.AsAddress(), from.AsAddress());

        // Security checks for recipient
        if (to == null || string.IsNullOrWhiteSpace(to.AsAddress()))
        {
            _logger.LogWarning("Rejected delivery to empty recipient address");
            return Task.FromResult(false);
        }

        var toAddress = to.AsAddress();

        // Check for valid email format
        if (!IsValidEmailFormat(toAddress))
        {
            _logger.LogWarning("Rejected delivery to invalid email format: {Email}", toAddress);
            return Task.FromResult(false);
        }

        // Only accept emails for our configured test domains
        // This prevents the server from being used as an open relay
        var allowedDomains = _options.AllowedDomains;
        var toDomain = GetDomainFromEmail(toAddress);

        if (!IsAllowedDomain(toDomain, allowedDomains))
        {
            _logger.LogWarning("Rejected delivery to non-test domain: {Domain}. Allowed domains: {AllowedDomains}",
                toDomain, string.Join(", ", allowedDomains));
            return Task.FromResult(false);
        }

        // Additional security: prevent email loops
        if (from.AsAddress().Equals(toAddress, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejected email loop attempt from/to: {Email}", toAddress);
            return Task.FromResult(false);
        }

        _logger.LogInformation("Accepted delivery to: {To}", toAddress);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Creates an instance of the mailbox filter (factory pattern).
    /// </summary>
    public IMailboxFilter CreateInstance(ISessionContext context)
    {
        return this;
    }

    private string GetDomainFromEmail(string email)
    {
        var atIndex = email.LastIndexOf('@');
        return atIndex >= 0 ? email.Substring(atIndex + 1) : string.Empty;
    }

    /// <summary>
    /// Checks if a domain is allowed, supporting subdomain matching.
    /// If "mailvoid.com" is in the allowed list, "subdomain.mailvoid.com" will also be allowed.
    /// </summary>
    private bool IsAllowedDomain(string domainToCheck, IEnumerable<string> allowedDomains)
    {
        if (string.IsNullOrWhiteSpace(domainToCheck))
            return false;

        foreach (var allowedDomain in allowedDomains)
        {
            if (string.IsNullOrWhiteSpace(allowedDomain))
                continue;

            // Exact match (case-insensitive)
            if (domainToCheck.Equals(allowedDomain, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Domain allowed by exact match: {Domain} matches {AllowedDomain}", 
                    domainToCheck, allowedDomain);
                return true;
            }

            // Subdomain match: check if domainToCheck ends with "." + allowedDomain
            // This ensures "subdomain.mailvoid.com" matches "mailvoid.com" 
            // but prevents "notmailvoid.com" from matching "mailvoid.com"
            var domainSuffix = "." + allowedDomain;
            if (domainToCheck.EndsWith(domainSuffix, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Domain allowed by subdomain match: {Domain} is subdomain of {AllowedDomain}", 
                    domainToCheck, allowedDomain);
                return true;
            }
        }

        return false;
    }

    private bool IsValidEmailFormat(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        // Basic email validation
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
            return false;

        // Check for multiple @ symbols
        if (email.Count(c => c == '@') != 1)
            return false;

        // Check domain has at least one dot
        var domain = email.Substring(atIndex + 1);
        if (!domain.Contains('.'))
            return false;

        // Check for invalid characters
        var invalidChars = new[] { ' ', '\t', '\n', '\r', '(', ')', '[', ']', '\\', ',', ';', ':', '<', '>' };
        if (email.Any(c => invalidChars.Contains(c)))
            return false;

        return true;
    }

}
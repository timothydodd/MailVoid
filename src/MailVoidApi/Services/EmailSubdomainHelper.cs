using System.Text.RegularExpressions;

namespace MailVoidApi.Services;

public static class EmailSubdomainHelper
{
    private static readonly Regex EmailRegex = new Regex(@"^[^@]+@([^@.]+)\.(.+)$", RegexOptions.Compiled);
    
    public static string ExtractSubdomain(string emailAddress)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return "default";
            
        var match = EmailRegex.Match(emailAddress.Trim().ToLowerInvariant());
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        
        // Fallback: try to extract anything before the first dot in the domain
        var atIndex = emailAddress.IndexOf('@');
        if (atIndex > 0 && atIndex < emailAddress.Length - 1)
        {
            var domain = emailAddress.Substring(atIndex + 1);
            var dotIndex = domain.IndexOf('.');
            if (dotIndex > 0)
            {
                return domain.Substring(0, dotIndex).ToLowerInvariant();
            }
        }
        
        return "default";
    }
    
    public static string GenerateMailGroupPath(string subdomain)
    {
        return $"subdomain/{subdomain}";
    }
}
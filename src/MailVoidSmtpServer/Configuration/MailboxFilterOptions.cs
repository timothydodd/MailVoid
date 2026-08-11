namespace MailVoidSmtpServer.Configuration;

public class MailboxFilterOptions
{
    public const string SectionName = "MailboxFilter";
    
    public List<string> AllowedDomains { get; set; } = new();
    public List<string> BlockedDomains { get; set; } = new();
}
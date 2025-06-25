namespace MailVoidSmtpServer.Configuration;

public class MailboxFilterOptions
{
    public const string SectionName = "MailboxFilter";
    
    public List<string> AllowedDomains { get; set; } = new();
    public List<string> BlockedDomains { get; set; } = new();
    public int MaxMessageSizeBytes { get; set; } = 10485760; // 10MB default
}
namespace MailVoidWeb;

public class EmailModel
{
    public long Id { get; set; }
    public string? Headers { get; set; }
    public string? Dkim { get; set; }
    public required string To { get; set; }
    public string? Html { get; set; }
    public required string From { get; set; }
    public string? Text { get; set; }
    public string? Sender_Ip { get; set; }
    public string? SPF { get; set; }
    public string? Attachments { get; set; }
    public string? Subject { get; set; }
    public string? Envelope { get; set; }
    public string? Charsets { get; set; }
    public DateTime? CreatedOn { get; set; }
    public string? Spam_Score { get; set; }
}

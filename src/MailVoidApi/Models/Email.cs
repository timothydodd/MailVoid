﻿namespace MailVoidWeb;

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
//{"to":["tdodd@dbmk2.com"],"from":"tdodd@me.com"}
public class MailData
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public Dictionary<string, string> Headers { get; init; } = new();
    public string? Html { get; init; }
    public string? Text { get; init; }
}

public class Envelope
{
    public List<string>? To { get; set; }
    public string? From { get; set; }
}


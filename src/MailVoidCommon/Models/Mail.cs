using ServiceStack.DataAnnotations;

namespace MailVoidCommon;


public class Mail
{
    [PrimaryKey]
    public long Id { get; set; }
    [Index]
    public required string To { get; set; }
    public required string Text { get; set; }
    public bool IsHtml { get; set; }
    [Index]
    public required string From { get; set; }
    public string? FromName { get; set; }
    public string? ToOthers { get; set; }
    public required string Subject { get; set; }
    public string? Charsets { get; set; }
    public DateTime CreatedOn { get; set; }
    [Index]
    public string? MailGroupPath { get; set; }
}

public class Contact
{
    [PrimaryKey]
    [AutoIncrement]
    public long Id { get; set; }
    [Index(Unique = true)]
    public required string From { get; set; }
    public required string Name { get; set; }
}

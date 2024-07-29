using Microsoft.EntityFrameworkCore;

namespace MailVoidCommon;

public class MailDbContext : DbContext
{
    public MailDbContext(DbContextOptions<MailDbContext> options)
    : base(options)
    {
    }

    public DbSet<Mail> Mail { get; set; }
    public DbSet<Contact> Contact { get; set; }
}
[PrimaryKey(nameof(Id))]
[Index(nameof(To), IsUnique = false)]
[Index(nameof(From), IsUnique = false)]
public class Mail
{
    public long Id { get; set; }
    public required string To { get; set; }
    public required string Text { get; set; }
    public bool IsHtml { get; set; }
    public required string From { get; set; }
    public string? FromName { get; set; }
    public string? ToOthers { get; set; }
    public required string Subject { get; set; }
    public string? Charsets { get; set; }
    public DateTime CreatedOn { get; set; }
}

[PrimaryKey(nameof(Id))]
[Index(nameof(From), IsUnique = true)]
public class Contact
{
    public long Id { get; set; }
    public required string From { get; set; }
    public required string Name { get; set; }
}

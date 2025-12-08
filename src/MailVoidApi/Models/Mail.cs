using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RoboDodd.OrmLite;

namespace MailVoidWeb;

[Table("Mail")]
public class Mail
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Index("IX_Mail_To")]
    public required string To { get; set; }

    [Required]
    public required string Text { get; set; }

    public bool IsHtml { get; set; }

    [Required]
    [Index("IX_Mail_From")]
    public required string From { get; set; }

    public string? FromName { get; set; }
    public string? ToOthers { get; set; }

    [Required]
    public required string Subject { get; set; }

    public string? Charsets { get; set; }

    [Default(typeof(DateTime), "CURRENT_TIMESTAMP")]
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    [Index("IX_Mail_MailGroupPath")]
    public string? MailGroupPath { get; set; }
}

[Table("Contact")]
public class Contact
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Index("IX_Contact_From", IsUnique = true)]
    public required string From { get; set; }

    [Required]
    public required string Name { get; set; }
}

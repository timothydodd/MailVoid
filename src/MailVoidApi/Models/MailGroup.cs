using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MailVoidWeb.Data.Models;
using RoboDodd.OrmLite;
using ForeignKeyAttribute = RoboDodd.OrmLite.ForeignKeyAttribute;

namespace MailVoidWeb;

[Table("MailGroup")]
public class MailGroup
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Index("IX_MailGroup_Path")]
    public string? Path { get; set; }

    [Index("IX_MailGroup_Subdomain")]
    public string? Subdomain { get; set; }

    public string? Description { get; set; }

    [Required]
    [RoboDodd.OrmLite.ForeignKey(typeof(User), OnDelete = "CASCADE")]
    public required Guid OwnerUserId { get; set; }

    public bool IsPublic { get; set; }

    /// <summary>
    /// Indicates if this is a private user mailbox that cannot be shared or deleted
    /// </summary>
    public bool IsUserPrivate { get; set; } = false;

    /// <summary>
    /// Indicates if this is a default user mailbox that cannot be unclaimed
    /// </summary>
    public bool IsDefaultMailbox { get; set; } = false;

    [Default(typeof(DateTime), "CURRENT_TIMESTAMP")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastActivity { get; set; }

    /// <summary>
    /// Number of days to retain emails in this mailbox. Null or 0 means no auto-deletion.
    /// Default is 3 days for new mailboxes.
    /// </summary>
    public int? RetentionDays { get; set; } = 3;

    /// <summary>
    /// Gets the path format for a user's private mailbox
    /// </summary>
    public static string GetUserPrivatePath(string username)
    {
        return $"user-{username}";
    }
}

[Table("MailGroupUser")]
[CompositeIndex(nameof(MailGroupId), nameof(UserId), Unique = true)]
public class MailGroupUser
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [ForeignKey(typeof(MailGroup), OnDelete = "CASCADE")]
    public required long MailGroupId { get; set; }

    [Required]
    [ForeignKey(typeof(User), OnDelete = "CASCADE")]
    public required Guid UserId { get; set; }

    [Default(typeof(DateTime), "CURRENT_TIMESTAMP")]
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}

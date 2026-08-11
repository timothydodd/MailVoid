using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MailVoidWeb;
using RoboDodd.OrmLite;
using ForeignKeyAttribute = RoboDodd.OrmLite.ForeignKeyAttribute;

namespace MailVoidWeb.Data.Models;

/// <summary>
/// Per-user opt-out: a mail group the user has chosen to hide from their mailbox list.
/// Everything is visible by default; hiding a group removes its mailboxes from the sidebar.
/// </summary>
[Table("HiddenMailGroup")]
[CompositeIndex(nameof(UserId), nameof(MailGroupId), Unique = true)]
public class HiddenMailGroup
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [ForeignKey(typeof(User), OnDelete = "CASCADE")]
    public required Guid UserId { get; set; }

    [Required]
    [ForeignKey(typeof(MailGroup), OnDelete = "CASCADE")]
    public required long MailGroupId { get; set; }

    [Default(typeof(DateTime), "CURRENT_TIMESTAMP")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

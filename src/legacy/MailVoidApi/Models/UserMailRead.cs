using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RoboDodd.OrmLite;
using ForeignKeyAttribute = RoboDodd.OrmLite.ForeignKeyAttribute;

namespace MailVoidWeb.Data.Models;

[Table("UserMailRead")]
[CompositeIndex(nameof(UserId), nameof(MailId), Unique = true)]
public class UserMailRead
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [ForeignKey(typeof(User), OnDelete = "CASCADE")]
    public required Guid UserId { get; set; }

    [Required]
    [ForeignKey(typeof(Mail), OnDelete = "CASCADE")]
    public required long MailId { get; set; }

    [Default(typeof(DateTime), "CURRENT_TIMESTAMP")]
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}

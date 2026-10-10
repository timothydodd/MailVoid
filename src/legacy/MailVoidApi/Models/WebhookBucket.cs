using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RoboDodd.OrmLite;

namespace MailVoidApi.Models;

[Table("WebhookBucket")]
[CompositeIndex(nameof(Name), Unique = true)]
public class WebhookBucket
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [StringLength(255)]
    public required string Name { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    public required Guid OwnerUserId { get; set; }

    public bool IsPublic { get; set; } = true;

    [Default(typeof(DateTime), "CURRENT_TIMESTAMP")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastActivity { get; set; }

    public int? RetentionDays { get; set; } = 3;
}

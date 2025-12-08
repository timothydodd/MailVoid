using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RoboDodd.OrmLite;

namespace MailVoidApi.Models;

[Table("Webhook")]
[CompositeIndex(nameof(BucketName))]
[CompositeIndex(nameof(CreatedOn))]
public class Webhook
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [StringLength(255)]
    public required string BucketName { get; set; }

    [Required]
    [StringLength(10)]
    public required string HttpMethod { get; set; }

    [Required]
    [StringLength(2048)]
    public required string Path { get; set; }

    [StringLength(4096)]
    public string? QueryString { get; set; }

    [Required]
    public required string Headers { get; set; }  // JSON

    [Required]
    public required string Body { get; set; }

    [StringLength(255)]
    public string? ContentType { get; set; }

    [StringLength(45)]
    public string? SourceIp { get; set; }

    [Default(typeof(DateTime), "CURRENT_TIMESTAMP")]
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
}

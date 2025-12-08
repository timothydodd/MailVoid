using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MailVoidWeb.Data.Models;
using RoboDodd.OrmLite;
using ForeignKeyAttribute = RoboDodd.OrmLite.ForeignKeyAttribute;

namespace MailVoidApi.Models;

[Table("RefreshToken")]
[CompositeIndex(nameof(Token), nameof(UserId))]
public class RefreshToken
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [Index("IX_RefreshToken_Token")]
    public required string Token { get; set; }

    [Required]
    [ForeignKey(typeof(User), OnDelete = "CASCADE")]
    public required Guid UserId { get; set; }

    public DateTime ExpiryDate { get; set; }
    public bool IsRevoked { get; set; }

    [Default(typeof(DateTime), "CURRENT_TIMESTAMP")]
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RoboDodd.OrmLite;

namespace MailVoidWeb.Data.Models;

[Table("User")]
public class User
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [StringLength(255)]
    [Index("IX_User_UserName", IsUnique = true)]
    public required string UserName { get; set; }

    [Required]
    [StringLength(255)]
    public required string PasswordHash { get; set; }

    [Required]
    public required DateTime TimeStamp { get; set; }

    [Required]
    public Role Role { get; set; } = Role.User;
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MailVoidWeb.Data.Models;

public class User
{
    [Key]
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(255)]
    public required string UserName { get; set; }
    
    [Required]
    [MaxLength(255)]
    public required string PasswordHash { get; set; }

    [Required]
    public required DateTime TimeStamp { get; set; }
    
    [Required]
    public Role Role { get; set; } = Role.User;
}

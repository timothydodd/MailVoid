using MailVoidWeb.Data.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MailVoidWeb
{
    public class MailGroup
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        
        public string? Path { get; set; }
        
        public string? Subdomain { get; set; }
        
        public string? Description { get; set; }
        
        [Required]
        [ForeignKey("User")]
        public required Guid OwnerUserId { get; set; }
        
        public bool IsPublic { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public virtual ICollection<MailGroupUser> MailGroupUsers { get; set; } = new List<MailGroupUser>();
    }

    public class MailGroupUser
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        
        [Required]
        [ForeignKey("MailGroup")]
        public required long MailGroupId { get; set; }
        
        [Required]
        [ForeignKey("User")]
        public required Guid UserId { get; set; }
        
        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
        
        public virtual MailGroup MailGroup { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }
}

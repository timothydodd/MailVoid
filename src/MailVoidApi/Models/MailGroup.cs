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
        
        /// <summary>
        /// Indicates if this is a private user mailbox that cannot be shared or deleted
        /// </summary>
        public bool IsUserPrivate { get; set; } = false;
        
        /// <summary>
        /// Indicates if this is a default user mailbox that cannot be unclaimed
        /// </summary>
        public bool IsDefaultMailbox { get; set; } = false;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? LastActivity { get; set; }
        
        public virtual ICollection<MailGroupUser> MailGroupUsers { get; set; } = new List<MailGroupUser>();
        
        /// <summary>
        /// Gets the path format for a user's private mailbox
        /// </summary>
        public static string GetUserPrivatePath(string username)
        {
            return $"user-{username}";
        }
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

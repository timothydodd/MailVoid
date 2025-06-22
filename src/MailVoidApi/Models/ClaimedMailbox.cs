using MailVoidWeb.Data.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MailVoidWeb.Models
{
    public class ClaimedMailbox
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        
        [Required]
        public required string EmailAddress { get; set; }
        
        [Required]
        [ForeignKey("User")]
        public required Guid UserId { get; set; }
        
        public DateTime ClaimedOn { get; set; } = DateTime.UtcNow;
        
        public bool IsActive { get; set; } = true;
        
        // Navigation property
        public User? User { get; set; }
        
        /// <summary>
        /// Gets the path format for this claimed mailbox (user-{username})
        /// </summary>
        public string GetMailGroupPath(string username)
        {
            return $"user-{username}";
        }
    }
}
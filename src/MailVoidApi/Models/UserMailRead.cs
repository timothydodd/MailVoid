using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MailVoidWeb.Data.Models
{
    public class UserMailRead
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        
        [Required]
        [ForeignKey("User")]
        public required Guid UserId { get; set; }
        
        [Required]
        [ForeignKey("Mail")]
        public required long MailId { get; set; }
        
        public DateTime ReadAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public virtual User User { get; set; } = null!;
        public virtual Mail Mail { get; set; } = null!;
    }
}
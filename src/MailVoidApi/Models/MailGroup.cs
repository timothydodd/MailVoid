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
        
        [Required]
        public required string Path { get; set; }
        
        public string? Rules { get; set; }
        
        [Required]
        [ForeignKey("User")]
        public required Guid OwnerUserId { get; set; }
        
        public bool IsPublic { get; set; }
    }

    public class MailGroupRule
    {
        public required List<string> Patterns { get; set; }
        public int? MaxLifeTime { get; set; }
    }
}

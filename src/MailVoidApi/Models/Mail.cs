using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MailVoidWeb;

public class Mail
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    
    [Required]
    public required string To { get; set; }
    
    [Required]
    public required string Text { get; set; }
    
    public bool IsHtml { get; set; }
    
    [Required]
    public required string From { get; set; }
    
    public string? FromName { get; set; }
    public string? ToOthers { get; set; }
    
    [Required]
    public required string Subject { get; set; }
    
    public string? Charsets { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public string? MailGroupPath { get; set; }
}

public class Contact
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    
    [Required]
    public required string From { get; set; }
    
    [Required]
    public required string Name { get; set; }
}

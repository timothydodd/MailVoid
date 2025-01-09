using MailVoidCommon.Data.Models;
using ServiceStack.DataAnnotations;

namespace MailVoidCommon
{
    public class MailGroup
    {
        [AutoIncrement]
        [PrimaryKey]
        public long Id { get; set; }
        [Index(Unique = true)]
        public required string Path { get; set; }
        [Index(Unique = true)]
        public string? Rules { get; set; }
        [References(typeof(User))]
        public required Guid OwnerUserId { get; set; }
        public bool IsPublic { get; set; }
    }

    public class MailGroupRule
    {
        public required List<string> Patterns { get; set; }
        public int? MaxLifeTime { get; set; }

    }
}

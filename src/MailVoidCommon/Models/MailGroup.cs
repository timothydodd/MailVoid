using ServiceStack.DataAnnotations;

namespace MailVoidCommon
{
    public class MailGroup
    {
        [PrimaryKey]
        public long Id { get; set; }
        public required string Path { get; set; }
        public required string Pattern { get; set; }
    }
}

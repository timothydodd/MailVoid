namespace MailVoidApi.Common;

public class PagedResults<T>
{
    public IEnumerable<T>? Items { get; set; }
    public long TotalCount { get; set; }
}

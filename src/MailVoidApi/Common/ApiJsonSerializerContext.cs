using System.Text.Json.Serialization;
using MailVoidApi.Controllers;
using MailVoidWeb;

[JsonSerializable(typeof(Mail))]
[JsonSerializable(typeof(IEnumerable<Mail>))]
[JsonSerializable(typeof(FilterOptions))]
[JsonSerializable(typeof(IEnumerable<string>))]
[JsonSerializable(typeof(EmailModel))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Envelope))]
public partial class ApiJsonSerializerContext : JsonSerializerContext
{

}

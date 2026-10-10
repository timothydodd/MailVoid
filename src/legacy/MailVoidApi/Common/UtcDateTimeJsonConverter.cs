using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailVoidApi.Common;

// MySQL returns DateTime values with Kind=Unspecified. Without this converter,
// System.Text.Json emits ISO 8601 strings without a timezone marker, which the
// browser parses as local time — shifting timestamps by the user's UTC offset.
// All persisted DateTimes are UTC, so we tag them as such on the wire.
public class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        writer.WriteStringValue(utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }
}

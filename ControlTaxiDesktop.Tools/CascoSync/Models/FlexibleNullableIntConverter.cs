using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlTaxiDesktop.Tools.CascoSync.Models;

public sealed class FlexibleNullableIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericValue))
            return numericValue;

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (int.TryParse(text, out var parsed))
                return parsed;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var longValue))
            return (int)longValue;

        return null;
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(value.Value);
    }
}

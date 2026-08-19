using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlTaxiDesktop.Tools.CascoSync.Models;

public sealed class FlexibleNullableDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numericValue))
            return numericValue;

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (decimal.TryParse(text, out var parsed))
                return parsed;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var intValue))
            return intValue;

        return null;
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(value.Value);
    }
}

using System.Text.Json.Serialization;

namespace AutoTrainer.Api.ApiTypes;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class NullNanDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return double.NaN; 
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            try
            {
                return reader.GetDouble();
            } catch { 
                return double.NaN;
            }
        }

        if (reader.TokenType == JsonTokenType.String && reader.GetString() == "NaN")
        {
            return double.NaN;
        }

        throw new JsonException($"Unexpected token type: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsNaN(value))
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }
}

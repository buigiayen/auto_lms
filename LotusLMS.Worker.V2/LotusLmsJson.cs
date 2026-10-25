using System.Globalization;
using System.Text.Json.Nodes;

namespace LotusLMS.Worker2;

internal static class LotusLmsJson
{
    public static JsonArray RequireResultArray(JsonObject response, string operation) =>
        response["result"] as JsonArray
        ?? throw new InvalidDataException(
            $"The {operation} response is missing a result array.");

    public static int ReadInt(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return int.TryParse(
            ReadString(node),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out number)
            ? number
            : 0;
    }

    public static int ReadPositiveInt(JsonNode? node)
    {
        var value = ReadInt(node);
        return value > 0 ? value : 0;
    }

    public static string? ReadString(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        var scalar = value.ToString();
        return string.IsNullOrWhiteSpace(scalar) ? null : scalar.Trim();
    }
}

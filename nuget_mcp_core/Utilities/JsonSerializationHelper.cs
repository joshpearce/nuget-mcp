using System.Text.Json;
using System.Text.Json.Serialization;

namespace NugetMcp.Core.Utilities;

public static class JsonSerializationHelper
{
    private static readonly JsonSerializerOptions _defaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static JsonSerializerOptions GetDefaultOptions()
    {
        // Return a new instance to prevent external modification
        return new JsonSerializerOptions(_defaultOptions);
    }
}
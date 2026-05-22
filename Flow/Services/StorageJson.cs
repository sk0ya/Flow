using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flow.Services;

internal static class StorageJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

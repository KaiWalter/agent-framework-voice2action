using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voice2Action.Infrastructure.AI;

public static class ToolResultJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Ok(string type, object data) =>
        JsonSerializer.Serialize(new { ok = true, type, data }, Options);

    public static string Error(string code, string message, string? type = null) =>
        JsonSerializer.Serialize(new { ok = false, type, error = new { code, message } }, Options);
}

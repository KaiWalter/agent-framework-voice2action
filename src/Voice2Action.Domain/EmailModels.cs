namespace Voice2Action.Domain;

using System.Text.Json.Serialization;

public sealed class DetectionResult
{
    [JsonPropertyName("is_spam")]
    public bool IsSpam { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class EmailResponse
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;
}

public static class EmailStateConstants
{
    public const string EmailStateScope = "EmailState";
}

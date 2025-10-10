using System.ComponentModel;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;

namespace Voice2Action.McpUtility;

// Hosts the utility MCP tools (transcription + current date/time)
internal static class UtilityToolHost
{
    internal static ITranscriptionService? Transcription;
    internal static IDateTimeService? DateTimeService;

    [Description("Transcribe the given audio file (mp3/wav/m4a) using Azure OpenAI Whisper and return raw text.")]
    public static string TranscribeVoiceRecording([Description("Path to recording file.")] string recording)
    {
        if (Transcription is null) throw new InvalidOperationException("Transcription service not initialized yet.");
        if (string.IsNullOrWhiteSpace(recording)) throw new ArgumentException("Recording path empty", nameof(recording));
        if (!File.Exists(recording)) throw new FileNotFoundException("Audio file not found", recording);
        try
        {
            var text = Transcription.TranscribeAsync(recording).GetAwaiter().GetResult();
            return ToolResultJson.Ok("Transcription", new { text, path = recording, chars = text?.Length ?? 0 });
        }
        catch (Exception ex)
        {
            return ToolResultJson.Error("TranscriptionFailed", ex.Message, "Transcription");
        }
    }

    [Description("Return the current date/time to support normalization of relative or partial dates. Format: LOCAL=yyyy-MM-ddTHH:mm:ssK;UTC=yyyy-MM-ddTHH:mm:ssZ")]
    public static string GetCurrentDateTime()
    {
        if (DateTimeService is null) throw new InvalidOperationException("Date/time service not initialized yet.");
        var raw = DateTimeService.GetCurrentDateTime();
        // Expect format LOCAL=...;UTC=...
        string? local = null; string? utc = null;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2)
            {
                if (kv[0].Equals("LOCAL", StringComparison.OrdinalIgnoreCase)) local = kv[1];
                if (kv[0].Equals("UTC", StringComparison.OrdinalIgnoreCase)) utc = kv[1];
            }
        }
        return ToolResultJson.Ok("DateTimeContext", new { local, utc, raw });
    }
}

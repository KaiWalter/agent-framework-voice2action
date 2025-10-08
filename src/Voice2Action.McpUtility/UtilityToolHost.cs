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
        return Transcription.TranscribeAsync(recording).GetAwaiter().GetResult();
    }

    [Description("Return the current date/time to support normalization of relative or partial dates. Format: LOCAL=yyyy-MM-ddTHH:mm:ssK;UTC=yyyy-MM-ddTHH:mm:ssZ")]
    public static string GetCurrentDateTime()
    {
        if (DateTimeService is null) throw new InvalidOperationException("Date/time service not initialized yet.");
        return DateTimeService.GetCurrentDateTime();
    }
}

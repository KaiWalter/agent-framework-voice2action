using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel; // For Description attributes to surface tool metadata
using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

internal static class UtilityAgentFactory
{
    public static ITextAgent Create(IChatClient chatClient, string promptPath, ITranscriptionService transcription, IDateTimeService dateTimeService)
    {
        var instructions = File.ReadAllText(Path.Combine(promptPath, "utility.md"));
        // Local functions with Description attributes so the tool metadata is preserved when
        // converted into AI function tools. This migrates the former Console static tool methods.
        [Description("Transcribe the given audio file (mp3/wav/m4a) using Azure OpenAI Whisper and return raw text.")]
        string TranscribeVoiceRecording([Description("Path to recording file.")] string recording)
        {
            if (string.IsNullOrWhiteSpace(recording)) throw new ArgumentException("Recording path empty", nameof(recording));
            if (!File.Exists(recording)) throw new FileNotFoundException("Audio file not found", recording);
            return transcription.TranscribeAsync(recording).GetAwaiter().GetResult();
        }

        [Description("Return the current date/time to support normalization of relative or partial dates. Format: LOCAL=yyyy-MM-ddTHH:mm:ssK;UTC=yyyy-MM-ddTHH:mm:ssZ")]
        string GetCurrentDateTime()
        {
            return dateTimeService.GetCurrentDateTime();
        }

        var options = new ChatClientAgentOptions(instructions)
        {
            ChatOptions = new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create(new Func<string, string>(TranscribeVoiceRecording)),
                    AIFunctionFactory.Create(new Func<string>(GetCurrentDateTime))
                ]
            }
        };
        var core = new ChatClientAgent(chatClient, options);
        return new BasicTextAgent("Utility", new[] { "TranscribeVoiceRecording(audioPath)", "GetCurrentDateTime()" }, core);
    }
}

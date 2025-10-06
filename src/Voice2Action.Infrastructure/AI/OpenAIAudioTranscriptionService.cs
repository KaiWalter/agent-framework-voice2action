using OpenAI.Audio;
using Azure.AI.OpenAI;
using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

public sealed class OpenAIAudioTranscriptionService : ITranscriptionService
{
    private readonly AzureOpenAIClient _client; // Using AzureOpenAIClient already configured for chat elsewhere
    private readonly string _deployment;

    public OpenAIAudioTranscriptionService(AzureOpenAIClient client, string deployment)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _deployment = deployment ?? throw new ArgumentNullException(nameof(deployment));
    }

    public async Task<string> TranscribeAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found", filePath);

        var audioClient = _client.GetAudioClient(_deployment);
        var opts = new AudioTranscriptionOptions
        {
            ResponseFormat = AudioTranscriptionFormat.Simple
        };

        var response = await audioClient.TranscribeAudioAsync(filePath, opts);
        var transcription = response.Value;
        if (string.IsNullOrWhiteSpace(transcription?.Text))
            throw new InvalidOperationException("Transcription returned empty text.");
        return transcription.Text!;
    }
}
